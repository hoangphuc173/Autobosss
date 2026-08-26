using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AutoBossGrabber;

/// <summary>
/// Auto Login Controller - Upgraded with features from tool tổng hợp (NpcScannerPlugin).
///
/// Major improvements over original:
/// - Reconnect tự động khi bị disconnect/kick (exponential backoff)
/// - Chọn server tự động thay vì bypass
/// - Hot-reload config mỗi 2 giây
/// - TriggerValueChanged qua Il2Cpp reflection (SendOnValueChanged)
/// - Phát hiện popup disconnect bằng normalize text tiếng Việt
/// - Ghi status file cho Launcher
/// - Log listener bắt "notingame" để trigger reconnect
/// </summary>
public class AutoLoginController : MonoBehaviour
{
	// ===== Login state flags (flag-based like tool tổng hợp, not enum state machine) =====
	private string _loginUsername = "";
	private string _loginPassword = "";
	private int _loginServer;
	private int _loginCharacter;
	private bool _autoHunting;
	private bool _autoLoginEnabled;

	// ===== Server select =====
	private bool _serverSelectDone;
	private int _serverSelectFailCount;
	private float _autoServerSelectTimer = -1f;

	// ===== Login =====
	private float _autoLoginTimer = -1f;
	private bool _autoLoginDone;

	// ===== Character select =====
	private float _autoCharSelectTimer = -1f;
	private bool _charSelectDone;
	private int _charSelectRetries;

	// ===== Enter game =====
	private float _autoEnterGameTimer = -1f;

	// ===== Scene tracking =====
	private string _currentScene = "";
	private bool _firstUpdate = true;
	private bool _titleChanged;

	// ===== Window sizing =====
	private int _windowWidth = -1;
	private int _windowHeight = -1;
	private int _currentResW;
	private int _currentResH;

	// ===== Config hot-reload =====
	private int _myAccountIndex;
	private float _lastConfigReadTime;
	private string _configPath = "";

	// ===== Status file =====
	private string _statusFilePath = "";
	private float _lastStatusWriteTime;

	// ===== Popup / Reconnect =====
	private float _popupCheckTimer;
	private bool _pendingResetFromLog;
	private float _nextAutoLoginAllowedTime;
	private float _lastResetTime = -999f;
	private float _lastReconnectWaitLogTime = -999f;
	private float _lastPopupKickHandledTime = -999f;
	private int _resetBurstCount;
	private float _resetBurstWindowStart = -1f;

	// ===== Auto farm after login =====
	private int _autoFarmRetryCount;
	private bool _autoFarmDone;

	// ===== Constants =====
	private const float RESET_DEBOUNCE_SECONDS = 2.5f;
	private const float RESET_BURST_WINDOW_SECONDS = 45f;
	private const int RESET_BURST_HARD_LIMIT = 6;
	private const float RECONNECT_BASE_DELAY_SECONDS = 2f;
	private const float RECONNECT_STEP_DELAY_SECONDS = 2f;
	private const float RECONNECT_MAX_DELAY_SECONDS = 25f;
	private const float POPUP_HANDLE_COOLDOWN_SECONDS = 4f;

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool SetWindowText(IntPtr hwnd, string lpString);

	public AutoLoginController(IntPtr ptr)
		: base(ptr)
	{
	}

	// ===== Public API for LogListener =====
	public void RequestResetFromLogSignal()
	{
		_pendingResetFromLog = true;
	}

	// =============================================
	// RESET & RECONNECT (from tool tổng hợp)
	// =============================================
	public void ResetAutoLogin()
	{
		float unscaledTime = Time.unscaledTime;
		if (unscaledTime - _lastResetTime < RESET_DEBOUNCE_SECONDS)
			return;

		_lastResetTime = unscaledTime;

		// Track burst resets within a window
		if (_resetBurstWindowStart < 0f || unscaledTime - _resetBurstWindowStart > RESET_BURST_WINDOW_SECONDS)
		{
			_resetBurstWindowStart = unscaledTime;
			_resetBurstCount = 0;
		}
		_resetBurstCount++;

		// Calculate reconnect delay with exponential backoff
		float stepDelay = (_resetBurstCount - 1) * RECONNECT_STEP_DELAY_SECONDS;
		float delay = Mathf.Min(RECONNECT_MAX_DELAY_SECONDS, RECONNECT_BASE_DELAY_SECONDS + stepDelay);
		if (_resetBurstCount >= RESET_BURST_HARD_LIMIT)
		{
			delay = Mathf.Max(delay, 35f);
		}

		_nextAutoLoginAllowedTime = Mathf.Max(_nextAutoLoginAllowedTime, unscaledTime + delay);

		Plugin.Log?.LogWarning($"[AutoLogin] Reset login flow (signal {_resetBurstCount}). Reconnect after {delay:F1}s.");

		// Reset all state flags
		_autoLoginDone = false;
		_serverSelectDone = false;
		_serverSelectFailCount = 0;
		_charSelectDone = false;
		_charSelectRetries = 0;
		_autoFarmDone = false;
		_autoFarmRetryCount = 0;
		_autoLoginTimer = _nextAutoLoginAllowedTime;
		_autoServerSelectTimer = -1f;
		_autoCharSelectTimer = -1f;
		_autoEnterGameTimer = -1f;
	}

	// =============================================
	// LIFECYCLE
	// =============================================
	public void Start()
	{
		try
		{
			Application.runInBackground = true;
			Application.targetFrameRate = 60;
			QualitySettings.vSyncCount = 0;
			Screen.sleepTimeout = -1;
		}
		catch { }

		// Register log listener for "notingame" detection
		try
		{
			BepInEx.Logging.Logger.Listeners.Add((ILogListener)(object)new AutoLoginLogListener(this));
			Plugin.Log?.LogInfo("[AutoLogin] Log listener registered.");
		}
		catch (Exception ex)
		{
			Plugin.Log?.LogWarning($"[AutoLogin] Failed to register log listener: {ex.Message}");
		}

		// Determine account index from env var
		string envIndex = Environment.GetEnvironmentVariable("VTDC_ACCOUNT_INDEX");
		if (!string.IsNullOrEmpty(envIndex) && int.TryParse(envIndex, out var result))
		{
			_myAccountIndex = result;
		}

		// Determine config path
		string gameRoot = Environment.GetEnvironmentVariable("VTDC_GAME_ROOT");
		if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
		{
			gameRoot = Environment.CurrentDirectory;
		}
		_configPath = Path.Combine(gameRoot, "accounts.json");

		// Create status directory
		string statusDir = Path.Combine(gameRoot, "status");
		try
		{
			if (!Directory.Exists(statusDir))
				Directory.CreateDirectory(statusDir);
		}
		catch { }
		_statusFilePath = Path.Combine(statusDir, $"status_{_myAccountIndex}.json");

		// Initial config load
		_currentScene = "";
		LoadConfigAndApplyRealtime(isFirstLoad: true);

		try
		{
			Scene activeScene = SceneManager.GetActiveScene();
			_currentScene = activeScene.name;
		}
		catch
		{
			_currentScene = "";
		}

		Plugin.Log?.LogInfo($"[AutoLogin] Khởi động hoàn tất. Account idx={_myAccountIndex} user='{_loginUsername}'");
	}

	// =============================================
	// UPDATE (main loop from tool tổng hợp architecture)
	// =============================================
	public void Update()
	{
		try
		{
			if (_firstUpdate)
			{
				Plugin.Log?.LogInfo("[AutoLogin] Update() đã chạy. Frame đầu tiên.");
				_firstUpdate = false;
			}

			// Hot-reload config every 2 seconds
			if (Time.unscaledTime - _lastConfigReadTime > 2f)
			{
				_lastConfigReadTime = Time.unscaledTime;
				LoadConfigAndApplyRealtime(isFirstLoad: false);
			}

			// Write status file every 3 seconds
			if (Time.unscaledTime - _lastStatusWriteTime > 3f)
			{
				_lastStatusWriteTime = Time.unscaledTime;
				WriteStatusFile();
			}

			// Set window title
			if (!_titleChanged)
			{
				try
				{
					IntPtr mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
					if (mainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(_loginUsername))
					{
						SetWindowText(mainWindowHandle, "Vũ Trụ Đại Chiến - [" + _loginUsername + "]");
						_titleChanged = true;
					}
				}
				catch { }
			}

			if (!_autoLoginEnabled)
				return;

			// Handle pending reset from log listener
			if (_pendingResetFromLog)
			{
				_pendingResetFromLog = false;
				ResetAutoLogin();
			}

			// Check for disconnect popups every 2 seconds
			if (Time.unscaledTime - _popupCheckTimer > 2f)
			{
				_popupCheckTimer = Time.unscaledTime;
				TryDismissPopups();
			}

			// Track scene changes
			Scene activeScene = SceneManager.GetActiveScene();
			string sceneName = activeScene.name;
			if (_currentScene != sceneName)
			{
				_currentScene = sceneName;
				Plugin.Log?.LogInfo($"[AutoLogin] Scene thay đổi: {_currentScene}");

				if (_currentScene == "LoginScene" || _currentScene == "InitScene")
				{
					// Reset login states when returning to login scene
					_serverSelectDone = false;
					_serverSelectFailCount = 0;
					_autoLoginDone = false;
					_charSelectDone = false;
					_autoFarmDone = false;
					_autoFarmRetryCount = 0;
					_autoServerSelectTimer = -1f;
					_autoLoginTimer = -1f;
					_autoEnterGameTimer = -1f;
					_autoCharSelectTimer = -1f;
				}
				else if (_currentScene == "MainGameScene")
				{
					// Successfully entered game - reset reconnect counters
					_nextAutoLoginAllowedTime = 0f;
					_resetBurstCount = 0;
					_resetBurstWindowStart = -1f;
					_lastReconnectWaitLogTime = -999f;
				}
			}

			// Apply window resolution
			if (_windowWidth > 0 && _windowHeight > 0 && (_currentResW != _windowWidth || _currentResH != _windowHeight))
			{
				Screen.fullScreen = false;
				Screen.fullScreenMode = (FullScreenMode)3;
				Screen.SetResolution(_windowWidth, _windowHeight, false);
				_currentResW = _windowWidth;
				_currentResH = _windowHeight;
			}

			// Handle auto farm after entering main game
			if (_currentScene == "MainGameScene" && !_autoFarmDone)
			{
				if (GameAPI.GetMyPlayer() != null)
				{
					HandleEnableAutoFarm();
				}
				return;
			}

			// Only process login flow on login/character scenes
			if (_currentScene != "LoginScene" && _currentScene != "InitScene"
				&& _currentScene != "SelectCharScene" && !_currentScene.ToLower().Contains("login"))
			{
				return;
			}

			// Check reconnect cooldown
			if (Time.unscaledTime < _nextAutoLoginAllowedTime)
			{
				if (Time.unscaledTime - _lastReconnectWaitLogTime > 3f)
				{
					_lastReconnectWaitLogTime = Time.unscaledTime;
					float remaining = _nextAutoLoginAllowedTime - Time.unscaledTime;
					Plugin.Log?.LogWarning($"[AutoLogin] Reconnect cooldown active: {remaining:F1}s");
				}
				return;
			}

			// Initialize timers
			if (_autoServerSelectTimer == -1f)
				_autoServerSelectTimer = Time.unscaledTime;
			if (_autoLoginTimer == -1f)
				_autoLoginTimer = Time.unscaledTime;

			// === STEP 1: Server select ===
			if (!_serverSelectDone && Time.unscaledTime - _autoServerSelectTimer > 0.35f)
			{
				if (HandleServerSelect())
				{
					_serverSelectDone = true;
					_serverSelectFailCount = 0;
					_autoLoginTimer = Time.unscaledTime;
					_nextAutoLoginAllowedTime = Mathf.Max(_nextAutoLoginAllowedTime, Time.unscaledTime + 1f);
				}
				else
				{
					_serverSelectFailCount++;
					if (_serverSelectFailCount >= 3 && IsLoginFormReady())
					{
						_serverSelectDone = true;
						_autoLoginTimer = Time.unscaledTime - 1f;
						Plugin.Log?.LogWarning("[AutoLogin] Server select không xác nhận, nhưng login form sẵn sàng. Tiếp tục login.");
					}
				}
				_autoServerSelectTimer = Time.unscaledTime + 1.5f;
			}
			else if (!_serverSelectDone)
			{
				return;
			}

			// === STEP 2: Fill credentials + Click Login ===
			if (!_autoLoginDone && Time.unscaledTime - _autoLoginTimer > 1.5f)
			{
				HandleAutoLogin();
				_autoLoginTimer = Time.unscaledTime + 2f;
				_nextAutoLoginAllowedTime = Mathf.Max(_nextAutoLoginAllowedTime, Time.unscaledTime + 2f);
			}

			// === STEP 3: Character select ===
			if (_autoLoginDone && !_charSelectDone && Time.unscaledTime - _autoCharSelectTimer > 1.5f)
			{
				HandleCharacterSelect();
			}

			// === STEP 4: Enter Game ===
			if (_charSelectDone)
			{
				if (_autoEnterGameTimer == -1f)
					_autoEnterGameTimer = Time.unscaledTime;
				if (Time.unscaledTime - _autoEnterGameTimer > 1.5f)
				{
					HandleEnterGame();
					_autoEnterGameTimer = Time.unscaledTime + 5f;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.LogError("[AutoLogin] Update Error: " + ex.Message);
		}
	}

	// =============================================
	// CONFIG LOADING (hot-reload from tool tổng hợp)
	// =============================================
	private void LoadConfigAndApplyRealtime(bool isFirstLoad)
	{
		try
		{
			// Also check plugin folder path (like existing code)
			string pluginPath = Path.Combine(Paths.PluginPath, "accounts.json");
			string pathToUse = File.Exists(_configPath) ? _configPath : (File.Exists(pluginPath) ? pluginPath : null);

			if (pathToUse == null)
			{
				if (isFirstLoad)
					Plugin.Log?.LogWarning($"[AutoLogin] Không tìm thấy accounts.json tại: {_configPath} hoặc {pluginPath}");
				return;
			}

			string json = File.ReadAllText(pathToUse);

			// Check if we need to parse currentAccountIndex from the JSON itself
			// (Useful if the Launcher didn't set VTDC_ACCOUNT_INDEX)
			string envIndex = Environment.GetEnvironmentVariable("VTDC_ACCOUNT_INDEX");
			if (string.IsNullOrEmpty(envIndex))
			{
				int parsedIndex = ExtractJsonInt(json, "currentAccountIndex", -1);
				if (parsedIndex >= 0)
				{
					_myAccountIndex = parsedIndex;
				}
			}

			string accountBlock = ExtractAccountBlock(json, _myAccountIndex);
			if (string.IsNullOrEmpty(accountBlock))
				return;

			_loginUsername = ExtractJsonString(accountBlock, "username");
			_loginPassword = ExtractJsonString(accountBlock, "password");
			_loginServer = ExtractJsonInt(accountBlock, "server", 0);
			_loginCharacter = Math.Max(0, ExtractJsonInt(accountBlock, "character", 1) - 1);
			_autoHunting = ExtractJsonBool(accountBlock, "autoHunting", false);
			_windowWidth = ExtractJsonInt(accountBlock, "windowWidth", 0);
			_windowHeight = ExtractJsonInt(accountBlock, "windowHeight", 0);

			// Also read settings block for window size (backward compat)
			if (_windowWidth <= 0 || _windowHeight <= 0)
			{
				int settingsIdx = json.IndexOf("\"settings\"", StringComparison.Ordinal);
				if (settingsIdx >= 0)
				{
					int braceStart = json.IndexOf('{', settingsIdx);
					int braceEnd = json.IndexOf('}', braceStart);
					if (braceStart >= 0 && braceEnd >= 0)
					{
						string settingsBlock = json.Substring(braceStart, braceEnd - braceStart + 1);
						int sw = ExtractJsonInt(settingsBlock, "windowWidth", 0);
						int sh = ExtractJsonInt(settingsBlock, "windowHeight", 0);
						if (sw > 0) _windowWidth = sw;
						if (sh > 0) _windowHeight = sh;
					}
				}
			}

			if (isFirstLoad)
			{
				_autoLoginEnabled = true;
				Plugin.Log?.LogInfo($"[AutoLogin] Config idx={_myAccountIndex} user='{_loginUsername}' server={_loginServer} char={_loginCharacter} hunt={_autoHunting} win={_windowWidth}x{_windowHeight}");
			}
		}
		catch (Exception ex)
		{
			if (isFirstLoad)
				Plugin.Log?.LogError($"[AutoLogin] Lỗi parse config: {ex.Message}");
		}
	}

	// =============================================
	// SERVER SELECT (from tool tổng hợp)
	// =============================================
	private bool HandleServerSelect()
	{
		try
		{
			int serverDisplay = Math.Max(1, _loginServer + 1);
			string desiredSv = $"sv{serverDisplay}";
			string desiredLabel = GetDesiredServerLabel(serverDisplay);

			// Try to find ChooseServerPanel
			ChooseServerPanel choosePanel = null;
			try
			{
				choosePanel = Object.FindObjectOfType<ChooseServerPanel>();
			}
			catch { }

			// If ChooseServerPanel is active, try to select from it
			if (choosePanel != null && ((Component)choosePanel).gameObject != null && ((Component)choosePanel).gameObject.activeInHierarchy)
			{
				if (TrySelectServerFromChoosePanel(choosePanel, serverDisplay, desiredLabel))
				{
					Plugin.Log?.LogInfo($"[AutoLogin] Selected server SV{serverDisplay} from ChooseServerPanel.");
					return true;
				}
			}

			// Check if server is already selected (look at UI text)
			if (IsServerAlreadySelected(serverDisplay, desiredLabel))
			{
				Plugin.Log?.LogInfo($"[AutoLogin] Server already selected: SV{serverDisplay}.");
				return true;
			}

			// Try to open the server selection panel
			if (TryOpenChooseServerPanel())
			{
				Plugin.Log?.LogInfo($"[AutoLogin] Opened choose-server panel for SV{serverDisplay}.");
				return false; // Will select on next tick
			}

			Plugin.Log?.LogWarning($"[AutoLogin] Could not open or detect choose-server panel for SV{serverDisplay}.");
		}
		catch (Exception ex)
		{
			Plugin.Log?.LogWarning("[AutoLogin] HandleServerSelect fail: " + ex.Message);
		}
		return false;
	}

	private string GetDesiredServerLabel(int serverDisplay)
	{
		return $"Vũ Trụ {serverDisplay}";
	}

	private bool IsServerAlreadySelected(int serverDisplay, string desiredLabel)
	{
		try
		{
			// Look for ExpandButton or server label text that shows current selection
			GameObject expandBtn = GameObject.Find("ExpandButton");
			if (expandBtn != null && expandBtn.activeInHierarchy)
			{
				string text = ReadTextFromGameObject(expandBtn);
				if (!string.IsNullOrEmpty(text))
				{
					string normalized = text.ToLower().Replace(" ", "");
					string desiredNorm = desiredLabel.ToLower().Replace(" ", "");
					if (normalized.Contains(desiredNorm) || normalized.Contains($"sv{serverDisplay}") || normalized.Contains($"vutru{serverDisplay}"))
					{
						Plugin.Log?.LogInfo($"[AutoLogin] ExpandButton already shows target '{desiredLabel}'.");
						return true;
					}
				}
			}
		}
		catch { }
		return false;
	}

	private bool TryOpenChooseServerPanel()
	{
		try
		{
			// Try clicking ExpandButton to open server selection
			GameObject expandBtn = GameObject.Find("ExpandButton");
			if (expandBtn != null && expandBtn.activeInHierarchy)
			{
				Button btn = expandBtn.GetComponent<Button>();
				if (btn != null)
				{
					SafeClickButton(btn);
					Plugin.Log?.LogInfo("[AutoLogin] Clicked ExpandButton to open server panel.");
					return true;
				}
			}
		}
		catch { }
		return false;
	}

	private bool TrySelectServerFromChoosePanel(ChooseServerPanel panel, int serverDisplay, string desiredLabel)
	{
		try
		{
			// Try to expand the panel first
			TryExpandChooseServerPanel(panel);

			// Look for visible buttons/text that match the desired server
			string normalizedTarget = desiredLabel.ToLower().Replace(" ", "");

			foreach (var btn in Resources.FindObjectsOfTypeAll<Button>())
			{
				if (btn == null || !((Component)btn).gameObject.activeInHierarchy)
					continue;

				// Check if this button is a child of the ChooseServerPanel
				if (!IsUnderGameObject(((Component)btn).gameObject, ((Component)panel).gameObject))
					continue;

				string text = ReadButtonText(btn);
				string normalized = text.ToLower().Replace(" ", "");
				string goName = ((Object)((Component)btn).gameObject).name.ToLower();

				if (normalized.Contains(normalizedTarget) || normalized.Contains($"vutru{serverDisplay}")
					|| goName.Contains($"sv{serverDisplay}") || goName.Contains($"server{serverDisplay}"))
				{
					SafeClickButton(btn);
					Plugin.Log?.LogInfo($"[AutoLogin] Clicked server button: '{text}' ({goName})");
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.LogWarning("[AutoLogin] TrySelectServerFromChoosePanel: " + ex.Message);
		}
		return false;
	}

	private void TryExpandChooseServerPanel(ChooseServerPanel panel)
	{
		try
		{
			GameObject panelGo = ((Component)panel).gameObject;
			// Look for an "ExpandButton" within the panel
			foreach (var btn in panelGo.GetComponentsInChildren<Button>(true))
			{
				if (btn == null || !((Component)btn).gameObject.activeInHierarchy)
					continue;
				string name = ((Object)((Component)btn).gameObject).name;
				if (name.Contains("Expand") || name.Contains("expand") || name.Contains("Arrow") || name.Contains("Drop"))
				{
					SafeClickButton(btn);
					break;
				}
			}
		}
		catch { }
	}

	// =============================================
	// AUTO LOGIN (fill credentials + click login)
	// =============================================
	private void HandleAutoLogin()
	{
		if (string.IsNullOrEmpty(_loginUsername) || string.IsNullOrEmpty(_loginPassword))
			return;

		try
		{
			bool userFilled = false;
			bool passFilled = false;

			// Try exact name match first
			GameObject userGo = GameObject.Find("UserName");
			GameObject passGo = GameObject.Find("Password");

			if (userGo != null)
			{
				TMP_InputField input = userGo.GetComponent<TMP_InputField>();
				if (input != null)
				{
					input.text = _loginUsername;
					TriggerValueChanged(input);
					userFilled = true;
					Plugin.Log?.LogInfo("[AutoLogin] Điền User (Exact: UserName)");
				}
			}

			if (passGo != null)
			{
				TMP_InputField input = passGo.GetComponent<TMP_InputField>();
				if (input != null)
				{
					input.text = _loginPassword;
					TriggerValueChanged(input);
					passFilled = true;
					Plugin.Log?.LogInfo("[AutoLogin] Điền Pass (Exact: Password)");
				}
			}

			// Fuzzy search if exact match failed
			if (!userFilled || !passFilled)
			{
				foreach (TMP_InputField item in Resources.FindObjectsOfTypeAll<TMP_InputField>())
				{
					if (item == null || !((Component)item).gameObject.activeInHierarchy)
						continue;

					string name = ((Object)((Component)item).gameObject).name;
					string lower = name.ToLower();

					if (!userFilled && (name == "UserName" || name == "Account" || lower.Contains("user") || lower.Contains("account")))
					{
						item.text = _loginUsername;
						TriggerValueChanged(item);
						userFilled = true;
						Plugin.Log?.LogInfo($"[AutoLogin] Điền User (Fuzzy: {name})");
					}
					else if (!passFilled && (name == "Password" || lower.Contains("pass")))
					{
						item.text = _loginPassword;
						TriggerValueChanged(item);
						passFilled = true;
						Plugin.Log?.LogInfo($"[AutoLogin] Điền Pass (Fuzzy: {name})");
					}
				}
			}

			if (!userFilled || !passFilled)
			{
				Plugin.Log?.LogWarning($"[AutoLogin] Chưa tìm thấy Input! user={userFilled} pass={passFilled}");
				return;
			}

			// Click Login button
			// Method 1: Exact name match
			GameObject loginGo = GameObject.Find("Login");
			if (loginGo != null && loginGo.activeInHierarchy)
			{
				Button loginBtn = loginGo.GetComponent<Button>();
				if (loginBtn != null)
				{
					((UnityEvent)loginBtn.onClick).Invoke();
					Plugin.Log?.LogInfo("[AutoLogin] Click Login (Exact: 'Login')");
					_autoLoginDone = true;
					_nextAutoLoginAllowedTime = Mathf.Max(_nextAutoLoginAllowedTime, Time.unscaledTime + 2f);
					_autoCharSelectTimer = Time.unscaledTime;
					return;
				}
			}

			// Method 2: FindButtonByNames
			Button namedBtn = FindButtonByNames(new[] {
				"Login", "LoginButton", "BtnLogin", "login",
				"loginbutton", "btnlogin", "dangnhap", "btn_dangnhap"
			});
			if (namedBtn != null)
			{
				((UnityEvent)namedBtn.onClick).Invoke();
				Plugin.Log?.LogInfo($"[AutoLogin] Click Login (Named: '{((Object)((Component)namedBtn).gameObject).name}')");
				_autoLoginDone = true;
				_nextAutoLoginAllowedTime = Mathf.Max(_nextAutoLoginAllowedTime, Time.unscaledTime + 2f);
				_autoCharSelectTimer = Time.unscaledTime;
				return;
			}

			// Method 3: Fuzzy text search on all buttons
			foreach (Button btn in Resources.FindObjectsOfTypeAll<Button>())
			{
				if (btn == null || !((Component)btn).gameObject.activeInHierarchy)
					continue;

				string goName = ((Object)((Component)btn).gameObject).name.ToLower();
				TextMeshProUGUI tmp = ((Component)btn).GetComponentInChildren<TextMeshProUGUI>();
				string btnText = "";
				if (tmp != null) btnText = ((TMP_Text)tmp).text?.ToLower() ?? "";

				bool nameMatch = goName == "login" || goName == "loginbutton" || goName == "btnlogin" || goName.Contains("dangnhap");
				bool textMatch = btnText.Contains("đăng nhập") || btnText.Contains("dang nhap") || btnText.Contains("login");

				if (nameMatch || textMatch)
				{
					// Exclude if under a Register parent
					Transform parent = ((Component)btn).transform.parent;
					bool isRegister = false;
					while (parent != null)
					{
						string pName = ((Object)parent).name ?? "";
						if (pName.Contains("Register") || pName.Contains("Signup"))
						{
							isRegister = true;
							break;
						}
						parent = parent.parent;
					}

					if (!isRegister)
					{
						((UnityEvent)btn.onClick).Invoke();
						Plugin.Log?.LogInfo($"[AutoLogin] Click Login (Fuzzy: '{goName}' text='{btnText}')");
						_autoLoginDone = true;
						_nextAutoLoginAllowedTime = Mathf.Max(_nextAutoLoginAllowedTime, Time.unscaledTime + 2f);
						_autoCharSelectTimer = Time.unscaledTime;
						return;
					}
				}
			}

			Plugin.Log?.LogWarning("[AutoLogin] KHÔNG tìm thấy nút Login!");
		}
		catch (Exception ex)
		{
			Plugin.Log?.LogError("[AutoLogin] HandleAutoLogin Error: " + ex.Message);
		}
	}

	// =============================================
	// CHARACTER SELECT
	// =============================================
	private void HandleCharacterSelect()
	{
		try
		{
			GameObject panel = GameObject.Find("CharacterChoosingPanel");
			if (panel == null || !panel.activeInHierarchy)
			{
				foreach (GameObject item in Resources.FindObjectsOfTypeAll<GameObject>())
				{
					if (item != null && ((Object)item).name == "CharacterChoosingPanel" && item.activeInHierarchy)
					{
						panel = item;
						break;
					}
				}
			}

			if (panel != null && panel.activeInHierarchy)
			{
				HorizontalLayoutGroup layout = panel.GetComponentInChildren<HorizontalLayoutGroup>();
				if (layout != null)
				{
					int idx = Math.Min(_loginCharacter, ((Component)layout).transform.childCount - 1);
					if (idx < 0) idx = 0;

					Transform child = ((Component)layout).transform.GetChild(idx);
					if (child != null && ((Component)child).gameObject.activeSelf)
					{
						Button btn = ((Component)child).GetComponent<Button>();
						Toggle toggle = ((Component)child).GetComponent<Toggle>();

						Plugin.Log?.LogInfo($"[AutoLogin] Chọn NV số {idx + 1}: '{((Object)child).name}'");

						if (btn != null)
						{
							((UnityEvent)btn.onClick).Invoke();
						}
						else if (toggle != null)
						{
							toggle.isOn = true;
							((UnityEvent<bool>)(object)toggle.onValueChanged).Invoke(true);
						}
					}
				}

				_charSelectDone = true;
				_autoEnterGameTimer = Time.unscaledTime;
			}
			else
			{
				_charSelectRetries++;
				float backoff = Mathf.Min(10f, 1.5f + _charSelectRetries);
				_autoCharSelectTimer = Time.unscaledTime + backoff;

				if (_charSelectRetries >= 8)
				{
					Plugin.Log?.LogWarning("[AutoLogin] Character panel not found too many times. Restarting login flow.");
					ResetAutoLogin();
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.LogWarning("[AutoLogin] HandleCharacterSelect: " + ex.Message);
		}
	}

	// =============================================
	// ENTER GAME
	// =============================================
	private void HandleEnterGame()
	{
		try
		{
			Button enterBtn = null;

			// Search in CharacterChoosingPanel first
			GameObject panel = GameObject.Find("CharacterChoosingPanel");
			if (panel == null)
			{
				foreach (GameObject item in Resources.FindObjectsOfTypeAll<GameObject>())
				{
					if (item != null && ((Object)item).name == "CharacterChoosingPanel" && item.activeInHierarchy)
					{
						panel = item;
						break;
					}
				}
			}

			if (panel != null && panel.activeInHierarchy)
			{
				var candidateButtons = new System.Collections.Generic.List<Button>();
				Il2CppArrayBase<Button> panelButtons = panel.GetComponentsInChildren<Button>(false);
				Button fallback = null;

				foreach (Button btn in panelButtons)
				{
					if (btn == null || !((Component)btn).gameObject.activeInHierarchy)
						continue;

					string goName = ((Object)((Component)btn).gameObject).name.ToLower();
					if (goName.Contains("create") || goName.Contains("tao") || goName.Contains("new"))
						continue;

					TextMeshProUGUI tmp = ((Component)btn).GetComponentInChildren<TextMeshProUGUI>();
					string text = (tmp != null && ((TMP_Text)tmp).text != null) ? ((TMP_Text)tmp).text.ToLower() : "";

					if (text.Contains("vào") || text.Contains("vao") || text.Contains("game")
						|| text.Contains("play") || text.Contains("enter") || text.Contains("start")
						|| text.Contains("bắt đầu") || text.Contains("bat dau")
						|| goName.Contains("play") || goName.Contains("vao"))
					{
						candidateButtons.Add(btn);
					}
					else if (fallback == null && !text.Contains("tạo") && !text.Contains("mới"))
					{
						fallback = btn;
					}
				}

				if (candidateButtons.Count > 0)
				{
					int idx = Math.Min(_loginCharacter, candidateButtons.Count - 1);
					if (idx < 0) idx = 0;
					enterBtn = candidateButtons[idx];
				}
				else if (fallback != null)
				{
					enterBtn = fallback;
				}
			}

			// Try known button names
			if (enterBtn == null)
			{
				string[] knownNames = { "PlayButton", "btnPlay", "btnEnter", "ButtonPlay", "btn_play", "StartGameButton", "StartButton", "EnterGame", "VaoGame" };
				foreach (string name in knownNames)
				{
					GameObject go = GameObject.Find(name);
					if (go != null && go.activeInHierarchy)
					{
						enterBtn = go.GetComponent<Button>();
						if (enterBtn != null) break;
					}
				}
			}

			// Fuzzy search all buttons
			if (enterBtn == null)
			{
				foreach (Button btn in Resources.FindObjectsOfTypeAll<Button>())
				{
					if (btn == null || !((Component)btn).gameObject.activeInHierarchy)
						continue;

					string goName = ((Object)((Component)btn).gameObject).name.ToLower();
					if (goName.Contains("close") || goName.Contains("back") || goName.Contains("exit"))
						continue;

					TextMeshProUGUI tmp = ((Component)btn).GetComponentInChildren<TextMeshProUGUI>();
					string text = (tmp != null && ((TMP_Text)tmp).text != null) ? ((TMP_Text)tmp).text.ToLower() : "";

					if (goName.Contains("play") || goName.Contains("start") || goName.Contains("enter")
						|| goName.Contains("vaogame") || goName.Contains("vao")
						|| text.Contains("vào") || text.Contains("vao") || text.Contains("game") || text.Contains("play") || text.Contains("bắt đầu"))
					{
						enterBtn = btn;
						break;
					}
				}
			}

			if (enterBtn != null)
			{
				Plugin.Log?.LogInfo("[AutoLogin] 👉 Click '" + ((Object)((Component)enterBtn).gameObject).name + "' → Vào Game!");
				((UnityEvent)enterBtn.onClick).Invoke();
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.LogWarning("[AutoLogin] HandleEnterGame fail: " + ex.Message);
		}
	}

	// =============================================
	// AUTO FARM (after entering game)
	// =============================================
	private void HandleEnableAutoFarm()
	{
		try
		{
			// Try to read 'running' or 'autoHunting' from accounts.json
			bool isRunning = _autoHunting;
			if (!isRunning && Plugin.Instance != null && Plugin.Instance.Config != null)
			{
				isRunning = Plugin.Instance.Config.Enabled;
			}
			
			if (!isRunning)
			{
				try 
				{
					string pathToUse = File.Exists(_configPath) ? _configPath : Path.Combine(Paths.PluginPath, "accounts.json");
					if (File.Exists(pathToUse))
					{
						string json = File.ReadAllText(pathToUse);
						string accountBlock = ExtractAccountBlock(json, _myAccountIndex);
						if (ExtractJsonBool(accountBlock, "running", false)) 
						{
							isRunning = true;
						}
					}
				} 
				catch {}
			}

			if (!isRunning)
			{
				_autoFarmDone = true;
				return;
			}

			_autoFarmRetryCount++;
			if (_autoFarmRetryCount < 5 * 60) // Wait ~5 seconds (frame-based)
				return;

			// Only try every second after initial wait
			if (_autoFarmRetryCount % 60 != 0)
				return;

			Plugin.Log?.LogInfo("Đang bật Auto Farm (Phím Q)... Lần thử: " + (_autoFarmRetryCount / 60));

			bool? isOn = AutoPickupLite.TryReadAutoAttackOn();
			
			// NẾU game vừa reconnect xong mà nút Q đã hiện sáng SẴN (game bị lỗi lưu trạng thái ảo)
			// thì nhân vật sẽ đứng im không đánh. Do đó nếu thấy nó sáng sẵn ở lần thử đầu tiên, ta phải tắt đi bật lại!
			if (_autoFarmRetryCount == 5 * 60 && isOn.HasValue && isOn.Value)
			{
				Plugin.Log?.LogWarning("Nút Auto Farm đang sáng sẵn ảo do reconnect! Đang thử tắt đi bật lại...");
				AutoPickupLite.TapAttackKey(); // Tắt nó đi
				// Sẽ thử lại ở frame sau (1 giây sau)
				return;
			}

			if (isOn.HasValue && isOn.Value)
			{
				Plugin.Log?.LogInfo("Đã bật Auto Farm thành công.");
				_autoFarmDone = true;
			}
			else
			{
				AutoPickupLite.TapAttackKey();

				if (_autoFarmRetryCount >= 30 * 60) // 30 seconds max
				{
					Plugin.Log?.LogWarning("Không thể xác nhận Auto Farm đã bật sau 30 lần thử.");
					_autoFarmDone = true;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.LogWarning("EnableAutoFarm error: " + ex.Message);
			_autoFarmDone = true;
		}
	}

	// =============================================
	// TriggerValueChanged (Il2Cpp reflection - from tool tổng hợp)
	// =============================================
	private void TriggerValueChanged(TMP_InputField input)
	{
		// Method 1: Try Il2Cpp reflection to call SendOnValueChanged (more reliable)
		try
		{
			MethodInfo method = ((Object)input).GetIl2CppType().GetMethod("SendOnValueChanged", (BindingFlags)36);
			if (method != (MethodInfo)null)
			{
				((MethodBase)method).Invoke((Il2CppSystem.Object)(object)input, (Il2CppReferenceArray<Il2CppSystem.Object>)null);
				Plugin.Log?.LogInfo("[AutoLogin] SendOnValueChanged OK");
				return;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.LogWarning("[AutoLogin] SendOnValueChanged fail: " + ex.Message);
		}

		// Method 2: Fallback to onValueChanged.Invoke
		try
		{
			((UnityEvent<string>)(object)input.onValueChanged).Invoke(input.text);
		}
		catch { }
	}

	// =============================================
	// POPUP DISMISS (disconnect detection - from tool tổng hợp)
	// =============================================
	private void TryDismissPopups()
	{
		try
		{
			// Step 1: Check if any disconnect text is visible
			bool hasDisconnect = false;

			foreach (TextMeshProUGUI tmp in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
			{
				if (tmp == null || ((TMP_Text)tmp).text == null || !((Component)tmp).gameObject.activeInHierarchy)
					continue;
				if (IsDisconnectText(NormalizePopupText(((TMP_Text)tmp).text)))
				{
					hasDisconnect = true;
					break;
				}
			}

			if (!hasDisconnect)
			{
				foreach (Text legacyText in Resources.FindObjectsOfTypeAll<Text>())
				{
					if (legacyText == null || legacyText.text == null || !((Component)legacyText).gameObject.activeInHierarchy)
						continue;
					if (IsDisconnectText(NormalizePopupText(legacyText.text)))
					{
						hasDisconnect = true;
						break;
					}
				}
			}

			if (!hasDisconnect)
				return;

			// Step 2: Cooldown check
			float now = Time.unscaledTime;
			if (now - _lastPopupKickHandledTime < POPUP_HANDLE_COOLDOWN_SECONDS)
				return;

			// Step 3: Find and click dismiss button
			bool clicked = false;
			foreach (Button btn in Resources.FindObjectsOfTypeAll<Button>())
			{
				if (btn == null || !((Component)btn).gameObject.activeInHierarchy || !((Selectable)btn).interactable)
					continue;

				string text = "";
				TextMeshProUGUI tmp = ((Component)btn).GetComponentInChildren<TextMeshProUGUI>();
				if (tmp != null && ((TMP_Text)tmp).text != null)
				{
					text = NormalizePopupText(((TMP_Text)tmp).text);
				}
				else
				{
					Text legacyText = ((Component)btn).GetComponentInChildren<Text>();
					if (legacyText != null && legacyText.text != null)
					{
						text = NormalizePopupText(legacyText.text);
					}
				}

				if (text.Contains("dong") || text.Contains("xong") || text.Contains("quayve")
					|| text == "ok" || text.Contains("xacnhan"))
				{
					Plugin.Log?.LogWarning($"[AutoLogin] Closing disconnect popup: {((Object)((Component)btn).gameObject).name} (text={text})");
					SafeClickButton(btn);
					_lastPopupKickHandledTime = now;
					clicked = true;
					break;
				}
			}

			if (clicked)
			{
				ResetAutoLogin();
			}
		}
		catch (Exception ex)
		{
			Plugin.Log?.LogError($"[AutoLogin] Popup dismiss error: {ex.Message}");
		}
	}

	private static bool IsDisconnectText(string normalizedText)
	{
		if (string.IsNullOrEmpty(normalizedText))
			return false;

		return normalizedText.Contains("hethan")
			|| normalizedText.Contains("matketnoi")
			|| normalizedText.Contains("ngatket")
			|| normalizedText.Contains("dangnhaplai")
			|| normalizedText.Contains("noikhac")
			|| normalizedText.Contains("notingame")
			|| normalizedText.Contains("session")
			|| normalizedText.Contains("expired");
	}

	private static string NormalizePopupText(string text)
	{
		if (string.IsNullOrEmpty(text))
			return string.Empty;

		string lower = text.ToLowerInvariant()
			.Replace("đ", "d")
			.Replace("Đ", "d")
			.Normalize(NormalizationForm.FormD);

		var sb = new StringBuilder(lower.Length);
		foreach (char c in lower)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
				&& !char.IsWhiteSpace(c) && c != '\n' && c != '\r' && c != '\t')
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}

	// =============================================
	// HELPERS
	// =============================================
	private bool IsLoginFormReady()
	{
		try
		{
			bool hasUser = false;
			bool hasPass = false;

			GameObject userGo = GameObject.Find("UserName");
			GameObject passGo = GameObject.Find("Password");

			hasUser = userGo != null && userGo.activeInHierarchy && userGo.GetComponent<TMP_InputField>() != null;
			hasPass = passGo != null && passGo.activeInHierarchy && passGo.GetComponent<TMP_InputField>() != null;

			if (hasUser && hasPass) return true;

			foreach (TMP_InputField item in Resources.FindObjectsOfTypeAll<TMP_InputField>())
			{
				if (item == null || ((Component)item).gameObject == null || !((Component)item).gameObject.activeInHierarchy)
					continue;

				string name = ((Object)((Component)item).gameObject).name ?? "";
				string lower = name.ToLowerInvariant();

				if (!hasUser && (name == "UserName" || name == "Account" || lower.Contains("user") || lower.Contains("account")))
					hasUser = true;
				if (!hasPass && (name == "Password" || lower.Contains("pass")))
					hasPass = true;

				if (hasUser && hasPass) return true;
			}
		}
		catch { }
		return false;
	}

	private Button FindButtonByNames(string[] names)
	{
		for (int i = 0; i < names.Length; i++)
		{
			GameObject go = GameObject.Find(names[i]);
			if (go != null && go.activeInHierarchy)
			{
				Button btn = go.GetComponent<Button>();
				if (btn != null) return btn;
			}
		}
		return null;
	}

	private void SafeClickButton(Button b)
	{
		if (b == null) return;
		try
		{
			((UnityEvent)b.onClick).Invoke();
		}
		catch { }
		try
		{
			var eventData = new PointerEventData(EventSystem.current)
			{
				button = PointerEventData.InputButton.Left
			};
			ExecuteEvents.Execute<IPointerDownHandler>(((Component)b).gameObject, eventData, ExecuteEvents.pointerDownHandler);
			ExecuteEvents.Execute<IPointerClickHandler>(((Component)b).gameObject, eventData, ExecuteEvents.pointerClickHandler);
			ExecuteEvents.Execute<IPointerUpHandler>(((Component)b).gameObject, eventData, ExecuteEvents.pointerUpHandler);
		}
		catch { }
	}

	private string ReadButtonText(Button btn)
	{
		if (btn == null) return "";
		try
		{
			TextMeshProUGUI tmp = ((Component)btn).GetComponentInChildren<TextMeshProUGUI>();
			if (tmp != null && ((TMP_Text)tmp).text != null)
				return ((TMP_Text)tmp).text;
			Text legacyText = ((Component)btn).GetComponentInChildren<Text>();
			if (legacyText != null && legacyText.text != null)
				return legacyText.text;
		}
		catch { }
		return "";
	}

	private string ReadTextFromGameObject(GameObject go)
	{
		if (go == null) return "";
		try
		{
			TextMeshProUGUI tmp = go.GetComponentInChildren<TextMeshProUGUI>();
			if (tmp != null && ((TMP_Text)tmp).text != null)
				return ((TMP_Text)tmp).text;
			Text legacyText = go.GetComponentInChildren<Text>();
			if (legacyText != null && legacyText.text != null)
				return legacyText.text;
		}
		catch { }
		return "";
	}

	private bool IsUnderGameObject(GameObject child, GameObject root)
	{
		if (child == null || root == null) return false;
		Transform t = child.transform;
		while (t != null)
		{
			if (t.gameObject == root) return true;
			t = t.parent;
		}
		return false;
	}

	// =============================================
	// STATUS FILE (from tool tổng hợp)
	// =============================================
	private void WriteStatusFile()
	{
		if (string.IsNullOrEmpty(_statusFilePath))
			return;
		try
		{
			string mapName = _currentScene;
			try
			{
				GameObject miniMap = GameObject.Find("MiniMap");
				if (miniMap != null)
				{
					foreach (var tmp in miniMap.GetComponentsInChildren<TextMeshProUGUI>(true))
					{
						if (tmp != null && !string.IsNullOrEmpty(((TMP_Text)tmp).text)
							&& ((TMP_Text)tmp).text.Length > 1 && ((TMP_Text)tmp).text.Length < 30)
						{
							mapName = ((TMP_Text)tmp).text.Trim();
							break;
						}
					}
				}
			}
			catch { }

			string status = _autoHunting ? "Auto Hunting" : "Đang nghỉ";
			if (_currentScene != "MainGameScene")
				status = "Login...";

			string contents = "{\n" +
				$"  \"username\": \"{EscapeJson(_loginUsername)}\",\n" +
				$"  \"scene\": \"{EscapeJson(_currentScene)}\",\n" +
				$"  \"map\": \"{EscapeJson(mapName)}\",\n" +
				$"  \"status\": \"{EscapeJson(status)}\",\n" +
				$"  \"timestamp\": \"{DateTime.Now:HH:mm:ss}\"\n" +
				"}";

			string tmpPath = _statusFilePath + ".tmp";
			File.WriteAllText(tmpPath, contents);
			File.Copy(tmpPath, _statusFilePath, true);
			File.Delete(tmpPath);
		}
		catch { }
	}

	// =============================================
	// JSON PARSING HELPERS (from tool tổng hợp - more robust)
	// =============================================
	private string ExtractAccountBlock(string json, int index)
	{
		int accountsIdx = json.IndexOf("\"accounts\"", StringComparison.Ordinal);
		if (accountsIdx < 0) return "";

		int arrayStart = -1;
		for (int i = accountsIdx; i < json.Length; i++)
		{
			if (json[i] == '[') { arrayStart = i; break; }
		}
		if (arrayStart < 0) return "";

		int currentIdx = -1;
		int searchFrom = arrayStart;
		for (; currentIdx < index; currentIdx++)
		{
			searchFrom = json.IndexOf("{", searchFrom + 1, StringComparison.Ordinal);
			if (searchFrom < 0) return "";
		}

		// Find matching closing brace (handle nested objects)
		int depth = 0;
		for (int k = searchFrom; k < json.Length; k++)
		{
			if (json[k] == '{') depth++;
			else if (json[k] == '}')
			{
				depth--;
				if (depth == 0)
					return json.Substring(searchFrom, k - searchFrom + 1);
			}
		}
		return "";
	}

	private string ExtractJsonString(string block, string key)
	{
		string marker = "\"" + key + "\"";
		int idx = block.IndexOf(marker, StringComparison.Ordinal);
		if (idx < 0) return "";

		int colon = block.IndexOf(':', idx);
		if (colon < 0) return "";

		int q1 = block.IndexOf('"', colon);
		int q2 = block.IndexOf('"', q1 + 1);
		if (q1 > 0 && q2 > 0)
			return block.Substring(q1 + 1, q2 - q1 - 1);
		return "";
	}

	private int ExtractJsonInt(string block, string key, int defaultValue)
	{
		string marker = "\"" + key + "\"";
		int idx = block.IndexOf(marker, StringComparison.Ordinal);
		if (idx < 0) return defaultValue;

		int colon = block.IndexOf(':', idx);
		if (colon < 0) return defaultValue;

		int numStart = -1;
		for (int i = colon + 1; i < block.Length; i++)
		{
			if (char.IsDigit(block[i]) || block[i] == '-') { numStart = i; break; }
		}
		if (numStart < 0) return defaultValue;

		int numEnd = numStart;
		while (numEnd < block.Length && (char.IsDigit(block[numEnd]) || block[numEnd] == '-'))
			numEnd++;

		if (int.TryParse(block.Substring(numStart, numEnd - numStart), out var result))
			return result;
		return defaultValue;
	}

	private bool ExtractJsonBool(string block, string key, bool defaultValue)
	{
		string marker = "\"" + key + "\"";
		int idx = block.IndexOf(marker, StringComparison.Ordinal);
		if (idx < 0) return defaultValue;

		int colon = block.IndexOf(':', idx);
		if (colon < 0) return defaultValue;

		int i = colon + 1;
		while (i < block.Length && char.IsWhiteSpace(block[i])) i++;
		if (i >= block.Length) return defaultValue;

		string rest = block.Substring(i);
		if (rest.StartsWith("true", StringComparison.OrdinalIgnoreCase)) return true;
		if (rest.StartsWith("false", StringComparison.OrdinalIgnoreCase)) return false;
		return defaultValue;
	}

	private static string EscapeJson(string s)
	{
		if (string.IsNullOrEmpty(s)) return "";
		return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
	}
}
