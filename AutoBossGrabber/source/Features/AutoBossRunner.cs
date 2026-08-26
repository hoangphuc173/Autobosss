using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// State machine chính của AutoBossGrabber.
/// Attach vào BasePlugin.gameObject (DontDestroyOnLoad, scene active) — Update() tick bình thường.
///
/// Flow:
///   Idle → FarmTown (user F1)
///     FarmTown → TeleportToBossMap (khi thấy boss)
///       TeleportToBossMap → ZoneScanLoop (khi đã ở map boss)
///         ZoneScanLoop → MoveToBoss (khi tìm thấy boss)
///           MoveToBoss → CombatBoss
///             CombatBoss → LootDrops (boss chết)
///               LootDrops → TeleportHome (timeout)
///                 TeleportHome → FarmTown
///
/// Hotkeys:
///   F1 - toggle ON/OFF
///   F2 - dump UI/runtime
///   F3 - test teleport
///   F4 - test next zone
///   F5 - test go home
///   F6 - dump network classes
/// </summary>
public class AutoBossRunner : MonoBehaviour
{
    public AutoBossConfig Config;
    public AutoBossState State = AutoBossState.Idle;

    private float _stateTimer = 0f;
    private float _scanTimer = 0f;
    private float _noItemTimer = 0f;
    private float _zoneSwitchTimer = 0f;  // cooldown giữa các lần đổi zone
    private float _enteredZoneAt = 0f;
    private float _lastZoneScanLogAt = -999f;  // throttle log ZoneScanLoop
    private float _lastPendingPollLogAt = -999f;  // throttle log "zone switch pending / waiting render"
    private float _lastMoveToBossLogAt = -999f;
    private float _lastReturnDebugLogAt = -999f;  // throttle log DEBUG ReturnToFarmMap (gọi mỗi tick)
    // Removed: _lastMobCountAt, _cachedAliveCount - now using ZoneSwitcher.GetCachedMobCount()
    private int _zoneAttempts = 0;
    private int _currentMapIndex = 0;
    private object _currentBoss = null;
    private float _bossMissingTimer = 0f;

    // === Portal chain index: bước hiện tại trong PortalChainMaps (0 = bước đầu tiên) ===
    // Dùng cho boss cần đi bộ qua nhiều phòng (ví dụ: Coller ở Pháo đài Frizar).
    // Reset khi bắt đầu TeleportToBossMap mới; KHÔNG reset khi Transition(WalkToPortal).
    private int _portalChainIndex = 0;

    // === Portal walk state (pattern từ Tool_Om_Boss AutoRedRibbon) ===
    private float _cachedGatewayX = 0f;
    private float _cachedGatewayY = 0f;
    private float _lastPortalMoveIssuedAt = -999f;
    private bool _portalMoveIssued = false;
    private float _lastPortalProgressX = 0f;
    private float _lastPortalProgressY = 0f;
    private float _lastPortalProgressDist = float.MaxValue;
    private float _lastPortalProgressCheckAt = -999f;
    private int _portalStallCount = 0;
    private string _lastPortalSignature = "";

    // Portal side-nudge (Tool_Om_Boss pattern): khi stalled, thử lệch trái/phải
    private int _portalNudgePhase = 0;  // 0=none, 1=left, 2=right
    private float _lastPortalNudgeAt = -999f;

    // === Boss approach state (same style as gateway walk) ===
    private float _cachedBossX = 0f;
    private float _cachedBossY = 0f;
    private float _lastBossMoveIssuedAt = -999f;
    private bool _bossMoveIssued = false;
    private float _lastBossProgressX = 0f;
    private float _lastBossProgressY = 0f;
    private float _lastBossProgressDist = float.MaxValue;
    private float _lastBossProgressCheckAt = -999f;
    private int _bossStallCount = 0;
    private int _bossNudgePhase = 0;
    private float _lastBossNudgeAt = -999f;
    private bool _bossAttackEngaged = false;

    // === Farm resume state (vòng đời Q RIÊNG, không dùng chung boss combat) ===
    private bool _farmAttackEngaged = false;
    private bool _captchaTriggered = false;

    // === Saved farm context (để "go back" đúng map/khu farm cũ sau khi săn boss) ===
    private string _savedFarmMap = "";
    private int _savedFarmZone = -1;
    private ZoneSwitcher.ZoneTab _savedFarmZoneTab = ZoneSwitcher.ZoneTab.Normal;
    private bool _hasSavedFarmContext = false;
    private bool _restoreStarted = false;

    // === FIX: Lưu thông tin boss profile để có thể reverse route ===
    private string _savedFarmAnchor = "";               // FastTravelAnchorMap của boss đang farm
    private List<string> _savedFarmPortalChain = null;  // PortalChainMaps của boss đang farm
    private int _savedFarmChainIndex = -1;              // Vị trí farm trong chain (-1 = không trong chain)

    // === Anti-spam teleport ===
    // Khi đã click teleport thành công, KHÔNG được gọi lại menu trong vài giây
    // (vì scene đang load, panel biến mất tạm thời, gọi lại sẽ kẹt menu cũ)
    private bool _teleportInProgress = false;
    private float _teleportCooldownUntil = 0f;
    private string _teleportTargetMap = "";
    private string _sceneNameBeforeTeleport = "";   // scene trước khi click teleport
    private float _teleportClickedAt = 0f;           // thời điểm click teleport

    // === Auto-dump timer (thay cho AutoBossBootstrapHelper) ===
    private bool _firstUpdateLogged = false;
    private float _dumpStart = -1f;
    private bool _d1, _d2, _d3;

    private const float ZoneSwitchCooldown = 0.45f; // thời gian tối thiểu giữa các lần đổi khu
    private const float ZoneEmptyDwellSec = 0.20f;  // khu trống: kiểm tra nhanh rồi chuyển
    private const float ZoneMobDwellSec = 1.20f;    // khu có mob: vẫn đủ thời gian cập nhật object
    private const float ZoneBossHintDwellSec = 0.60f; // khu có dấu hiệu boss: quét lại trước khi chuyển
    private const float DeadRecoveryTimeout = 10f; // giây chờ sau DeadRecovery
    private const float FarmResumeDurationSec = 30f; // farm lại bao lâu trước khi đi săn boss tiếp
    private const float ReturnToFarmTimeoutSec = 20f;
    private const float RestoreFarmZoneTimeoutSec = 30f; // timeout riêng cho RestoreFarmZone (zone switch mất ~3-6s/khu)

    private void Awake()
    {
        if (Config == null) Config = Plugin.Instance?.Config ?? new AutoBossConfig();
        if (Plugin.Instance != null) Plugin.Instance.Runner = this;

        // Initialize boss notification hook
        BossNotificationHook.Initialize(Plugin.Log, Config);
        BossNotificationHook.OnBossNotificationDetected += OnBossNotificationDetectedWithText;

        MessageHook.OnBossDetected += OnBossMessageDetected;
        MessageHook.Install(Plugin.Log, Config);

        // Auto-load config đã lưu từ lần trước (skill triggers, thông số chiến đấu, ...)
        AutoBossUI.TryAutoLoad();

        // Add VirtualMouse component and start Python Bot
        gameObject.AddComponent<VirtualMouse>();
        CaptchaManager.StartPythonBot();

        // Cố định kích thước cửa sổ game để Bot Python nhận diện và click chuẩn xác
        // Tắt ép buộc độ phân giải 1000x500 vì AI bot có thể nhận diện ở mọi kích thước
        // Screen.SetResolution(1000, 500, false);
    }

    private void OnDestroy()
    {
        BossNotificationHook.OnBossNotificationDetected -= OnBossNotificationDetectedWithText;
        MessageHook.OnBossDetected -= OnBossMessageDetected;
        MessageHook.Flush();

        // Stop python bot
        CaptchaManager.StopPythonBot();
    }

    /// <summary>
    /// Tìm BossProfile khớp với text thông báo boss (normalize, không phân biệt dấu/hoa-thường).
    /// Trả về null nếu không khớp profile nào.
    /// </summary>
    private BossProfile MatchBossProfile(string notificationText)
    {
        if (Config.BossProfiles == null || string.IsNullOrEmpty(notificationText)) return null;

        string norm = NormalizeText(notificationText);
        foreach (var profile in Config.BossProfiles)
        {
            if (profile?.BossNames == null) continue;
            foreach (var name in profile.BossNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (norm.IndexOf(NormalizeText(name), StringComparison.Ordinal) >= 0)
                    return profile;
                // Thêm: khớp từng từ đủ dài (giống MessageHook.BuildBossKeywords)
                foreach (var word in NormalizeText(name).Split(' '))
                    if (word.Length >= 4 && norm.IndexOf(word, StringComparison.Ordinal) >= 0)
                        return profile;
            }
        }
        return null;
    }

    /// <summary>Bỏ dấu tiếng Việt + lowercase (dùng nội bộ để match profile).</summary>
    private static string NormalizeText(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var form = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(form.Length);
        foreach (var c in form)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) ==
                System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Replace('đ', 'd').Trim();
    }

    /// <summary>
    /// Áp dụng BossProfile vào Config.Active fields.
    /// Sau khi gọi hàm này, RunTeleportToBossMap/RunWalkToPortal sẽ dùng đúng route.
    /// </summary>
    private void ApplyBossProfile(BossProfile profile)
    {
        if (profile == null) return;
        Plugin.Log.LogWarning($"[AutoBoss] ApplyBossProfile: '{profile.ProfileName}' " +
            $"bossMap={string.Join(",", profile.BossMapNames ?? new System.Collections.Generic.List<string>())} " +
            $"anchor='{profile.FastTravelAnchorMap}' " +
            $"chain=[{string.Join("->", profile.PortalChainMaps ?? new System.Collections.Generic.List<string>())}]");

        if (profile.BossNames != null)       Config.BossNames = profile.BossNames;
        if (profile.BossMapNames != null)    Config.BossMapNames = profile.BossMapNames;
        Config.FastTravelAnchorMap = profile.FastTravelAnchorMap ?? "";
        Config.PortalChainMaps     = profile.PortalChainMaps ?? new System.Collections.Generic.List<string>();

        // Reset cờ direct-Cung vì đang chuyển sang boss/map khác
        _tryDirectToCung = false;
        _portalChainIndex = 0;
    }

    /// <summary>Handler từ BossNotificationHook (UI text scan fallback).</summary>
    private void OnBossNotificationDetectedWithText(string notificationText)
    {
        if (Config == null || Config.Enabled || State != AutoBossState.Idle) return;
        if (!Config.AutoDetectBossNotification) return;

        var profile = MatchBossProfile(notificationText);
        if (profile != null)
            ApplyBossProfile(profile);
        else
            Plugin.Log.LogWarning($"[AutoBoss] BossNotificationHook: no profile matched for '{notificationText}', using current config");

        Plugin.Log.LogWarning("[AutoBoss] *** AUTO-STARTING TOOL *** (boss detected via UI hook)");
        EnableAutoFarmMode();
    }

    /// <summary>Handler từ MessageHook (packet hook, nguồn chính).</summary>
    private void OnBossMessageDetected(string info)
    {
        Plugin.Log.LogWarning($"[AutoBoss] Boss notification via MESSAGE HOOK -> {info}");

        if (Config == null)
        {
            Plugin.Log.LogWarning("[AutoBoss] Config is null -> cannot auto-start");
            return;
        }
        if (State != AutoBossState.Idle)
        {
            Plugin.Log.LogInfo($"[AutoBoss] Tool already running (State={State}) -> ignore boss notification");
            return;
        }
        if (!Config.AutoDetectBossNotification)
        {
            Plugin.Log.LogWarning("[AutoBoss] AutoDetectBossNotification is disabled -> manual F1 required");
            return;
        }

        var profile = MatchBossProfile(info);
        if (profile != null)
            ApplyBossProfile(profile);
        else
            Plugin.Log.LogWarning($"[AutoBoss] MessageHook: no profile matched for '{info}', using current config");

        Plugin.Log.LogWarning("[AutoBoss] *** AUTO-STARTING TOOL *** (boss detected via message hook)");
        EnableAutoFarmMode();
    }

    private void Update()
    {
        // --- First-tick log ---
        if (!_firstUpdateLogged)
        {
            _firstUpdateLogged = true;
            Plugin.Log.LogInfo($"[AutoBoss] Update() FIRST TICK (frame={Time.frameCount}, enabled={Config?.Enabled})");

            // Trigger dump ngay 1 lần để verify pipeline hoạt động
            RunAutoDump("ui_panel_dump_0.txt", "runtime_types_0.txt");
        }

        // --- Auto-dump timer: 8s / 20s / 40s sau khi attach ---
        if (_dumpStart < 0f) _dumpStart = Time.time;
        float dt = Time.time - _dumpStart;

        if (!_d1 && dt >= 8f)
        {
            _d1 = true;
            RunAutoDump("ui_panel_dump_1.txt", "runtime_types_1.txt");
        }
        if (!_d2 && dt >= 20f)
        {
            _d2 = true;
            RunAutoDump("ui_panel_dump_2.txt", "runtime_types_2.txt");
        }
        if (!_d3 && dt >= 40f)
        {
            _d3 = true;
            RunAutoDump("ui_panel_dump_3.txt", "runtime_types_3.txt");
        }

        // --- Hotkeys (luôn chạy dù Config.Enabled hay không) ---
        HandleHotkeys();

        // --- MESSAGE HOOK (nguon chinh): flush log + ban event boss o main thread ---
        MessageHook.Tick();

        // --- UI TEXT SCAN (fallback): chi dung khi message hook KHONG cai duoc ---
        // Message hook doc truc tiep packet nen chinh xac hon; scan UI chi de du phong.
        if (!MessageHook.IsInstalled)
            BossNotificationHook.CheckActiveTexts();

        // --- AUTO-DETECT BOSS NOTIFICATION (cũ - dùng polling 1s) ---
        // Tắt fallback cũ vì dễ false positive; chỉ dùng BossNotificationHook đã lọc chặt hơn ở trên
        if (false && Config != null && Config.AutoDetectBossNotification && !Config.Enabled && State == AutoBossState.Idle)
        {
            if (BossNotificationDetector.DetectBossNotification())
            {
                Plugin.Log.LogInfo("[AutoBoss] Boss notification detected (polling) -> AUTO-STARTING tool");
                Config.Enabled = true;
                Transition(AutoBossState.TeleportToBossMap);
                _currentMapIndex = 0;
                return;
            }
        }

        if (Config == null || !Config.Enabled) return;

        // Auto Item loop
        AutoItemManager.Instance.Update(Config);

        _stateTimer += Time.deltaTime;

        // === Global teleport guard: nếu đã click teleport và scene đã đổi → hoàn tất ===
        // Đặt ở đây để bắt được cả khi state đã chuyển sang FarmTown/WalkToPortal
        if (_teleportInProgress && !string.IsNullOrEmpty(_sceneNameBeforeTeleport))
        {
            string curScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(curScene) && curScene != _sceneNameBeforeTeleport)
            {
                Plugin.Log.LogInfo($"[AutoBoss] Global teleport guard: scene '{_sceneNameBeforeTeleport}' -> '{curScene}'. Target='{_teleportTargetMap}'.");
                _teleportInProgress = false;
                _sceneNameBeforeTeleport = "";
                MapTransporter.ResetTeleportSession();
                MapTransporter.CloseTeleportPanel();

                // Nếu đang trong TeleportHome → FarmTown
                // Nếu đang trong TeleportToBossMap → ZoneScanLoop hoặc WalkToPortal
                // Nếu đang trong ReturnToFarmMap → RestoreFarmZone hoặc ReverseWalkToFarm
                if (State == AutoBossState.TeleportHome)
                {
                    Transition(AutoBossState.FarmTown);
                    return;
                }
                if (State == AutoBossState.ReturnToFarmMap)
                {
                    // Check xem có cần reverse walk không
                    if (_savedFarmChainIndex >= 0 && !string.IsNullOrEmpty(_savedFarmAnchor))
                    {
                        // Đã teleport về anchor → Bắt đầu reverse walk
                        Plugin.Log.LogInfo($"[AutoBoss] Teleport guard: arrived at anchor → ReverseWalkToFarm");
                        Transition(AutoBossState.ReverseWalkToFarm);
                    }
                    else
                    {
                        // Không cần reverse walk → Thẳng RestoreFarmZone
                        Transition(AutoBossState.RestoreFarmZone);
                    }
                    return;
                }
                if (State == AutoBossState.TeleportToBossMap)
                {
                    string tgt = (Config.BossMapNames != null && _currentMapIndex < Config.BossMapNames.Count)
                        ? GetFastTravelMapName(Config.BossMapNames[_currentMapIndex]) : "";
                    bool hasChainOrPortalGuard = (Config.PortalChainMaps != null && Config.PortalChainMaps.Count > 0)
                        || (!string.IsNullOrEmpty(tgt) && tgt != (Config.BossMapNames != null && _currentMapIndex < Config.BossMapNames.Count ? Config.BossMapNames[_currentMapIndex] : ""));
                    if (!string.IsNullOrEmpty(tgt) && GameAPI.IsInMap(tgt))
                    {
                        if (hasChainOrPortalGuard)
                            Transition(AutoBossState.WalkToPortal);
                        else
                            Transition(AutoBossState.ZoneScanLoop);
                    }
                    else
                    {
                        Transition(AutoBossState.ZoneScanLoop);
                    }
                    return;
                }
            }
        }

        switch (State)
        {
            case AutoBossState.Idle:
                break;

            case AutoBossState.SolveCaptcha:
                RunSolveCaptcha();
                break;

            case AutoBossState.FarmTown:
                RunFarmTown();
                break;

            case AutoBossState.TeleportToBossMap:
                RunTeleportToBossMap();
                break;

            case AutoBossState.WalkToPortal:
                RunWalkToPortal();
                break;

            case AutoBossState.ZoneScanLoop:
                RunZoneScanLoop();
                break;

            case AutoBossState.MoveToBoss:
                RunMoveToBoss();
                break;

            case AutoBossState.CombatBoss:
                RunCombatBoss();
                break;

            case AutoBossState.LootDrops:
                RunLootDrops();
                break;

            case AutoBossState.ReturnToFarmMap:
                RunReturnToFarmMap();
                break;

            case AutoBossState.ReverseWalkToFarm:
                RunReverseWalkToFarm();
                break;

            case AutoBossState.RestoreFarmZone:
                RunRestoreFarmZone();
                break;

            case AutoBossState.ResumeFarming:
                RunResumeFarming();
                break;

            case AutoBossState.TeleportHome:
                RunTeleportHome();
                break;

            case AutoBossState.DeadRecovery:
                RunDeadRecovery();
                break;
        }

        // Watchdog PER-ZONE, không phải per-scan.
        // _stateTimer được reset MỖI KHI xác nhận đổi khu thành công (xem RunZoneScanLoop),
        // nên ở ZoneScanLoop watchdog chỉ đo thời gian kẹt TRÊN MỘT KHU, không đo cả vòng quét.
        // Trước đây limit = zones*(cooldown+dwell) nhưng _stateTimer lại cộng dồn cả vòng quét
        // → 10 khu quét thật ~120-140s vượt limit 120s ngay tại Khu 8 dù đang chạy bình thường.
        float watchdogLimit = 60f;
        if (State == AutoBossState.ZoneScanLoop)
        {
            // Ngân sách cho MỘT khu: dwell mob (8s) + cooldown (3s) + biên retry render panel.
            watchdogLimit = Math.Max(45f, ZoneMobDwellSec + ZoneSwitchCooldown + 30f);
        }

        // Watchdog: nếu kẹt quá lâu → ưu tiên quay về saved farm, fallback FarmTown.
        if (_stateTimer > watchdogLimit && State != AutoBossState.Idle && State != AutoBossState.CombatBoss)
        {
            // Không route ReturnToFarmMap/RestoreFarmZone/ReverseWalkToFarm về chính nó (tránh loop) — đã có timeout riêng.
            bool canReturnToFarm = _hasSavedFarmContext
                && State != AutoBossState.ReturnToFarmMap
                && State != AutoBossState.ReverseWalkToFarm
                && State != AutoBossState.RestoreFarmZone
                && State != AutoBossState.TeleportHome;
            AutoBossState fallback = canReturnToFarm ? AutoBossState.ReturnToFarmMap : AutoBossState.FarmTown;
            Plugin.Log.LogWarning($"[AutoBoss] Watchdog: stuck in {State} for {_stateTimer:F0}s (limit={watchdogLimit:F0}) → {fallback}");
            Transition(fallback);
        }
    }

    // ===== Auto-dump helper =====

    private void RunAutoDump(string uiFile, string runtimeFile)
    {
        try
        {
            Plugin.Log.LogInfo($"[AutoDump] Dumping → {uiFile} + {runtimeFile} (frame={Time.frameCount})");
            UiPanelDumper.DumpAll(uiFile);
            UiPanelDumper.DumpRuntimeTypes(runtimeFile);
            Plugin.Log.LogInfo("[AutoDump] Done");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[AutoDump] FAIL: {ex.Message}");
        }
    }

    // ===== State handlers =====

    private void RunFarmTown()
    {
        // Kiểm tra nếu chết → recovery
        if (GameAPI.FindDeathPanel() != null)
        {
            Plugin.Log.LogWarning("[AutoBoss] DeathPanel visible → DeadRecovery");
            Transition(AutoBossState.DeadRecovery);
            return;
        }

        _scanTimer += Time.deltaTime;
        if (_scanTimer >= Config.ScanBossEverySec)
        {
            _scanTimer = 0f;
            SaveCurrentFarmContext();
            // Bosses usually spawn in the BossMap, so we should go there to check
            Plugin.Log.LogInfo("[AutoBoss] Ready to hunt -> Teleporting to Boss Map");
            _currentMapIndex = 0;
            Transition(AutoBossState.TeleportToBossMap);
        }
    }

    // Cờ: lần teleport tiếp theo sẽ thử trực tiếp đến Cung (không qua Planet Plant)
    private bool _tryDirectToCung = false;

    private string GetFastTravelMapName(string targetMap)
    {
        // Ưu tiên FastTravelAnchorMap trong config nếu được cấu hình
        if (!string.IsNullOrEmpty(Config.FastTravelAnchorMap))
            return Config.FastTravelAnchorMap;

        // Hành vi mặc định cũ: Cung → Planet Plant (trừ khi đã bật direct mode)
        if (targetMap.IndexOf("Cung", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (_tryDirectToCung)
                return targetMap; // "Cung"
            return "Planet Plant"; // default anchor
        }
        return targetMap;
    }

    /// <summary>
    /// Trả về map mục tiêu của bước WalkToPortal hiện tại.
    /// Nếu có PortalChainMaps và chưa đi qua hết → trả map trung gian tiếp theo.
    /// Ngược lại → trả BossMapNames[_currentMapIndex] (đích cuối).
    /// </summary>
    private string GetCurrentPortalChainTarget()
    {
        if (Config.PortalChainMaps != null && _portalChainIndex < Config.PortalChainMaps.Count)
            return Config.PortalChainMaps[_portalChainIndex];
        return Config.BossMapNames != null && _currentMapIndex < Config.BossMapNames.Count
            ? Config.BossMapNames[_currentMapIndex]
            : "";
    }

    // === Town/Safe zone detection ===
    // Các map town có NPC thường (Yamcha, Yajirobe...) → KHÔNG scan boss ở đây
    // "mainGame" bị xóa khỏi danh sách - đây là tên scene Unity, không phải town
    // Chỉ dùng tên map thực sự (từ minimap) để detect town
    private static readonly string[] TownMapKeywords = { "ngoai", "quay", "town", "lang", "village" };
    private static bool IsTownMap(string mapName)
    {
        if (string.IsNullOrEmpty(mapName)) return false;
        string lower = mapName.ToLowerInvariant();
        foreach (var kw in TownMapKeywords)
            if (lower.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private void RunTeleportToBossMap()
    {
        if (Config.BossMapNames == null || _currentMapIndex >= Config.BossMapNames.Count)
        {
            _teleportInProgress = false;
            Plugin.Log.LogWarning(_hasSavedFarmContext
                ? "[AutoBoss] No more boss maps -> return to saved farm"
                : "[AutoBoss] No more boss maps -> TeleportHome");
            Transition(_hasSavedFarmContext ? AutoBossState.ReturnToFarmMap : AutoBossState.TeleportHome);
            return;
        }
        string targetMap = Config.BossMapNames[_currentMapIndex];
        string fastTravelMap = GetFastTravelMapName(targetMap);

        // Reset portal chain index mỗi lần bắt đầu teleport đến map mới
        // (chỉ reset khi _stateTimer == 0 tức là vừa Transition vào state này)
        if (_stateTimer < 0.1f)
            _portalChainIndex = 0;

        // === FIX: Smart skip - nếu đang ở giữa portal chain, skip teleport và adjust index ===
        string currentMap = GameAPI.GetCurrentMapFromMiniMap();
        if (string.IsNullOrEmpty(currentMap))
            currentMap = GameAPI.GetCurrentMapName();

        // Kiểm tra xem đang ở bước nào trong chain
        int currentChainStep = -1;
        if (Config.PortalChainMaps != null && !string.IsNullOrEmpty(currentMap))
        {
            for (int i = 0; i < Config.PortalChainMaps.Count; i++)
            {
                if (string.IsNullOrEmpty(Config.PortalChainMaps[i])) continue;
                if (currentMap.IndexOf(Config.PortalChainMaps[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    currentChainStep = i;
                    break;
                }
            }
        }

        // Nếu đang ở giữa chain → skip teleport, adjust index, chuyển thẳng sang WalkToPortal
        if (currentChainStep >= 0)
        {
            _portalChainIndex = currentChainStep + 1; // Bắt đầu từ bước tiếp theo
            _teleportInProgress = false;
            Plugin.Log.LogInfo($"[AutoBoss] Already at chain step {currentChainStep} ('{Config.PortalChainMaps[currentChainStep]}') -> skip to step {_portalChainIndex}, WalkToPortal");

            // Kiểm tra xem đã đến BossMap cuối chưa
            bool alreadyAtBoss = !string.IsNullOrEmpty(targetMap) &&
                currentMap.IndexOf(targetMap, StringComparison.OrdinalIgnoreCase) >= 0;

            if (alreadyAtBoss)
            {
                Plugin.Log.LogInfo($"[AutoBoss] Already at boss map '{targetMap}' -> ZoneScanLoop");
                Transition(AutoBossState.ZoneScanLoop);
            }
            else
            {
                // Đang ở map trung gian, kể cả bước cuối của chain -> tiếp tục đi tới BossMap.
                Plugin.Log.LogInfo($"[AutoBoss] Still on chain map '{currentMap}' -> WalkToPortal toward '{targetMap}'");
                Transition(AutoBossState.WalkToPortal);
            }
            return;
        }

        // Đã ở đúng anchor → bỏ qua menu
        if (GameAPI.IsInMap(fastTravelMap))
        {
            Plugin.Log.LogInfo($"[AutoBoss] Already in anchor '{fastTravelMap}', skipping menu.");
            _teleportInProgress = false;
            bool hasChainOrPortal = (Config.PortalChainMaps != null && Config.PortalChainMaps.Count > 0)
                || fastTravelMap != targetMap;
            if (hasChainOrPortal)
                Transition(AutoBossState.WalkToPortal);
            else
                Transition(AutoBossState.ZoneScanLoop);
            return;
        }

        // === ĐANG TRONG COOLDOWN TELEPORT (vừa click, đang chờ load scene) ===
        if (_teleportInProgress)
        {
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            // 1) Scene đã đổi → teleport thành công (kể cả scene mới không chứa tên map
            //    vì game Unity có thể đặt scene name khác tên map hiển thị)
            if (!string.IsNullOrEmpty(_sceneNameBeforeTeleport) &&
                !string.IsNullOrEmpty(currentScene) &&
                currentScene != _sceneNameBeforeTeleport)
            {
                Plugin.Log.LogInfo($"[AutoBoss] Scene changed '{_sceneNameBeforeTeleport}' -> '{currentScene}'. Teleport OK.");
                _teleportInProgress = false;
                MapTransporter.ResetTeleportSession();
                MapTransporter.CloseTeleportPanel();
                bool hasChainOrPortal2 = (Config.PortalChainMaps != null && Config.PortalChainMaps.Count > 0)
                    || fastTravelMap != targetMap;
                if (hasChainOrPortal2)
                    Transition(AutoBossState.WalkToPortal);
                else
                    Transition(AutoBossState.ZoneScanLoop);
                return;
            }

            // 2) Hết thời gian chờ tối đa → fallback check IsInMap
            if (Time.time >= _teleportCooldownUntil)
            {
                if (GameAPI.IsInMap(fastTravelMap))
                {
                    Plugin.Log.LogInfo($"[AutoBoss] Teleport to '{fastTravelMap}' confirmed by IsInMap.");
                    _teleportInProgress = false;
                    MapTransporter.ResetTeleportSession();
                    MapTransporter.CloseTeleportPanel();
                    bool hasChainOrPortal3 = (Config.PortalChainMaps != null && Config.PortalChainMaps.Count > 0)
                        || fastTravelMap != targetMap;
                    if (hasChainOrPortal3)
                        Transition(AutoBossState.WalkToPortal);
                    else
                        Transition(AutoBossState.ZoneScanLoop);
                    return;
                }

                // Đã hết cooldown mà chưa vào map → timeout, thử lại
                Plugin.Log.LogWarning($"[AutoBoss] Teleport timeout. Scene='{currentScene}'. Retrying...");
                _teleportInProgress = false;
                MapTransporter.ResetTeleportSession();
                _stateTimer = 0f;
                return;
            }

            // Còn trong cooldown → KHÔNG làm gì cả
            // (Không bấm O, không gọi menu - game đang load)
            return;
        }

        // === CHƯA TELEPORT: chuẩn bị và click ===
        // Ghi lại scene hiện tại để detect scene change sau click
        _sceneNameBeforeTeleport = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        // Nhấn P (hợp thể) trước khi mở menu teleport (nếu AutoFusion bật)
        if (Config.AutoFusion)
            AutoPickupLite.TapFusionKey();

        if (MapTransporter.OpenMenuAndTeleport(fastTravelMap))
        {
            _teleportInProgress = true;
            _teleportTargetMap = fastTravelMap;
            _teleportCooldownUntil = Time.time + 8f; // đợi tối đa 8s cho scene load
            _teleportClickedAt = Time.time;
            Plugin.Log.LogInfo($"[AutoBoss] Teleport to '{fastTravelMap}' clicked from scene '{_sceneNameBeforeTeleport}'. Waiting up to 8s...");
        }
        else if (_stateTimer > Config.TeleportTimeoutSec)
        {
            Plugin.Log.LogWarning($"[AutoBoss] Teleport to '{fastTravelMap}' failed -> trying next");
            MapTransporter.ResetTeleportSession();
            _teleportInProgress = false;
            _currentMapIndex++;
            _stateTimer = 0f;
        }
    }

    private void RunWalkToPortal()
    {
        if (_stateTimer > Config.TeleportTimeoutSec)
        {
            Plugin.Log.LogWarning(_hasSavedFarmContext
                ? "[AutoBoss] WalkToPortal timeout -> return to saved farm"
                : "[AutoBoss] WalkToPortal timeout -> TeleportHome");
            _portalMoveIssued = false;
            _cachedGatewayX = _cachedGatewayY = 0f;
            Transition(_hasSavedFarmContext ? AutoBossState.ReturnToFarmMap : AutoBossState.TeleportHome);
            return;
        }

        // === PORTAL CHAIN: xác định map đích của bước hiện tại ===
        // Nếu có PortalChainMaps → đi từng bước: Chain[0] → Chain[1] → ... → BossMap
        // Nếu không (Vegiita cũ) → đích thẳng là BossMap
        string targetMap = GetCurrentPortalChainTarget();
        string finalBossMap = (Config.BossMapNames != null && _currentMapIndex < Config.BossMapNames.Count)
            ? Config.BossMapNames[_currentMapIndex] : "";

        // Đã vào map đích của bước hiện tại?
        bool inTargetMap = GameAPI.IsInMap(targetMap);
        string minimapName = GameAPI.GetCurrentMapFromMiniMap();
        if (!inTargetMap && !string.IsNullOrEmpty(minimapName) &&
            minimapName.IndexOf(targetMap, StringComparison.OrdinalIgnoreCase) >= 0)
            inTargetMap = true;

        if (inTargetMap)
        {
            _portalMoveIssued = false;
            _cachedGatewayX = _cachedGatewayY = 0f;
            _portalStallCount = 0;
            _portalNudgePhase = 0;
            _lastPortalSignature = "";

            // === FIX: Kiểm tra xem có đang stuck loop tại cùng 1 map không ===
            // Nếu targetMap == map hiện tại VÀ không phải boss map cuối → có thể bị loop
            bool isPotentialLoop = false;
            if (Config.PortalChainMaps != null && _portalChainIndex < Config.PortalChainMaps.Count)
            {
                string chainMapAtCurrentIndex = Config.PortalChainMaps[_portalChainIndex];
                if (!string.IsNullOrEmpty(chainMapAtCurrentIndex) &&
                    targetMap == chainMapAtCurrentIndex &&
                    minimapName.IndexOf(chainMapAtCurrentIndex, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Đang ở map X, target cũng là X → skip sang bước tiếp theo
                    isPotentialLoop = true;
                    Plugin.Log.LogWarning($"[AutoBoss] Potential loop detected: already at '{targetMap}' (chain step {_portalChainIndex}) -> force skip to next step");
                }
            }

            bool isLastStep = Config.PortalChainMaps == null
                || _portalChainIndex >= Config.PortalChainMaps.Count - 1;

            if (isPotentialLoop || !isLastStep)
            {
                // Còn bước tiếp theo trong chain → tăng index, reset timer, tiếp tục WalkToPortal
                _portalChainIndex++;
                string nextTarget = GetCurrentPortalChainTarget();

                // Double-check: nếu nextTarget cũng là map hiện tại → skip luôn
                if (!string.IsNullOrEmpty(nextTarget) &&
                    minimapName.IndexOf(nextTarget, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Plugin.Log.LogWarning($"[AutoBoss] Next target '{nextTarget}' is also current map -> skip to ZoneScanLoop");
                    _zoneAttempts = 0;
                    Transition(AutoBossState.ZoneScanLoop);
                    return;
                }

                Plugin.Log.LogInfo($"[AutoBoss] Portal chain step {_portalChainIndex}: entered '{targetMap}' -> walking to '{nextTarget}'");
                _stateTimer = 0f;
                // Không Transition (vẫn WalkToPortal), chỉ reset state để tìm gateway mới
                return;
            }

            // Bước cuối cùng → đã đến map cuối (BossMap hoặc đích cuối chain)
            // Kiểm tra thêm: đích cuối chain có phải chính xác là finalBossMap không?
            bool arrivedAtBoss = string.IsNullOrEmpty(finalBossMap)
                || GameAPI.IsInMap(finalBossMap)
                || (!string.IsNullOrEmpty(minimapName) && minimapName.IndexOf(finalBossMap, StringComparison.OrdinalIgnoreCase) >= 0);

            if (arrivedAtBoss || targetMap == finalBossMap)
            {
                Plugin.Log.LogInfo($"[AutoBoss] Portal chain complete -> Entered '{targetMap}' (final boss map '{finalBossMap}')");
                _zoneAttempts = 0;
                Transition(AutoBossState.ZoneScanLoop);
                return;
            }

            // targetMap là bước cuối chain nhưng chưa phải finalBossMap → cần thêm 1 bước portal nữa
            Plugin.Log.LogInfo($"[AutoBoss] Arrived at last chain step '{targetMap}', still need to reach boss map '{finalBossMap}'");
            _portalChainIndex++;
            _stateTimer = 0f;
            return;
        }

        // Log mỗi 3s để debug
        if (Time.time % 3f < 0.2f)
        {
            Plugin.Log.LogInfo($"[AutoBoss] WalkToPortal: target='{targetMap}', minimap='{minimapName}', stateTimer={_stateTimer:F1}");
        }

        // === SAFETY: Nếu stuck > 8s ở Planet Plant mà vẫn chưa vào Cung (chỉ áp dụng Vegiita, không có chain) ===
        bool isVegiitaMode = (Config.PortalChainMaps == null || Config.PortalChainMaps.Count == 0)
            && targetMap.IndexOf("Cung", StringComparison.OrdinalIgnoreCase) >= 0;
        if (isVegiitaMode && _stateTimer > 8f && !string.IsNullOrEmpty(minimapName) &&
            minimapName.IndexOf("Planet Plant", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Plugin.Log.LogWarning($"[AutoBoss] Stuck in Planet Plant for {_stateTimer:F1}s. Switching to direct-Cung teleport mode.");
            _tryDirectToCung = true; // lần sau sẽ gọi menu trực tiếp đến "Cung"
            _portalMoveIssued = false;
            _cachedGatewayX = _cachedGatewayY = 0f;
            Transition(AutoBossState.TeleportToBossMap);
            return;
        }

        Vector2 myPos = GameAPI.GetPlayerPosition();

        // === Tìm gateway theo pattern Tool_Om_Boss ===
        // Detect scene change: gateway signature thay đổi → đã vào room mới
        string currentSig = GetGatewaySignature();
        if (!string.IsNullOrEmpty(_lastPortalSignature) && !string.IsNullOrEmpty(currentSig)
            && currentSig != _lastPortalSignature)
        {
            Plugin.Log.LogInfo($"[AutoBoss] Gateway signature changed -> rescan");
            _lastPortalSignature = currentSig;
            _cachedGatewayX = _cachedGatewayY = 0f;
        }

        // Tìm gateway xa nhất bên phải (map đi theo trục X)
        if (_cachedGatewayX == 0f && _cachedGatewayY == 0f)
        {
            float bestX = float.MinValue;
            float bestY = 0f;
            bool found = false;

            // Ưu tiên 1: MapGateway MonoBehaviour
            try
            {
                var gateways = UnityEngine.Object.FindObjectsOfType<MapGateway>();
                if (gateways != null)
                {
                    foreach (var gw in gateways)
                    {
                        if (gw == null) continue;
                        var p = gw.transform.position;
                        if (p.x > bestX) { bestX = p.x; bestY = p.y; found = true; }
                    }
                    if (found)
                        Plugin.Log.LogInfo($"[AutoBoss] Found MapGateway @ ({bestX:F0},{bestY:F0})");
                }
            }
            catch { }

            // Ưu tiên 2: ChangeMap class
            if (!found)
            {
                try
                {
                    var changeMaps = GameAPI.FindChangeMaps();
                    if (changeMaps != null)
                    {
                        foreach (var cm in changeMaps)
                        {
                            if (cm == null) continue;
                            var go = cm as UnityEngine.Component;
                            if (go == null) continue;
                            var p = go.transform.position;
                            if (p.x > bestX) { bestX = p.x; bestY = p.y; found = true; }
                        }
                        if (found)
                            Plugin.Log.LogInfo($"[AutoBoss] Found ChangeMap @ ({bestX:F0},{bestY:F0})");
                    }
                }
                catch { }
            }

            // Ưu tiên 3: GameObject tên chứa Portal/Gateway
            if (!found)
            {
                try
                {
                    var allGos = UnityEngine.Object.FindObjectsOfType<GameObject>();
                    foreach (var go in allGos)
                    {
                        if (go == null || !go.activeInHierarchy) continue;
                        string n = go.name ?? "";
                        if (n.IndexOf("Portal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Gateway", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("ChangeMap", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var p = go.transform.position;
                            if (p.x > bestX) { bestX = p.x; bestY = p.y; found = true; }
                        }
                    }
                    if (found)
                        Plugin.Log.LogInfo($"[AutoBoss] Found Portal GameObject @ ({bestX:F0},{bestY:F0})");
                }
                catch { }
            }

            if (!found)
            {
                if (_stateTimer > 5f)
                    Plugin.Log.LogWarning($"[AutoBoss] No portal found after {_stateTimer:F1}s in scene");
                return;
            }

            _cachedGatewayX = bestX;
            _cachedGatewayY = bestY;
            _lastPortalSignature = currentSig;
        }

        // === Di chuyển tới gateway (pattern Tool_Om_Boss TickMoveToNextGate) ===
        float dist = Mathf.Abs(myPos.x - _cachedGatewayX) + Mathf.Abs(myPos.y - _cachedGatewayY);

        // Portal progress tracking
        bool stalled = UpdatePortalProgress(myPos.x, myPos.y, _cachedGatewayX, _cachedGatewayY);

        // === MULTI-STEP PATH (pattern từ Tool_Om_Boss TryIssueMoveToPathOnly): path-only move ===
        // Tool_Om_Boss dùng stepSize=0.8 vì game engine xử lý collision theo grid nhỏ.
        // OPTIMIZED: tăng từ 0.55f lên 1.2f để di chuyển nhanh hơn, giảm từ 1.25f xuống 0.9f để reissue nhanh hơn
        if (dist > 1.2f)
        {
            if (!_portalMoveIssued || (stalled && Time.time - _lastPortalMoveIssuedAt >= 0.9f))
            {
                GameAPI.MoveToPathOnly(_cachedGatewayX, _cachedGatewayY);
                _portalMoveIssued = true;
                _lastPortalMoveIssuedAt = Time.time;
                if (Time.time % 2f < 0.1f)
                    Plugin.Log.LogInfo($"[AutoBoss] portal-walk target=({_cachedGatewayX:F0},{_cachedGatewayY:F0}) dist={dist:F1} stalled={stalled}");
            }
        }
        else if (stalled && dist > 0.15f && Time.time - _lastPortalMoveIssuedAt >= 0.9f)
        {
            // Đến gần nhưng vẫn chưa trigger portal → nudge
            GameAPI.MoveToPathOnly(_cachedGatewayX, _cachedGatewayY);
            _portalMoveIssued = true;
            _lastPortalMoveIssuedAt = Time.time;
            Plugin.Log.LogInfo($"[AutoBoss] portal-walk-reissue dist={dist:F1} stalled={stalled}");
        }

        // === Side-nudge (Tool_Om_Boss pattern): stalled ≥ 2 lần → lệch trái 1.35f ===
        // CHỈ nudge khi dist < 10 (gateway gần) - nếu xa quá thì nudge vô ích
        if (stalled && _portalStallCount >= 2 && Time.time - _lastPortalNudgeAt >= 1.0f && dist < 10f)
        {
            float nudgeX;
            string dir;
            if (_portalNudgePhase <= 0)
            {
                nudgeX = _cachedGatewayX - 1.35f;
                dir = "left";
                _portalNudgePhase = 1;
            }
            else
            {
                nudgeX = _cachedGatewayX + 1.35f;
                dir = "right";
                _portalNudgePhase = 2;
            }
            GameAPI.MoveTo(nudgeX, _cachedGatewayY);
            _lastPortalNudgeAt = Time.time;
            _portalMoveIssued = true;
            _lastPortalMoveIssuedAt = Time.time;
            Plugin.Log.LogInfo($"[AutoBoss] portal-nudge-{dir} target=({nudgeX:F0},{_cachedGatewayY:F0}) player=({myPos.x:F0},{myPos.y:F0})");
        }

        // === Break-step (Tool_Om_Boss pattern): stalled ≥ 4 lần → demo player lệch theo hướng gateway ===
        // CHỈ break-step khi dist < 8 (rất gần) - dùng để trigger portal
        if (_portalStallCount >= 4 && Time.time - _lastPortalMoveIssuedAt >= 1.5f && dist < 8f)
        {
            float dx = _cachedGatewayX - myPos.x;
            float dy = _cachedGatewayY - myPos.y;
            float stepX = myPos.x, stepY = myPos.y;
            if (Mathf.Abs(dx) >= 0.25f)
                stepX = myPos.x + Mathf.Sign(dx) * 0.8f;
            else
                stepY = myPos.y + Mathf.Sign(dy) * 0.8f;
            GameAPI.MoveTo(stepX, stepY);
            _portalMoveIssued = true;
            _lastPortalMoveIssuedAt = Time.time;
            Plugin.Log.LogInfo($"[AutoBoss] portal-break-step from=({myPos.x:F0},{myPos.y:F0}) break=({stepX:F0},{stepY:F0}) stall={_portalStallCount}");
        }

        // Sau 12s nếu vẫn chưa vào → reset và rescan
        if (_stateTimer > 12.0f && dist > 2.0f)
        {
            Plugin.Log.LogWarning($"[AutoBoss] Portal stuck for {_stateTimer:F1}s, rescan");
            _cachedGatewayX = _cachedGatewayY = 0f;
            _portalNudgePhase = 0;
            _portalStallCount = 0;
        }

        // === SAFETY NET: Nếu gateway quá xa (>60) → không thể tới bằng cách đi bộ, có thể ở map khác ===
        // Pattern từ Tool_Om_Boss: timeout 15s vẫn còn ở anchor → có thể game dùng menu teleport, không phải đi bộ
        if (dist > 60f && _stateTimer > 8f)
        {
            Plugin.Log.LogWarning($"[AutoBoss] Gateway too far ({dist:F0}) - might be wrong map. Check MiniMap='{GameAPI.GetCurrentMapFromMiniMap()}'");
            // Không teleport, để timeout xử lý
        }
    }

    private bool UpdatePortalProgress(float px, float py, float gx, float gy)
    {
        // OPTIMIZED: giảm interval từ 0.45f xuống 0.35f để detect stall nhanh hơn
        if (Time.time - _lastPortalProgressCheckAt < 0.35f)
            return _portalStallCount > 2;

        _lastPortalProgressCheckAt = Time.time;
        float newDist = Mathf.Abs(px - gx) + Mathf.Abs(py - gy);

        if (newDist + 0.12f < _lastPortalProgressDist)
        {
            // Đang tiến lại gần → reset stall
            _portalStallCount = 0;
            _lastPortalProgressX = px;
            _lastPortalProgressY = py;
            _lastPortalProgressDist = newDist;
            return false;
        }
        else
        {
            _portalStallCount++;
            if (_portalStallCount > 2)
            {
                Plugin.Log.LogInfo($"[AutoBoss] Portal stalled: dist={newDist:F1}, lastProgress=({_lastPortalProgressX:F0},{_lastPortalProgressY:F0})");
            }
            return true;
        }
    }

    private void ResetBossApproachState()
    {
        _cachedBossX = 0f;
        _cachedBossY = 0f;
        _lastBossMoveIssuedAt = -999f;
        _bossMoveIssued = false;
        _lastBossProgressX = 0f;
        _lastBossProgressY = 0f;
        _lastBossProgressDist = float.MaxValue;
        _lastBossProgressCheckAt = -999f;
        _bossStallCount = 0;
        _bossNudgePhase = 0;
        _lastBossNudgeAt = -999f;
    }

    private bool UpdateBossProgress(float px, float py, float bx, float by)
    {
        // OPTIMIZED: giảm interval từ 0.45f xuống 0.35f để detect stall nhanh hơn
        if (Time.time - _lastBossProgressCheckAt < 0.35f)
            return _bossStallCount > 2;

        _lastBossProgressCheckAt = Time.time;
        float newDist = Mathf.Abs(px - bx) + Mathf.Abs(py - by);

        if (newDist + 0.12f < _lastBossProgressDist)
        {
            _bossStallCount = 0;
            _lastBossProgressX = px;
            _lastBossProgressY = py;
            _lastBossProgressDist = newDist;
            return false;
        }

        _bossStallCount++;
        if (_bossStallCount > 2)
        {
            Plugin.Log.LogInfo($"[AutoBoss] Boss stalled: dist={newDist:F1}, lastProgress=({_lastBossProgressX:F0},{_lastBossProgressY:F0})");
        }
        return true;
    }

    private string GetGatewaySignature()
    {
        try
        {
            var gateways = UnityEngine.Object.FindObjectsOfType<MapGateway>();
            if (gateways == null || gateways.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var gw in gateways)
            {
                if (gw == null) continue;
                sb.Append($"{gw.transform.position.x:F0},{gw.transform.position.y:F0}|");
            }
            return sb.ToString();
        }
        catch { return ""; }
    }

    private void RunZoneScanLoop()
    {
        // === TOWN DETECTION: ưu tiên dùng minimap name (tên map thực) để detect town ===
        // KHÔNG dùng scene name (GetCurrentMapName) vì Unity scene thường tên "MainGameScene"
        // sẽ match sai với TownMapKeywords cũ chứa "mainGame".
        string minimapForTownCheck = GameAPI.GetCurrentMapFromMiniMap();
        string myMap = !string.IsNullOrEmpty(minimapForTownCheck)
            ? minimapForTownCheck
            : GameAPI.GetCurrentMapName();

        if (IsTownMap(myMap))
        {
            Plugin.Log.LogWarning(_hasSavedFarmContext
                ? $"[AutoBoss] ZoneScan but in TOWN '{myMap}' -> return to saved farm"
                : $"[AutoBoss] ZoneScan but in TOWN '{myMap}' -> TeleportHome");
            Transition(_hasSavedFarmContext ? AutoBossState.ReturnToFarmMap : AutoBossState.TeleportHome);
            return;
        }

        // === Quét boss (3 lớp: mobType, BossFlag, name) - SỬ DỤNG CACHE tối ưu ===
        var boss = BossDetector.FindBoss(Config.BossNames);
        if (boss != null)
        {
            if (!_captchaTriggered && _stateTimer > 0.5f)
            {
                _captchaTriggered = true;
                CaptchaManager.TriggerScan();
            }
            _currentBoss = boss;
            _bossMissingTimer = 0f;
            _zoneAttempts = 0;
            _zoneSwitchTimer = 0f;
            Transition(AutoBossState.SolveCaptcha);
            return;
        }

        int zoneLimit = ZoneSwitcher.HasKnownTotalZones ? ZoneSwitcher.TotalZones : Config.MaxZoneAttempts;
        float zoneAge = Math.Max(0f, Time.time - _enteredZoneAt);

        // === ADAPTIVE DWELL: sử dụng cached mob count từ ZoneSwitcher thay vì scan lại ===
        int aliveMobs = ZoneSwitcher.GetCachedMobCount();

        // Smart dwell: nếu không có mob nào → skip nhanh, nếu có mob → đợi boss spawn
        // Bonus: nếu có mob tên gợi ý boss (elite, leader) → dwell ngắn hơn
        float dwellSec = ZoneEmptyDwellSec;
        if (aliveMobs > 0)
        {
            // Check xem có mob đặc biệt không (tên chứa hint boss)
            bool hasEliteMob = false;
            try
            {
                var mobs = GameAPI.FindAllMobs();
                if (mobs != null && mobs.Count > 0 && mobs.Count <= 3)
                {
                    foreach (var m in mobs)
                    {
                        if (!GameAPI.IsMobAlive(m)) continue;
                        string name = GameAPI.GetMobName(m);
                        if (string.IsNullOrEmpty(name)) continue;
                        string lower = name.ToLowerInvariant();
                        if (lower.Contains("elite") || lower.Contains("leader") ||
                            lower.Contains("boss") || lower.Contains("truong"))
                        {
                            hasEliteMob = true;
                            break;
                        }
                    }
                }
            }
            catch { }

            dwellSec = hasEliteMob ? ZoneBossHintDwellSec : ZoneMobDwellSec;
        }

        // === LOG DETAIL mỗi 2s để debug zone switch ===
        if (Time.time - _lastZoneScanLogAt >= 2f)
        {
            _lastZoneScanLogAt = Time.time;
            int remaining = Math.Max(0, zoneLimit - _zoneAttempts);
            Plugin.Log.LogInfo($"[AutoBoss] ZoneScan map='{myMap}' alive={aliveMobs} zoneAge={zoneAge:F1}/{dwellSec:F1}s zoneAttempts={_zoneAttempts}/{zoneLimit} ({remaining} left) zoneSwitchTimer={_zoneSwitchTimer:F1}");
        }

        // Đang chờ panel render hoặc HUD xác nhận → poll ngay, KHÔNG đợi dwell/cooldown.
        bool pendingAny = ZoneSwitcher.IsWaitingForPanelRender || ZoneSwitcher.IsWaitingForZoneChange;
        if (pendingAny)
        {
            bool switched2 = ZoneSwitcher.NextZone();
            if (switched2 && ZoneSwitcher.LastActionClickedZone)
            {
                // Xác nhận thành công → commit
                _stateTimer = 0f;
                _enteredZoneAt = Time.time;
                _zoneSwitchTimer = 0f;
                _zoneAttempts++;
                _captchaTriggered = false;
                if (_zoneAttempts % 5 == 0 || _zoneAttempts == 1)
                    Plugin.Log.LogInfo($"[AutoBoss] Khu {ZoneSwitcher.LastClickedZoneIndex} confirmed (#{_zoneAttempts})");

                BossDetector.InvalidateCache();
                return;
            }
            else if (Time.time - _lastPendingPollLogAt >= 1.5f)
            {
                _lastPendingPollLogAt = Time.time;
                if (ZoneSwitcher.IsWaitingForZoneChange)
                    Plugin.Log.LogInfo($"[AutoBoss] Zone switch pending → polling HUD for Khu {ZoneSwitcher.PendingZoneIndex}");
                else
                    Plugin.Log.LogInfo("[AutoBoss] Zone panel opened → waiting button render");
            }
            return;
        }

        // === Gate: chỉ call NextZone khi vượt cả cooldown VÀ dwell ===
        _zoneSwitchTimer += Time.deltaTime;
        if (_zoneSwitchTimer < ZoneSwitchCooldown) return;
        if (zoneAge < dwellSec) return;
        _zoneSwitchTimer = 0f;

        bool switched = ZoneSwitcher.NextZone();

        if (switched)
        {
            if (!ZoneSwitcher.LastActionClickedZone)
            {
                // Panel vừa mở hoặc pending confirm → re-arm timer để poll ngay lần sau
                _zoneSwitchTimer = ZoneSwitchCooldown;
                return;
            }

            // Xác nhận thành công → commit
            _stateTimer = 0f;
            _enteredZoneAt = Time.time;
            _zoneAttempts++;
            _captchaTriggered = false;

            if (_zoneAttempts % 5 == 0 || _zoneAttempts == 1)
                Plugin.Log.LogInfo($"[AutoBoss] Khu {ZoneSwitcher.LastClickedZoneIndex} confirmed (#{_zoneAttempts})");

            BossDetector.InvalidateCache();
            return;
        }

        // === NextZone fail hoặc exhausted ===
        if (ZoneSwitcher.IsExhausted)
        {
            Plugin.Log.LogWarning($"[AutoBoss] Boss not found after all {ZoneSwitcher.TotalZones} zones");
            _currentMapIndex++;
            Transition(_currentMapIndex < (Config.BossMapNames?.Count ?? 0)
                ? AutoBossState.TeleportToBossMap
                : AutoBossState.ReturnToFarmMap);
            return;
        }

        Plugin.Log.LogWarning($"[AutoBoss] NextZone() returned false at attempt {_zoneAttempts}");
        int retryLimit = ZoneSwitcher.HasKnownTotalZones ? ZoneSwitcher.TotalZones + 2 : Config.MaxZoneAttempts;
        if (_zoneAttempts >= retryLimit)
        {
            Plugin.Log.LogWarning(ZoneSwitcher.HasKnownTotalZones
                ? $"[AutoBoss] Zone switch failed after {_zoneAttempts} tries (limit={retryLimit})"
                : $"[AutoBoss] Zone count unknown, giving up after {_zoneAttempts} attempts");
            _currentMapIndex++;
            Transition(_currentMapIndex < (Config.BossMapNames?.Count ?? 0)
                ? AutoBossState.TeleportToBossMap
                : AutoBossState.ReturnToFarmMap);
        }
    }

    private void RunSolveCaptcha()
    {
        // Kiểm tra xem khu vực mới vào có boss không
        var boss = BossDetector.FindBoss(Config.BossNames);
        if (boss != null)
        {
            if (!_captchaTriggered && _stateTimer > 0.1f)
            {
                _captchaTriggered = true;
                CaptchaManager.TriggerScan();
            }
            // Khu này CÓ BOSS -> Theo logic của user: "captcha sẽ hiện khi nhân vật chuyển vào khu boss"
            // Chờ Python bot quét và giải captcha nếu có (ít nhất 1.0s để bot nhận diện)
            if (_stateTimer < 1.0f || CaptchaManager.IsSolving)
            {
                if (_stateTimer > 15.0f)
                {
                    Plugin.Log.LogWarning("[AutoBoss] Bỏ qua chờ captcha vì timeout 15s.");
                }
                else
                {
                    if (Time.time % 1f < 0.1f)
                        Plugin.Log.LogInfo($"[AutoBoss] Khu vực có boss! Đang chờ captcha: isSolving={CaptchaManager.IsSolving} ({_stateTimer:F1}s)...");
                    return;
                }
            }
            
            // Đã hết 5 giây (captcha đã được giải xong nếu có), tiến hành vào combat
            Plugin.Log.LogInfo("[AutoBoss] Đã qua thời gian chờ captcha, bắt đầu di chuyển tới Boss!");
            _currentBoss = boss;
            _bossMissingTimer = 0f;
            _zoneAttempts = 0;
            _zoneSwitchTimer = 0f;
            Transition(AutoBossState.MoveToBoss);
        }
        else
        {
            // Khu này KHÔNG CÓ BOSS -> Không có captcha, quay lại quét khu tiếp theo ngay lập tức
            Transition(AutoBossState.ZoneScanLoop);
            // Đặt lại thời gian vào khu để ZoneScanLoop có thể đợi dwellSec trước khi chuyển khu tiếp
            _enteredZoneAt = Time.time;
        }
    }

    /// <summary>
    /// Xử lý khi object boss hiện tại báo "chết/mất".
    /// Server có thể phá hủy object cũ và cấp object MỚI (boss dịch chuyển/đổi
    /// instance) trong khi boss CHƯA chết thật — bản cũ bỏ cuộc ngay tại đây
    /// (khai "defeated" khi HP còn 33 triệu) nên skill không kịp kích hoạt.
    /// Chờ grace 1.5s rồi quét lại theo tên; chỉ trả true khi boss thật sự biến mất.
    /// </summary>
    private bool ConfirmBossDeadOrMissing()
    {
        _bossMissingTimer += Time.deltaTime;
        if (_bossMissingTimer < 1.5f) return false; // chờ xem object có sống lại không

        var reacquired = BossDetector.FindBoss(Config.BossNames);
        if (reacquired != null && !BossDetector.IsDeadOrMissing(reacquired))
        {
            string name = BossDetector.GetMobNameSafe(reacquired);
            Plugin.Log.LogInfo($"[AutoBoss] Boss object stale -> reacquired '{name}'");
            _currentBoss = reacquired;
            _bossMissingTimer = 0f;
            return false;
        }
        return true;
    }

    private void RunMoveToBoss()
    {
        // Tạm dừng nếu Python bot đang giải captcha
        bool shouldPause = CaptchaManager.IsSolving ? (Time.realtimeSinceStartup - CaptchaManager.LastSolvingActivity < 10f) : (Time.realtimeSinceStartup - CaptchaManager.LastSolvingActivity < 3f);
        if (shouldPause)
        {
            if (Time.time % 1f < 0.1f)
                Plugin.Log.LogInfo("[AutoBoss] Tạm dừng MoveToBoss vì Python bot đang giải Captcha...");
            return;
        }

        if (BossDetector.IsDeadOrMissing(_currentBoss))
        {
            if (!ConfirmBossDeadOrMissing()) return; // grace hoặc vừa thay object mới
            Transition(AutoBossState.LootDrops);
            return;
        }
        _bossMissingTimer = 0f;

        // Click thẳng vào boss (nếu đang trong màn hình) để game target boss sớm,
        // thay vì chờ auto Q chọn đại quái gần nhất.
        BossClicker.ClickBoss(_currentBoss);

        var pos = GameAPI.GetMobPosition(_currentBoss);
        var myPos = GameAPI.GetPlayerPosition();
        float targetShift = Mathf.Abs(pos.x - _cachedBossX) + Mathf.Abs(pos.y - _cachedBossY);
        if ((_cachedBossX == 0f && _cachedBossY == 0f) || targetShift > 1.5f)
        {
            _cachedBossX = pos.x;
            _cachedBossY = pos.y;
            _bossMoveIssued = false;
            _bossStallCount = 0;
            _bossNudgePhase = 0;
            _lastBossProgressDist = float.MaxValue;
        }

        float d = Vector2.Distance(myPos, pos);
        float pathDist = Mathf.Abs(myPos.x - _cachedBossX) + Mathf.Abs(myPos.y - _cachedBossY);
        bool stalled = UpdateBossProgress(myPos.x, myPos.y, _cachedBossX, _cachedBossY);

        if (Time.time - _lastMoveToBossLogAt >= 1.5f)
        {
            _lastMoveToBossLogAt = Time.time;
            string name = BossDetector.GetMobNameSafe(_currentBoss);
            Plugin.Log.LogInfo($"[AutoBoss] MoveToBoss '{name}' dist={d:F1} pathDist={pathDist:F1} stalled={stalled} attackRange={Config.AttackRange:F1} myPos={myPos} bossPos={pos}");
        }

        if (d > Config.AttackRange)
        {
            // OPTIMIZED: tăng threshold từ 0.55f lên 1.2f, giảm reissue interval từ 1.25f xuống 0.9f
            if (pathDist > 1.2f)
            {
                if (!_bossMoveIssued || (stalled && Time.time - _lastBossMoveIssuedAt >= 0.9f))
                {
                    if (!GameAPI.MoveToPathOnly(_cachedBossX, _cachedBossY))
                        GameAPI.MoveTo(_cachedBossX, _cachedBossY);
                    _bossMoveIssued = true;
                    _lastBossMoveIssuedAt = Time.time;
                    if (Time.time % 2f < 0.1f)
                        Plugin.Log.LogInfo($"[AutoBoss] boss-walk target=({_cachedBossX:F0},{_cachedBossY:F0}) pathDist={pathDist:F1} stalled={stalled}");
                }
            }
            else if (stalled && pathDist > 0.15f && Time.time - _lastBossMoveIssuedAt >= 0.9f)
            {
                if (!GameAPI.MoveToPathOnly(_cachedBossX, _cachedBossY))
                    GameAPI.MoveTo(_cachedBossX, _cachedBossY);
                _bossMoveIssued = true;
                _lastBossMoveIssuedAt = Time.time;
                Plugin.Log.LogInfo($"[AutoBoss] boss-walk-reissue pathDist={pathDist:F1} stalled={stalled}");
            }

            if (stalled && _bossStallCount >= 2 && Time.time - _lastBossNudgeAt >= 1.0f && pathDist < 10f)
            {
                float nudgeX;
                string dir;
                if (_bossNudgePhase <= 0)
                {
                    nudgeX = _cachedBossX - 1.2f;
                    dir = "left";
                    _bossNudgePhase = 1;
                }
                else
                {
                    nudgeX = _cachedBossX + 1.2f;
                    dir = "right";
                    _bossNudgePhase = 2;
                }
                GameAPI.MoveTo(nudgeX, _cachedBossY);
                _bossMoveIssued = true;
                _lastBossMoveIssuedAt = Time.time;
                _lastBossNudgeAt = Time.time;
                Plugin.Log.LogInfo($"[AutoBoss] boss-nudge-{dir} target=({nudgeX:F0},{_cachedBossY:F0}) player=({myPos.x:F0},{myPos.y:F0})");
            }

            if (_bossStallCount >= 4 && Time.time - _lastBossMoveIssuedAt >= 1.5f && pathDist < 8f)
            {
                float dx = _cachedBossX - myPos.x;
                float dy = _cachedBossY - myPos.y;
                float stepX = myPos.x, stepY = myPos.y;
                if (Mathf.Abs(dx) >= 0.25f)
                    stepX = myPos.x + Mathf.Sign(dx) * 0.8f;
                else
                    stepY = myPos.y + Mathf.Sign(dy) * 0.8f;
                GameAPI.MoveTo(stepX, stepY);
                _bossMoveIssued = true;
                _lastBossMoveIssuedAt = Time.time;
                Plugin.Log.LogInfo($"[AutoBoss] boss-break-step from=({myPos.x:F0},{myPos.y:F0}) break=({stepX:F0},{stepY:F0}) stall={_bossStallCount}");
            }

        }
        else
        {
            ResetBossApproachState();
            Transition(AutoBossState.CombatBoss);
        }
    }

    private void RunCombatBoss()
    {
        // Tạm dừng nếu Python bot đang giải captcha
        bool shouldPause = CaptchaManager.IsSolving ? (Time.realtimeSinceStartup - CaptchaManager.LastSolvingActivity < 10f) : (Time.realtimeSinceStartup - CaptchaManager.LastSolvingActivity < 3f);
        if (shouldPause)
        {
            if (Time.time % 1f < 0.1f)
                Plugin.Log.LogInfo("[AutoBoss] Tạm dừng CombatBoss vì Python bot đang giải Captcha...");
            
            // Tạm tắt auto attack Q nếu đang bật để tránh làm hỏng captcha
            if (_bossAttackEngaged)
            {
                AutoPickupLite.SetAutoAttack(false);
                _bossAttackEngaged = false;
            }
            return;
        }

        if (BossDetector.IsDeadOrMissing(_currentBoss))
        {
            if (!ConfirmBossDeadOrMissing()) return; // grace hoặc vừa thay object mới
            string name = BossDetector.GetMobNameSafe(_currentBoss);
            Plugin.Log.LogInfo($"[AutoBoss] Boss '{name}' defeated → Loot");
            _currentBoss = null;
            // KHÔNG tắt _bossAttackEngaged ở đây - để LootDrops tắt
            Transition(AutoBossState.LootDrops);
            return;
        }
        _bossMissingTimer = 0f;

        // Click thẳng vào boss để game giữ target boss: auto Q đánh đúng boss
        // và thanh HP mục tiêu hiện HP boss. Throttle sẵn trong BossClicker.
        BossClicker.ClickBoss(_currentBoss);

        if (GameAPI.GetPlayerHpPct() < Config.RetreatHpPct)
        {
            float currentHp = GameAPI.GetPlayerHpPct();
            Plugin.Log.LogWarning(_hasSavedFarmContext
                ? $"[AutoBoss] HP low ({currentHp:F1}% < {Config.RetreatHpPct:F0}%) → return to saved farm"
                : $"[AutoBoss] HP low ({currentHp:F1}% < {Config.RetreatHpPct:F0}%) → Retreat home");
            // HP thấp → tắt auto Q ngay
            if (_bossAttackEngaged)
            {
                Plugin.Log.LogInfo("[AutoBoss] HP low -> stop auto attack Q");
                AutoPickupLite.SetAutoAttack(false);
                _bossAttackEngaged = false;
            }
            Transition(_hasSavedFarmContext ? AutoBossState.ReturnToFarmMap : AutoBossState.TeleportHome);
            return;
        }

        if (_stateTimer > Config.CombatTimeoutSec)
        {
            Plugin.Log.LogWarning($"[AutoBoss] Combat timeout {_stateTimer:F0}s → skip boss");
            _currentBoss = null;
            // Timeout → tắt auto Q
            if (_bossAttackEngaged)
            {
                Plugin.Log.LogInfo("[AutoBoss] Combat timeout -> stop auto attack Q");
                AutoPickupLite.SetAutoAttack(false);
                _bossAttackEngaged = false;
            }
            Transition(AutoBossState.LootDrops);
            return;
        }

        // Giữ auto attack Q LUÔN ON trong combat: đọc trạng thái thật từ game,
        // chỉ bấm khi game tự tắt (vd sau khi dùng skill số).
        if (!BossSkillManager.IsSkillInProgress())
        {
            AutoPickupLite.SetAutoAttack(true);
            _bossAttackEngaged = true;
        }

        // Tự động dùng skill khi HP boss xuống ngưỡng cấu hình (HP thật).
        // Dừng ở trigger đầu tiên bắn được để không tiêu nhiều skill cùng frame.
        if (Config.BossSkillTriggers != null)
        {
            foreach (var trigger in Config.BossSkillTriggers)
            {
                if (BossSkillManager.UseSkillAtBossHp(_currentBoss, trigger.HpThreshold, trigger.SkillKey))
                    break;
            }
        }

        // Skill số làm game tắt auto Q. Đồng bộ cờ nội bộ ngay sau trigger,
        // để LootDrops không bấm Q lần nữa và vô tình bật auto lại.
        if (BossSkillManager.ConsumeAutoAttackDisabledBySkill())
        {
            // _bossAttackEngaged = false;
            Plugin.Log.LogInfo("[AutoBoss] Skill press disabled game auto Q -> keeping _bossAttackEngaged=true to ensure LootDrops turns it off if needed");
        }

        // Cập nhật HP boss lên UI overlay
        AutoBossUI.Instance?.UpdateState(State.ToString(), BossSkillManager.GetLastKnownBossHp(_currentBoss));

        // Pickup item gần đó trong lúc đánh
        AutoPickupLite.PickupNearest(Config.LootRadius);
    }

    private void RunLootDrops()
    {
        var picked = AutoPickupLite.PickupNearest(Config.LootRadius);
        if (!picked) _noItemTimer += Time.deltaTime;
        else _noItemTimer = 0f;

        if (_noItemTimer > Config.LootIdleTimeoutSec)
        {
            Transition(AutoBossState.ReturnToFarmMap);
        }
    }

    private void RunReturnToFarmMap()
    {
        if (!_hasSavedFarmContext || string.IsNullOrEmpty(_savedFarmMap))
        {
            Plugin.Log.LogWarning($"[AutoBoss] ReturnToFarmMap aborted: saved farm context missing (hasContext={_hasSavedFarmContext}, map='{_savedFarmMap}', zone={_savedFarmZone}) -> FarmTown");
            Transition(AutoBossState.FarmTown);
            return;
        }

        // Hàm này chạy mỗi tick — chỉ log DEBUG mỗi 5s để không ngập LogOutput
        if (Time.time - _lastReturnDebugLogAt >= 5f)
        {
            _lastReturnDebugLogAt = Time.time;
            Plugin.Log.LogInfo($"[AutoBoss] ReturnToFarmMap DEBUG: savedFarmMap='{_savedFarmMap}', savedFarmAnchor='{_savedFarmAnchor}', savedFarmChainIndex={_savedFarmChainIndex}");
        }

        string currentMap = GameAPI.GetCurrentMapFromMiniMap();
        if (string.IsNullOrEmpty(currentMap))
            currentMap = GameAPI.GetCurrentMapName();

        if (IsTownMap(currentMap))
        {
            Plugin.Log.LogInfo($"[AutoBoss] ReturnToFarmMap while in town-like map '{currentMap}' -> keep returning to saved farm '{_savedFarmMap}'");
        }

        if (GameAPI.IsInMap(_savedFarmMap))
        {
            Plugin.Log.LogInfo($"[AutoBoss] Already back in saved farm map '{_savedFarmMap}'");
            Transition(AutoBossState.RestoreFarmZone);
            return;
        }

        if (_teleportInProgress)
        {
            if (Time.time >= _teleportCooldownUntil)
            {
                if (GameAPI.IsInMap(_savedFarmMap))
                {
                    Plugin.Log.LogInfo($"[AutoBoss] Return teleport confirmed by IsInMap('{_savedFarmMap}')");
                    _teleportInProgress = false;
                    MapTransporter.ResetTeleportSession();
                    MapTransporter.CloseTeleportPanel();
                    Transition(AutoBossState.RestoreFarmZone);
                    return;
                }

                Plugin.Log.LogWarning($"[AutoBoss] Return teleport timeout for '{_savedFarmMap}'");
                _teleportInProgress = false;
                MapTransporter.ResetTeleportSession();
            }
            return;
        }

        _sceneNameBeforeTeleport = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        // === FIX: 3 cases để về farm map ===
        // Case 1: Farm ở chain map (dungeon) → Teleport về anchor → WalkToPortal ngược lại
        // Case 2: Farm ở anchor map → Dùng teleport menu với tên map
        // Case 3: Farm ở map thường → Dùng nút "Quay lại"

        if (_savedFarmChainIndex >= 0 && !string.IsNullOrEmpty(_savedFarmAnchor))
        {
            // Case 1: Farm trong chain → Reverse route
            Plugin.Log.LogInfo($"[AutoBoss] Saved farm '{_savedFarmMap}' is chain step {_savedFarmChainIndex} → teleport to anchor '{_savedFarmAnchor}' then walk");

            // Check đã ở anchor chưa
            if (GameAPI.IsInMap(_savedFarmAnchor))
            {
                Plugin.Log.LogInfo($"[AutoBoss] Already at anchor '{_savedFarmAnchor}' → start reverse walk to chain step {_savedFarmChainIndex}");
                _portalChainIndex = 0;  // Reset chain walker
                Transition(AutoBossState.ReverseWalkToFarm);
                return;
            }

            // Teleport về anchor
            if (MapTransporter.OpenMenuAndTeleport(_savedFarmAnchor))
            {
                _teleportInProgress = true;
                _teleportTargetMap = _savedFarmAnchor;
                _teleportCooldownUntil = Time.time + 8f;
                _teleportClickedAt = Time.time;
                Plugin.Log.LogInfo($"[AutoBoss] ReturnToFarmMap teleporting to anchor '{_savedFarmAnchor}' for reverse walk");
                return;
            }
        }
        else
        {
            bool isFarmingAtAnchor = !string.IsNullOrEmpty(Config.FastTravelAnchorMap) &&
                                     _savedFarmMap.IndexOf(Config.FastTravelAnchorMap, StringComparison.OrdinalIgnoreCase) >= 0;

            if (isFarmingAtAnchor)
            {
                // Case 2: Farm ở anchor → Dùng teleport menu với tên map
                Plugin.Log.LogInfo($"[AutoBoss] Saved farm '{_savedFarmMap}' is anchor → use teleport menu");
                if (MapTransporter.OpenMenuAndTeleport(_savedFarmMap))
                {
                    _teleportInProgress = true;
                    _teleportTargetMap = _savedFarmMap;
                    _teleportCooldownUntil = Time.time + 8f;
                    _teleportClickedAt = Time.time;
                    Plugin.Log.LogInfo($"[AutoBoss] ReturnToFarmMap teleporting to anchor '{_savedFarmMap}' via menu");
                    return;
                }
            }
            else
            {
                // Case 3: Farm ở map thường → Click nút "Quay lại"
                string[] returnButtons = { "Quay lại", "Quay lai", "Return", "Back", "Previous" };
                foreach (var btn in returnButtons)
                {
                    if (MapTransporter.OpenMenuAndTeleport(btn))
                    {
                        _teleportInProgress = true;
                        _teleportTargetMap = _savedFarmMap;
                        _teleportCooldownUntil = Time.time + 8f;
                        _teleportClickedAt = Time.time;
                        Plugin.Log.LogInfo($"[AutoBoss] ReturnToFarmMap clicking '{btn}' button to return to '{_savedFarmMap}'");
                        return;
                    }
                }
            }
        }

        if (_stateTimer > ReturnToFarmTimeoutSec)
        {
            Plugin.Log.LogWarning($"[AutoBoss] ReturnToFarmMap timeout (no button/menu found) -> FarmTown fallback");
            _hasSavedFarmContext = false;
            Transition(AutoBossState.FarmTown);
        }
    }

    private void RunReverseWalkToFarm()
    {
        // Đi ngược lại vào chain map để về đúng farm map
        // Được gọi sau khi đã teleport về anchor
        if (_savedFarmChainIndex < 0 || _savedFarmPortalChain == null)
        {
            Plugin.Log.LogWarning("[AutoBoss] ReverseWalkToFarm called but no saved chain info -> RestoreFarmZone");
            Transition(AutoBossState.RestoreFarmZone);
            return;
        }

        string targetChainMap = _portalChainIndex <= _savedFarmChainIndex
            ? _savedFarmPortalChain[_portalChainIndex]
            : "";

        if (string.IsNullOrEmpty(targetChainMap))
        {
            Plugin.Log.LogWarning($"[AutoBoss] ReverseWalkToFarm: invalid chain index {_portalChainIndex} -> RestoreFarmZone");
            Transition(AutoBossState.RestoreFarmZone);
            return;
        }

        // Check đã đến farm map chưa
        if (GameAPI.IsInMap(_savedFarmMap))
        {
            Plugin.Log.LogInfo($"[AutoBoss] ReverseWalkToFarm: reached farm map '{_savedFarmMap}' -> RestoreFarmZone");
            Transition(AutoBossState.RestoreFarmZone);
            return;
        }

        // Check đã ở target chain map chưa
        string currentMap = GameAPI.GetCurrentMapFromMiniMap();
        if (string.IsNullOrEmpty(currentMap))
            currentMap = GameAPI.GetCurrentMapName();

        if (GameAPI.IsInMap(targetChainMap))
        {
            Plugin.Log.LogInfo($"[AutoBoss] ReverseWalkToFarm: reached chain step {_portalChainIndex} ('{targetChainMap}')");

            // Đã đến farm map (cuối chain) → Done
            if (_portalChainIndex == _savedFarmChainIndex)
            {
                Plugin.Log.LogInfo($"[AutoBoss] ReverseWalkToFarm: reached final farm map '{_savedFarmMap}' -> RestoreFarmZone");
                Transition(AutoBossState.RestoreFarmZone);
                return;
            }

            // Chưa đến farm map → Reset để tìm portal tiếp theo
            _portalChainIndex++;
            _cachedGatewayX = 0f;
            _cachedGatewayY = 0f;
            _portalMoveIssued = false;
            _lastPortalSignature = "";
            _stateTimer = 0f;
            Plugin.Log.LogInfo($"[AutoBoss] ReverseWalkToFarm: moving to next chain step {_portalChainIndex}");
            return;
        }

        // Tìm và đi tới gateway (copy logic từ RunWalkToPortal)
        var myPos = GameAPI.GetPlayerPosition();
        string currentSig = $"{currentMap}_{targetChainMap}";

        if (_cachedGatewayX == 0f && _cachedGatewayY == 0f || _lastPortalSignature != currentSig)
        {
            float bestX = -9999f, bestY = 0f;
            bool found = false;

            try
            {
                var changeMaps = GameAPI.FindChangeMaps();
                if (changeMaps != null)
                {
                    foreach (var cm in changeMaps)
                    {
                        if (cm == null) continue;
                        var go = cm as UnityEngine.Component;
                        if (go == null) continue;
                        var p = go.transform.position;
                        if (p.x > bestX) { bestX = p.x; bestY = p.y; found = true; }
                    }
                    if (found)
                        Plugin.Log.LogInfo($"[AutoBoss] ReverseWalkToFarm: Found ChangeMap @ ({bestX:F0},{bestY:F0})");
                }
            }
            catch { }

            if (!found)
            {
                try
                {
                    var allGos = UnityEngine.Object.FindObjectsOfType<GameObject>();
                    foreach (var go in allGos)
                    {
                        if (go == null || !go.activeInHierarchy) continue;
                        string n = go.name ?? "";
                        if (n.IndexOf("Portal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Gateway", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("ChangeMap", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var p = go.transform.position;
                            if (p.x > bestX) { bestX = p.x; bestY = p.y; found = true; }
                        }
                    }
                    if (found)
                        Plugin.Log.LogInfo($"[AutoBoss] ReverseWalkToFarm: Found Portal GameObject @ ({bestX:F0},{bestY:F0})");
                }
                catch { }
            }

            if (!found)
            {
                if (_stateTimer > 5f)
                    Plugin.Log.LogWarning($"[AutoBoss] ReverseWalkToFarm: No portal found after {_stateTimer:F1}s");
                return;
            }

            _cachedGatewayX = bestX;
            _cachedGatewayY = bestY;
            _lastPortalSignature = currentSig;
        }

        float dist = Mathf.Abs(myPos.x - _cachedGatewayX) + Mathf.Abs(myPos.y - _cachedGatewayY);
        bool stalled = UpdatePortalProgress(myPos.x, myPos.y, _cachedGatewayX, _cachedGatewayY);

        if (dist > 1.2f)
        {
            if (!_portalMoveIssued || (stalled && Time.time - _lastPortalMoveIssuedAt >= 0.9f))
            {
                GameAPI.MoveToPathOnly(_cachedGatewayX, _cachedGatewayY);
                _portalMoveIssued = true;
                _lastPortalMoveIssuedAt = Time.time;
                if (Time.time % 2f < 0.1f)
                    Plugin.Log.LogInfo($"[AutoBoss] ReverseWalkToFarm: portal-walk target=({_cachedGatewayX:F0},{_cachedGatewayY:F0}) dist={dist:F1}");
            }
        }
        else if (stalled && dist > 0.15f && Time.time - _lastPortalMoveIssuedAt >= 0.9f)
        {
            GameAPI.MoveToPathOnly(_cachedGatewayX, _cachedGatewayY);
            _portalMoveIssued = true;
            _lastPortalMoveIssuedAt = Time.time;
        }

        if (_stateTimer > 15f)
        {
            Plugin.Log.LogWarning($"[AutoBoss] ReverseWalkToFarm timeout for step {_portalChainIndex} -> FarmTown fallback");
            _hasSavedFarmContext = false;
            Transition(AutoBossState.FarmTown);
        }
    }

    private void RunRestoreFarmZone()
    {
        if (!_hasSavedFarmContext)
        {
            Plugin.Log.LogWarning("[AutoBoss] No saved farm context while restoring zone -> ResumeFarming fallback");
            Transition(AutoBossState.ResumeFarming);
            return;
        }

        if (!_restoreStarted)
        {
            int targetZone = _savedFarmZone >= 0 ? _savedFarmZone : 0;
            Plugin.Log.LogInfo($"[AutoBoss] RestoreFarmZone: starting restore to zone={targetZone}, tab={_savedFarmZoneTab}");
            ZoneSwitcher.StartRestore(targetZone, _savedFarmZoneTab);
            _restoreStarted = true;
            return;
        }

        // Log chi tiết mỗi 1.5s để theo dõi quá trình restore
        if (Time.time % 1.5f < 0.1f)
        {
            Plugin.Log.LogInfo($"[AutoBoss] RestoreFarmZone: tick (stateTimer={_stateTimer:F1}s, target={_savedFarmZone}, isRestoring={ZoneSwitcher.IsRestoring})");
        }

        if (ZoneSwitcher.TickRestore())
        {
            if (ZoneSwitcher.RestoreFailed)
                Plugin.Log.LogWarning("[AutoBoss] RestoreFarmZone failed -> continue ResumeFarming anyway");
            else
                Plugin.Log.LogInfo($"[AutoBoss] RestoreFarmZone SUCCESS -> zone {_savedFarmZone} restored -> ResumeFarming");
            Transition(AutoBossState.ResumeFarming);
            return;
        }

        if (_stateTimer > RestoreFarmZoneTimeoutSec)
        {
            Plugin.Log.LogWarning($"[AutoBoss] RestoreFarmZone timeout after {_stateTimer:F1}s (limit={RestoreFarmZoneTimeoutSec}s) -> ResumeFarming fallback");
            Transition(AutoBossState.ResumeFarming);
        }
    }

    private void RunResumeFarming()
    {
        // Bật auto Q 1 lần rồi kết thúc tool
        if (!_farmAttackEngaged)
        {
            int currentZone = GameAPI.GetCurrentZoneIndexFromHUD();
            string currentMap = GameAPI.GetCurrentMapFromMiniMap();
            if (string.IsNullOrEmpty(currentMap))
                currentMap = GameAPI.GetCurrentMapName();
            Plugin.Log.LogInfo($"[AutoBoss] ResumeFarming: map='{currentMap}', zone={currentZone} -> engage auto Q, stay ready for next boss");

            // Nhấn P (hợp thể) trước khi bật Q farm (nếu AutoFusion bật)
            if (Config.AutoFusion)
                AutoPickupLite.TapFusionKey();

            AutoPickupLite.SetAutoAttack(true);
            _farmAttackEngaged = true;

            Plugin.Log.LogInfo("[AutoBoss] ResumeFarming complete -> Idle, waiting for next boss (Q still on)");
            Transition(AutoBossState.Idle);
        }
    }

    private void SaveCurrentFarmContext()
    {
        string map = GameAPI.GetCurrentMapFromMiniMap();
        if (string.IsNullOrEmpty(map))
            map = GameAPI.GetCurrentMapName();

        if (string.IsNullOrEmpty(map) || IsTownMap(map))
            return;

        string fastTravelBossMap = "";
        if (Config.BossMapNames != null && Config.BossMapNames.Count > 0)
            fastTravelBossMap = GetFastTravelMapName(Config.BossMapNames[0]);

        bool isBossMap = false;
        if (Config.BossMapNames != null)
        {
            foreach (var bossMap in Config.BossMapNames)
            {
                if (!string.IsNullOrEmpty(bossMap) && map.IndexOf(bossMap, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isBossMap = true;
                    break;
                }
            }
        }
        if (!isBossMap && !string.IsNullOrEmpty(fastTravelBossMap) && map.IndexOf(fastTravelBossMap, StringComparison.OrdinalIgnoreCase) >= 0)
            isBossMap = true;

        if (isBossMap)
            return;

        // === FIX: Detect nếu farm map nằm trong portal chain ===
        // Nếu có → lưu thông tin reverse route (anchor + chain + index)
        // Nếu không → lưu bình thường
        _savedFarmChainIndex = -1;
        _savedFarmAnchor = "";
        _savedFarmPortalChain = null;

        if (Config.PortalChainMaps != null && Config.PortalChainMaps.Count > 0)
        {
            for (int i = 0; i < Config.PortalChainMaps.Count; i++)
            {
                string chainMap = Config.PortalChainMaps[i];
                if (string.IsNullOrEmpty(chainMap)) continue;

                if (map.IndexOf(chainMap, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Farm ở map trong chain → lưu thông tin để reverse route
                    _savedFarmChainIndex = i;
                    _savedFarmAnchor = Config.FastTravelAnchorMap ?? "";
                    _savedFarmPortalChain = new List<string>(Config.PortalChainMaps);
                    Plugin.Log.LogInfo($"[AutoBoss] Farm map '{map}' is chain step {i} → will reverse route via anchor '{_savedFarmAnchor}'");
                    break;
                }
            }
        }

        _savedFarmMap = map;
        _savedFarmZone = GameAPI.GetCurrentZoneIndexFromHUD();
        // Đọc trực tiếp từ màn hình, không cần mở bảng Khu gây giật lag
        _savedFarmZoneTab = GameAPI.IsCurrentZoneChaotic()
            ? ZoneSwitcher.ZoneTab.Chaotic
            : ZoneSwitcher.ZoneTab.Normal;
        _hasSavedFarmContext = true;

        string reverseInfo = _savedFarmChainIndex >= 0
            ? $" (chain step {_savedFarmChainIndex}, anchor='{_savedFarmAnchor}')"
            : "";
        Plugin.Log.LogInfo($"[AutoBoss] Saved farm context map='{_savedFarmMap}', zone={_savedFarmZone}, tab={_savedFarmZoneTab}{reverseInfo}");
    }

    private void RunTeleportHome()
    {
        _sceneNameBeforeTeleport = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Plugin.Log.LogWarning($"[AutoBoss] RunTeleportHome entered (state={State}, hasSavedFarm={_hasSavedFarmContext}, savedMap='{_savedFarmMap}', savedZone={_savedFarmZone}, stateTimer={_stateTimer:F1})");
        if (MapTransporter.GoHome())
        {
            _teleportInProgress = true;
            _teleportCooldownUntil = Time.time + 8f;
            _teleportTargetMap = "Home";
            _teleportClickedAt = Time.time;
            MapTransporter.ResetTeleportSession();
            Plugin.Log.LogInfo($"[AutoBoss] TeleportHome clicked from scene '{_sceneNameBeforeTeleport}'. Waiting up to 8s...");
            // KHÔNG transition ngay - sẽ transition từ state guard khi scene đổi
        }
        else if (_stateTimer > Config.TeleportTimeoutSec)
        {
            Plugin.Log.LogWarning("[AutoBoss] TeleportHome failed -> Force FarmTown");
            _teleportInProgress = false;
            MapTransporter.ResetTeleportSession();
            Transition(AutoBossState.FarmTown);
        }
    }

    /// <summary>
    /// State guard chung: nếu đang trong cooldown teleport mà scene đã đổi → hoàn tất teleport.
    /// Được gọi đầu Update() để áp dụng cho MỌI state (TeleportToBossMap, TeleportHome).
    /// </summary>
    private void TeleportStateGuard(string expectedFastTravelMap, string nextStateIfSame, string nextStateIfDifferent)
    {
        if (!_teleportInProgress) return;
        if (string.IsNullOrEmpty(_sceneNameBeforeTeleport)) return;

        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (string.IsNullOrEmpty(currentScene)) return;

        if (currentScene != _sceneNameBeforeTeleport)
        {
            Plugin.Log.LogInfo($"[AutoBoss] Teleport guard: scene changed '{_sceneNameBeforeTeleport}' -> '{currentScene}'. Target='{_teleportTargetMap}'.");
            _teleportInProgress = false;
            _sceneNameBeforeTeleport = "";
            MapTransporter.ResetTeleportSession();
            MapTransporter.CloseTeleportPanel();

            if (!string.IsNullOrEmpty(expectedFastTravelMap) && GameAPI.IsInMap(expectedFastTravelMap))
                Transition(nextStateIfSame == null ? AutoBossState.ZoneScanLoop : ParseState(nextStateIfSame));
            else
                Transition(nextStateIfDifferent == null ? AutoBossState.FarmTown : ParseState(nextStateIfDifferent));
            return;
        }

        // Fallback timeout
        if (Time.time >= _teleportCooldownUntil)
        {
            Plugin.Log.LogWarning($"[AutoBoss] Teleport guard: timeout. Scene still '{currentScene}'. Target='{_teleportTargetMap}'.");
            _teleportInProgress = false;
            _sceneNameBeforeTeleport = "";
            MapTransporter.ResetTeleportSession();
            // Không retry ở đây - để state handler quyết định
        }
    }

    private AutoBossState ParseState(string s) => s switch
    {
        "Idle" => AutoBossState.Idle,
        "FarmTown" => AutoBossState.FarmTown,
        "TeleportToBossMap" => AutoBossState.TeleportToBossMap,
        "WalkToPortal" => AutoBossState.WalkToPortal,
        "ZoneScanLoop" => AutoBossState.ZoneScanLoop,
        "MoveToBoss" => AutoBossState.MoveToBoss,
        "CombatBoss" => AutoBossState.CombatBoss,
        "LootDrops" => AutoBossState.LootDrops,
        "ReturnToFarmMap" => AutoBossState.ReturnToFarmMap,
        "ReverseWalkToFarm" => AutoBossState.ReverseWalkToFarm,
        "RestoreFarmZone" => AutoBossState.RestoreFarmZone,
        "ResumeFarming" => AutoBossState.ResumeFarming,
        "TeleportHome" => AutoBossState.TeleportHome,
        "SolveCaptcha" => AutoBossState.SolveCaptcha,
        "DeadRecovery" => AutoBossState.DeadRecovery,
        _ => AutoBossState.FarmTown
    };

    private void RunDeadRecovery()
    {
        var deathPanel = GameAPI.FindDeathPanel() as UnityEngine.Component;
        if (deathPanel != null)
        {
            var btns = deathPanel.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            foreach (var btn in btns)
            {
                if (btn == null) continue;
                string t = UIHelper.GetButtonText(btn);
                if (t.IndexOf("hồi sinh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("hoi sinh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("respawn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Plugin.Log.LogInfo($"[AutoBoss] Clicking respawn button: '{t}'");
                    btn.onClick.Invoke();
                    break;
                }
            }
        }

        // Timeout tăng lên 10s để đảm bảo animation respawn hoàn tất
        if (_stateTimer > DeadRecoveryTimeout)
        {
            _currentBoss = null;
            Plugin.Log.LogWarning(_hasSavedFarmContext
                ? "[AutoBoss] DeadRecovery done → return to saved farm"
                : "[AutoBoss] DeadRecovery done → TeleportHome (no saved farm context)");
            Transition(_hasSavedFarmContext ? AutoBossState.ReturnToFarmMap : AutoBossState.TeleportHome);
        }
    }

    // ===== Hotkeys =====

    public void EnableAutoFarmMode()
    {
        if (Config == null) return;
        Config.Enabled = true;
        Plugin.Log.LogInfo($"[AutoBoss] ENABLED (via AutoLogin)");
        if (State == AutoBossState.Idle)
        {
            BossNotificationDetector.ResetCooldown();
            string currentMap = GameAPI.GetCurrentMapFromMiniMap();
            if (string.IsNullOrEmpty(currentMap))
                currentMap = GameAPI.GetCurrentMapName();

            string normCurrentMap = NormalizeText(currentMap);
            bool isInBossMap = false;
            bool isInChainMap = false;
            bool isInAnchorMap = false;
            int chainMapIndex = -1;

            if (Config.BossMapNames != null)
            {
                foreach (var bossMap in Config.BossMapNames)
                {
                    if (!string.IsNullOrEmpty(bossMap) && normCurrentMap.IndexOf(NormalizeText(bossMap), StringComparison.Ordinal) >= 0)
                    {
                        isInBossMap = true;
                        break;
                    }
                }
            }
            if (!isInBossMap && Config.PortalChainMaps != null)
            {
                for (int i = 0; i < Config.PortalChainMaps.Count; i++)
                {
                    string chainMap = Config.PortalChainMaps[i];
                    if (!string.IsNullOrEmpty(chainMap) && normCurrentMap.IndexOf(NormalizeText(chainMap), StringComparison.Ordinal) >= 0)
                    {
                        isInChainMap = true;
                        chainMapIndex = i;
                        break;
                    }
                }
            }
            if (!isInBossMap && !isInChainMap && !string.IsNullOrEmpty(Config.FastTravelAnchorMap))
            {
                if (normCurrentMap.IndexOf(NormalizeText(Config.FastTravelAnchorMap), StringComparison.Ordinal) >= 0)
                {
                    isInAnchorMap = true;
                }
            }

            SaveCurrentFarmContext();

            if (isInBossMap)
            {
                _currentMapIndex = 0;
                Transition(AutoBossState.ZoneScanLoop);
            }
            else if (isInChainMap)
            {
                _currentMapIndex = 0;
                _portalChainIndex = chainMapIndex + 1;
                Transition(AutoBossState.WalkToPortal);
            }
            else if (isInAnchorMap)
            {
                _currentMapIndex = 0;
                _portalChainIndex = 0;
                Transition(AutoBossState.WalkToPortal);
            }
            else
            {
                Transition(AutoBossState.FarmTown);
            }
        }
    }

    private void HandleHotkeys()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            Config.Enabled = !Config.Enabled;
            Plugin.Log.LogInfo($"[AutoBoss] {(Config.Enabled ? "ENABLED" : "DISABLED")} (manual F1)");
            if (Config.Enabled && State == AutoBossState.Idle)
            {
                // Reset cooldown để không bị block bởi detection cooldown
                BossNotificationDetector.ResetCooldown();

                // FIX: Detect xem có đang ở đâu để quyết định flow
                string currentMap = GameAPI.GetCurrentMapFromMiniMap();
                if (string.IsNullOrEmpty(currentMap))
                    currentMap = GameAPI.GetCurrentMapName();

                Plugin.Log.LogWarning($"[AutoBoss] F1 DEBUG: currentMap='{currentMap}'");

                // Normalize để so sánh không phân biệt dấu/hoa-thường tiếng Việt
                string normCurrentMap = NormalizeText(currentMap);

                bool isInBossMap = false;
                bool isInChainMap = false;
                bool isInAnchorMap = false;
                int chainMapIndex = -1;

                // Check Boss Map (đích cuối - đổi khu ngay)
                if (Config.BossMapNames != null)
                {
                    foreach (var bossMap in Config.BossMapNames)
                    {
                        if (!string.IsNullOrEmpty(bossMap) &&
                            normCurrentMap.IndexOf(NormalizeText(bossMap), StringComparison.Ordinal) >= 0)
                        {
                            isInBossMap = true;
                            Plugin.Log.LogInfo($"[AutoBoss] F1 pressed in Boss Map '{currentMap}' → ZoneScanLoop");
                            break;
                        }
                    }
                }

                // Check Chain Map (map trung gian - phải đi tiếp)
                if (!isInBossMap && Config.PortalChainMaps != null)
                {
                    for (int i = 0; i < Config.PortalChainMaps.Count; i++)
                    {
                        string chainMap = Config.PortalChainMaps[i];
                        if (!string.IsNullOrEmpty(chainMap) &&
                            normCurrentMap.IndexOf(NormalizeText(chainMap), StringComparison.Ordinal) >= 0)
                        {
                            isInChainMap = true;
                            chainMapIndex = i;
                            Plugin.Log.LogInfo($"[AutoBoss] F1 pressed in Chain Map '{currentMap}' (step {i}) → WalkToPortal to boss map");
                            break;
                        }
                    }
                }

                // Check Anchor Map (map neo - phải đi tiếp qua chain)
                if (!isInBossMap && !isInChainMap && !string.IsNullOrEmpty(Config.FastTravelAnchorMap))
                {
                    if (normCurrentMap.IndexOf(NormalizeText(Config.FastTravelAnchorMap), StringComparison.Ordinal) >= 0)
                    {
                        isInAnchorMap = true;
                        Plugin.Log.LogInfo($"[AutoBoss] F1 pressed in Anchor Map '{currentMap}' → WalkToPortal to boss map");
                    }
                }

                // Lưu farm context trước khi đi
                SaveCurrentFarmContext();

                if (isInBossMap)
                {
                    // Đang ở map boss → quét zone ngay
                    _currentMapIndex = 0;
                    Transition(AutoBossState.ZoneScanLoop);
                }
                else if (isInChainMap)
                {
                    // Đang ở map trung gian → đi tiếp qua portal
                    _currentMapIndex = 0;
                    _portalChainIndex = chainMapIndex + 1; // Bắt đầu từ bước TIẾP THEO
                    Transition(AutoBossState.WalkToPortal);
                }
                else if (isInAnchorMap)
                {
                    // Đang ở anchor → đi qua chain từ đầu
                    _currentMapIndex = 0;
                    _portalChainIndex = 0;
                    Transition(AutoBossState.WalkToPortal);
                }
                else
                {
                    // Đang ở farm/town → teleport đến boss map
                    Plugin.Log.LogInfo($"[AutoBoss] F1 pressed in Farm/Town '{currentMap}' → FarmTown");
                    Transition(AutoBossState.FarmTown);
                }
            }
            else if (!Config.Enabled)
            {
                // Tắt tool: tắt cả 2 loại auto attack (boss và farm)
                if (_farmAttackEngaged)
                {
                    Plugin.Log.LogInfo("[AutoBoss] Disabled while farm auto engaged -> stop auto attack Q");
                    AutoPickupLite.SetAutoAttack(false);
                    _farmAttackEngaged = false;
                }
                if (_bossAttackEngaged)
                {
                    Plugin.Log.LogInfo("[AutoBoss] Disabled while boss auto engaged -> stop auto attack Q");
                    AutoPickupLite.SetAutoAttack(false);
                    _bossAttackEngaged = false;
                }
            }
        }
        if (Input.GetKeyDown(KeyCode.F2))
        {
            Plugin.Log.LogInfo("[UI] Dumping panels + runtime types...");
            UiPanelDumper.DumpAll("ui_panel_dump.txt");
            UiPanelDumper.DumpRuntimeTypes("runtime_types.txt");
        }
        if (Input.GetKeyDown(KeyCode.F6))
        {
            Plugin.Log.LogInfo("[Network] Dumping network classes...");
            NetworkClassDumper.DumpNetworkClasses(Plugin.Log);
        }
        if (Input.GetKeyDown(KeyCode.F7))
        {
            Plugin.Log.LogInfo("[Inspector] Dumping UI text hierarchy...");
            UITextInspector.DumpAllActiveTexts($"ui_text_inspector_{DateTime.Now:HHmmss}.txt");
        }
        if (Input.GetKeyDown(KeyCode.F3))
        {
            Plugin.Log.LogInfo("[Test] Open teleport menu -> teleport to boss map");
            if (Config.BossMapNames != null && Config.BossMapNames.Count > 0)
                MapTransporter.OpenMenuAndTeleport(Config.BossMapNames[0]);
        }
        if (Input.GetKeyDown(KeyCode.F4))
        {
            Plugin.Log.LogInfo("[Test] Next zone");
            ZoneSwitcher.NextZone();
        }
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Plugin.Log.LogInfo("[Test] Go home");
            MapTransporter.GoHome();
        }
        if (Input.GetKeyDown(KeyCode.F8))
        {
            // Bang thong ke cmd -> dung de chot cmd nao la boss announce.
            Plugin.Log.LogInfo("[MessageHook] Dumping command stats...");
            MessageHook.Flush();
            MessageHook.DumpCommandStats();
        }
    }



    // ===== Transition helper =====

    public void Transition(AutoBossState newState)
    {
        if (State == newState) return;
        Plugin.Log.LogInfo($"[AutoBoss] State: {State} → {newState}");
        AutoBossState oldState = State;
        State = newState;
        _stateTimer = 0f;
        if (newState == AutoBossState.ZoneScanLoop)
        {
            _zoneAttempts = 0;
            _zoneSwitchTimer = 0f;
            _enteredZoneAt = Time.time;
            _captchaTriggered = false;
            if (oldState != AutoBossState.SolveCaptcha)
                ZoneSwitcher.ResetState();
            BossDetector.InvalidateCache(); // Force rescan boss khi vào zone mới
        }
        if (newState == AutoBossState.MoveToBoss) ResetBossApproachState();
        if (newState == AutoBossState.LootDrops)
        {
            _noItemTimer = 0f;
            Plugin.Log.LogInfo("[AutoBoss] Entering LootDrops -> enforce stop auto attack Q");
            AutoPickupLite.SetAutoAttack(false);
            _bossAttackEngaged = false;
        }
        if (newState == AutoBossState.FarmTown) { _currentBoss = null; _scanTimer = 0f; }
        if (newState == AutoBossState.RestoreFarmZone) _restoreStarted = false;
        // if (oldState == AutoBossState.CombatBoss && newState != AutoBossState.CombatBoss && _bossAttackEngaged)
        // {
        //     Plugin.Log.LogInfo("[AutoBoss] CombatBoss -> stop auto attack Q");
        //     AutoPickupLite.TapAttackKey();
        //     _bossAttackEngaged = false;
        // }
        if (newState == AutoBossState.CombatBoss)
        {
            _bossAttackEngaged = false;
            _bossMissingTimer = 0f;
            BossSkillManager.ResetForNewCombat();
            // Vào combat: click boss ngay 1 lần (bỏ qua throttle) trước khi bật auto Q
            BossClicker.ClickBoss(_currentBoss, true);
        }
        // Vòng đời Q riêng cho ResumeFarming, KHÔNG dùng chung với boss combat.
        // Khi ResumeFarming → Idle: KHÔNG tắt Q, để nhân vật tiếp tục farm.
        // Chỉ tắt Q khi rời ResumeFarming sang state khác (tele boss, loot...) hoặc F1 tắt tool.
        if (oldState == AutoBossState.ResumeFarming && newState != AutoBossState.ResumeFarming
            && newState != AutoBossState.Idle && _farmAttackEngaged)
        {
            Plugin.Log.LogInfo("[AutoBoss] ResumeFarming -> stop auto attack Q");
            AutoPickupLite.SetAutoAttack(false);
            _farmAttackEngaged = false;
        }
        if (newState == AutoBossState.ResumeFarming) _farmAttackEngaged = false;
        // Reset teleport progress khi ra khỏi TeleportToBossMap / ReturnToFarmMap
        if (oldState == AutoBossState.TeleportToBossMap && newState != AutoBossState.TeleportToBossMap)
            _teleportInProgress = false;
        if (oldState == AutoBossState.ReturnToFarmMap && newState != AutoBossState.ReturnToFarmMap)
            _teleportInProgress = false;
        if (newState == AutoBossState.TeleportToBossMap) _portalChainIndex = 0;
        // Reset zoneAttempts khi chuyển sang WalkToPortal
        if (newState == AutoBossState.WalkToPortal) _zoneAttempts = 0;
    }
}
