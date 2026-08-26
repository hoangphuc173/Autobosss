using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AutoBossGrabber;

/// <summary>
/// Quản lý việc dùng skill khi đánh boss.
/// - UseSkillAtBossHp(boss, hpThreshold): Dùng skill khi HP boss <= threshold (HP thật, không phải %).
/// - Ưu tiên nhấn phím số (1-4) → fallback click button → fallback reflection.
/// - Rate-limit để không spam skill.
/// </summary>
public static class BossSkillManager
{
    private static float _lastSkillUsedAt = -999f;
    private static readonly Dictionary<int, float> _skillCooldowns = new Dictionary<int, float>();
    private static int _pendingSkillToPress;
    private static int _pendingPressesLeft;
    private static float _pendingNextPressAt = -999f;
    private static float _skillCastEndTime = -999f;

    private const float GlobalSkillCooldown = 1.5f;
    private const float PerSkillCooldown = 30f; // đủ dài để không spam, nhưng vẫn trigger lại nếu boss heal rồi xuống lại
    private const float SkillPressDelay = 0.3f;
    private const int TotalPresses = 5; // Spam click 5 lần để chắc chắn skill ăn (vượt qua animation lock)
    private const float SkillCastDuration = 2.0f;

    // Debug HP log throttle — log mỗi 3s khi đang combat để chẩn đoán
    private static float _lastDebugHpLogAt = -999f;
    private const float DebugHpLogInterval = 3f;

    // Throttle cho "UseSkillByIndex FAILED" để không spam log mỗi frame
    private static float _lastSkillFailLogAt = -999f;
    private const float SkillFailLogInterval = 5f;

    // One-time type dump để tìm method HP đúng
    private static bool _typeDumped = false;

    public static bool IsSkillInProgress()
    {
        return _pendingPressesLeft > 0 || Time.time < _skillCastEndTime;
    }

    /// <summary>
    /// True khi vừa bấm skill — lần bấm đầu làm game TẮT auto attack Q.
    /// AutoBoss đọc cờ này để đồng bộ lại _bossAttackEngaged, tránh bấm Q thừa
    /// (bấm thừa sẽ vô tình BẬT Q lại lúc nhặt đồ).
    /// </summary>
    public static bool ConsumeAutoAttackDisabledBySkill()
    {
        if (!_autoAttackDisabledBySkill) return false;
        _autoAttackDisabledBySkill = false;
        return true;
    }

    private static bool _autoAttackDisabledBySkill = false;

    /// <summary>Gọi khi bắt đầu combat boss mới để reset cooldown skill + cache HP.</summary>
    public static void ResetForNewCombat()
    {
        _skillCooldowns.Clear();
        _lastSkillUsedAt = -999f;
        _pendingSkillToPress = 0;
        _pendingPressesLeft = 0;
        _pendingNextPressAt = -999f;
        _skillCastEndTime = -999f;
        _autoAttackDisabledBySkill = false;
        // Xoá luôn cache HP: component + HP của trận trước không được dùng cho boss mới,
        // nếu không UI sẽ hiện HP cũ và skill có thể trigger sai ngay khi vừa vào combat.
        _cachedHpTextComponent = null;
        _cachedBossMaxHp = 0;
        _lastKnownBossHp = -1f;
        Plugin.Log.LogInfo("[BossSkill] ResetForNewCombat: cleared skill cooldowns + HP cache");
    }

    // Map skillIndex (1-4) → KeyCode phím số tương ứng
    private static readonly Dictionary<int, KeyCode> _skillKeyMap = new Dictionary<int, KeyCode>
    {
        { 1, KeyCode.Alpha1 },
        { 2, KeyCode.Alpha2 },
        { 3, KeyCode.Alpha3 },
        { 4, KeyCode.Alpha4 },
    };

    /// <summary>
    /// Kiểm tra HP boss và tự động dùng skill nếu HP boss <= hpThreshold (HP thật).
    /// Mỗi lần HP xuống tới ngưỡng sẽ trigger 1 lần (có cooldown 30s chống spam).
    /// Trả về true nếu đã dùng skill.
    /// </summary>
    public static bool UseSkillAtBossHp(object boss, float hpThreshold, int skillIndex = 1)
    {
        if (boss == null) return false;

        try
        {
            if (_pendingPressesLeft > 0)
            {
                if (Time.time < _pendingNextPressAt)
                    return false;

                int pendingSkill = _pendingSkillToPress;
                _pendingPressesLeft--;
                
                if (UseSkillByIndex(pendingSkill))
                {
                    Plugin.Log.LogInfo($"[BossSkill] Sent follow-up press ({TotalPresses - _pendingPressesLeft}/{TotalPresses}) for skill {pendingSkill}");
                    _lastSkillUsedAt = Time.time;
                    _skillCooldowns[pendingSkill] = Time.time;
                    _skillCastEndTime = Time.time + SkillCastDuration;
                    
                    if (_pendingPressesLeft > 0)
                        _pendingNextPressAt = Time.time + SkillPressDelay;
                        
                    return true;
                }

                Plugin.Log.LogWarning($"[BossSkill] Follow-up press for skill {pendingSkill} failed. Retrying...");
                if (_pendingPressesLeft > 0)
                    _pendingNextPressAt = Time.time + SkillPressDelay;
            }

            // Force dump type info 1 lần duy nhất để tìm method HP đúng
            if (!_typeDumped)
            {
                _typeDumped = true;
                Plugin.Log.LogWarning($"[BossSkill] === FORCE DUMP BOSS (first encounter) ===");
                DumpBossType(boss);
            }

            float bossHp = GetBossHp(boss);

            // Debug log throttled mỗi 3s — giúp chẩn đoán HP có đọc được không
            if (Time.time - _lastDebugHpLogAt >= DebugHpLogInterval)
            {
                _lastDebugHpLogAt = Time.time;
                if (bossHp < 0)
                    Plugin.Log.LogWarning($"[BossSkill] DEBUG: GetBossHp={bossHp} (FAIL - không đọc được HP, type={boss.GetType().Name})");
                else
                    Plugin.Log.LogInfo($"[BossSkill] DEBUG: Boss HP={bossHp:F0}, threshold={hpThreshold:F0}, skill={skillIndex}");
            }

            if (bossHp < 0) return false; // không đọc được HP → bỏ qua

            // HP còn cao hơn ngưỡng → chưa đến lúc
            if (bossHp > hpThreshold) return false;

            if (Time.time - _lastSkillUsedAt < GlobalSkillCooldown) return false;

            if (_skillCooldowns.TryGetValue(skillIndex, out float lastUsed))
                if (Time.time - lastUsed < PerSkillCooldown) return false;

            Plugin.Log.LogInfo($"[BossSkill] Boss HP={bossHp:F0} <= {hpThreshold:F0} → first press for skill {skillIndex}");

            if (UseSkillByIndex(skillIndex))
            {
                _autoAttackDisabledBySkill = true;
                _pendingSkillToPress = skillIndex;
                _pendingPressesLeft = TotalPresses - 1;
                _pendingNextPressAt = Time.time + SkillPressDelay;
                Plugin.Log.LogInfo(
                    $"[BossSkill] First press for skill {skillIndex} sent; " +
                    $"scheduled {_pendingPressesLeft} more presses every {SkillPressDelay:F1}s to bypass animation lock");
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[BossSkill] UseSkillAtBossHp fail: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Dùng skill theo index (1-4 tương ứng phím số 1-4 trên bàn phím).
    /// Ưu tiên: ActionButtonController (chắc chắn nhất) → button search → player method → nhấn phím (yếu nhất).
    /// </summary>
    public static bool UseSkillByIndex(int index)
    {
        try
        {
            // Cách 1 (ưu tiên cao nhất): ActionButtonController với pattern từ AutoPickupLite
            var controllers = Il2CppAPI.FindObjectsOfType(typeof(ActionButtonController));
            if (controllers == null || controllers.Length == 0)
                controllers = Il2CppAPI.FindObjectsOfTypeAll(typeof(ActionButtonController));

            if (controllers != null && controllers.Length > 0)
            {
                foreach (var ctrlObj in controllers)
                {
                    if (ctrlObj == null) continue;
                    if (TryClickSkillFromController(ctrlObj, index))
                    {
                        Plugin.Log.LogInfo($"[BossSkill] Clicked skill {index} via ActionButtonController");
                        return true;
                    }
                }
            }

            // Cách 2: Tìm button theo tên/path chứa "skill" + index
            if (TryClickSkillButton(index))
            {
                Plugin.Log.LogInfo($"[BossSkill] Clicked skill {index} via button search");
                return true;
            }

            // Cách 3: Gọi player.useSkill(index) qua reflection
            if (TryCallPlayerUseSkill(index))
            {
                Plugin.Log.LogInfo($"[BossSkill] Used skill {index} via player method");
                return true;
            }

            // Cách 4 (fallback yếu nhất): Giả lập nhấn phím số 1-4
            if (TryPressSkillKey(index))
            {
                Plugin.Log.LogInfo($"[BossSkill] Pressed key {index} for skill {index}");
                return true;
            }

            // Tất cả 4 cách đều thất bại — log để chẩn đoán (throttled 5s)
            if (Time.time - _lastSkillFailLogAt >= SkillFailLogInterval)
            {
                _lastSkillFailLogAt = Time.time;
                var player = GameAPI.GetMyPlayer();
                string playerType = player != null ? player.GetType().Name : "null";
                var ctrlDebug = Il2CppAPI.FindObjectsOfType(typeof(ActionButtonController));
                int ctrlCount = ctrlDebug?.Length ?? 0;
                Plugin.Log.LogWarning(
                    $"[BossSkill] UseSkillByIndex({index}) FAILED all 4 approaches. " +
                    $"playerType={playerType}, ActionButtonController count={ctrlCount}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[BossSkill] UseSkillByIndex({index}) exception: {ex.Message}");
        }

        return false;
    }

    /// <summary>Nhấn phím số tương ứng với skill index (1→Key1, 2→Key2, ...).</summary>
    private static bool TryPressSkillKey(int index)
    {
        if (!_skillKeyMap.TryGetValue(index, out KeyCode key)) return false;
        try
        {
            // Dùng Il2Cpp Input simulation nếu có, fallback sang SendKey
            // UnityEngine.Input không thể inject keystroke — dùng reflection gọi keyboard handler của game
            var player = GameAPI.GetMyPlayer();
            if (player != null)
            {
                var t = player.GetType();
                // Tìm method nhận KeyCode hoặc int tương ứng skill slot
                foreach (var methodName in new[] { "onKeyDown", "OnKeyDown", "handleKey", "HandleKey", "onSkillKey" })
                {
                    var m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m == null) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(KeyCode))
                    {
                        m.Invoke(player, new object[] { key });
                        return true;
                    }
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(int))
                    {
                        m.Invoke(player, new object[] { index });
                        return true;
                    }
                }
            }

            // Tìm InputManager / KeyboardManager singleton
            foreach (var typeName in new[] { "InputManager", "KeyboardManager", "HotkeyManager", "BattleInputHandler" })
            {
                var t = GameAPI.FindTypeByName(typeName);
                if (t == null) continue;

                // Thử gọi singleton.onKeyDown(KeyCode) hoặc .pressKey(int)
                var instance = GameAPI.GetSingleton(t);
                if (instance == null) continue;

                foreach (var methodName in new[] { "onKeyDown", "OnKeyDown", "pressKey", "PressKey", "triggerSkill" })
                {
                    var m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m == null) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(KeyCode))
                    {
                        m.Invoke(instance, new object[] { key });
                        return true;
                    }
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(int))
                    {
                        m.Invoke(instance, new object[] { index });
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[BossSkill] TryPressSkillKey({index}) fail: {ex.Message}");
        }
        return false;
    }

    private static float _lastKnownBossHp = -1f;

    /// <summary>Trả về HP boss lần gần nhất đọc được (dùng cho UI overlay).</summary>
    public static float GetLastKnownBossHp(object boss)
    {
        if (boss == null) return _lastKnownBossHp;
        float hp = GetBossHp(boss);
        if (hp >= 0) _lastKnownBossHp = hp;
        return _lastKnownBossHp;
    }

    // Cache cho UI HP text component
    private static object _cachedHpTextComponent = null;
    // maxHP ghi nhận lúc cache component — bar đổi sang entity khác thì maxHP đổi,
    // nhờ đó phát hiện và từ chối đọc nhầm HP của quái khác thành HP boss.
    private static long _cachedBossMaxHp = 0;
    private static float _lastUiHpSearchTime = -999f;
    private const float UiHpSearchCooldown = 2f;
    
    // Regex match dạng: "239.970/240.000" hoặc "47.125.741/61.566.000" hoặc "100/100"
    // Hỗ trợ cả dấu chấm và dấu phẩy phân cách hàng nghìn
    private static readonly Regex _hpPattern = new Regex(
        @"^[\s]*([0-9][0-9.,]*)\s*/\s*([0-9][0-9.,]*)[\s]*$", 
        RegexOptions.Compiled);

    private static float GetBossHp(object boss)
    {
        try
        {
            // Ưu tiên 1: đọc thẳng từ object boss (Character.getInfoInBar() —
            // cùng nguồn game dùng vẽ thanh HP). Không bao giờ nhầm sang HP
            // của player hay quái khác vì đọc đúng object mà BossDetector chọn.
            int apiHp = GameAPI.GetMobHp(boss);
            if (apiHp > 0) return apiHp;

            // Ưu tiên 2: quét UI text (fallback khi reflection không đọc được)
            float uiHp = ReadBossHpFromUI();
            if (uiHp >= 0) return uiHp;

            if (apiHp == 0)
            {
                if (!GameAPI.IsMobAlive(boss))
                    return 0f; // chết thật
            }
            return -1f;
        }
        catch { return -1f; }
    }

    /// <summary>
    /// Đọc HP boss từ UI text components.
    /// Tìm text active dạng "curHp/maxHp" rồi chọn ứng viên "giống boss" nhất:
    ///   +2 điểm nếu nằm trong info bar của target (path chứa "ObjectMapInfoBarManager" —
    ///      chính là thanh HP của entity đang nhắm),
    ///   +1 điểm nếu maxHP lớn (> 10 triệu).
    /// Chỉ nhận ứng viên >= 1 điểm để không bắt nhầm thanh HP của player
    /// (239.970/240.000) hay chữ linh tinh. Bản cũ CHỈ nhận maxHP > 10 triệu
    /// nên boss có HP <= 10 triệu không bao giờ đọc được ("ko nhận hp").
    /// Cache component tìm được để không scan lại mỗi frame.
    /// </summary>
    private static float ReadBossHpFromUI()
    {
        try
        {
            // Thử đọc từ cached component trước
            if (_cachedHpTextComponent != null)
            {
                float hp = TryParseHpFromComponent(_cachedHpTextComponent, out long cachedMax);
                if (hp >= 0 && (_cachedBossMaxHp <= 0 || cachedMax == _cachedBossMaxHp))
                    return hp;
                // Bar đã đổi sang entity khác (maxHP khác) hoặc không còn parse được
                _cachedHpTextComponent = null;
                _cachedBossMaxHp = 0;
            }

            // Throttle tìm kiếm UI mới
            if (Time.time - _lastUiHpSearchTime < UiHpSearchCooldown)
                return -1f;
            _lastUiHpSearchTime = Time.time;

            object bestComp = null;
            long bestCur = 0, bestMax = 0;
            int bestScore = 0;
            string bestPath = "";

            // Scan tất cả Text components
            var uiTexts = UnityEngine.Object.FindObjectsOfType<Text>();
            foreach (var text in uiTexts)
            {
                if (text == null || !text.gameObject.activeInHierarchy) continue;
                if (string.IsNullOrEmpty(text.text)) continue;

                if (text.text.Contains("/"))
                    Plugin.Log.LogInfo($"[BossSkill] Inspecting UI.Text with '/': '{text.text}'");

                ConsiderHpCandidate(text, text.text, text.transform,
                    ref bestComp, ref bestCur, ref bestMax, ref bestScore, ref bestPath);
            }

            // Scan TextMeshProUGUI
            var tmpTexts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>();
            foreach (var text in tmpTexts)
            {
                if (text == null || !text.gameObject.activeInHierarchy) continue;
                if (string.IsNullOrEmpty(text.text)) continue;

                if (text.text.Contains("/"))
                    Plugin.Log.LogInfo($"[BossSkill] Inspecting TMP with '/': '{text.text}'");

                ConsiderHpCandidate(text, text.text, text.transform,
                    ref bestComp, ref bestCur, ref bestMax, ref bestScore, ref bestPath);
            }

            if (bestComp == null) return -1f;

            _cachedHpTextComponent = bestComp;
            _cachedBossMaxHp = bestMax;
            Plugin.Log.LogInfo(
                $"[BossSkill] Found boss HP: '{bestCur:N0}/{bestMax:N0}' → hp={bestCur}, path={bestPath}");
            return (float)bestCur;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[BossSkill] ReadBossHpFromUI fail: {ex.Message}");
        }
        return -1f;
    }

    /// <summary>
    /// Xét 1 ứng viên text "cur/max": ưu tiên thanh HP của MỤC TIÊU đang nhắm
    /// (path chứa "ObjectMapInfoBarManager"), rồi tới maxHP lớn. Ứng viên 0 điểm
    /// bị loại. Cùng điểm thì lấy bar có maxHP lớn hơn.
    /// KHÔNG match "InfoBar" chung chung: thanh HP của chính player cũng nằm
    /// trong node tên "infoBar.xxx" → bản cũ đọc nhầm HP player thành HP boss.
    /// </summary>
    private static void ConsiderHpCandidate(object comp, string raw, Transform tr,
        ref object bestComp, ref long bestCur, ref long bestMax, ref int bestScore, ref string bestPath)
    {
        if (!TryParseHpText(raw, out long cur, out long max)) return;

        string path = GetTransformPathSafe(tr);
        int score = 0;
        if (path.IndexOf("ObjectMapInfoBarManager", StringComparison.OrdinalIgnoreCase) >= 0) score += 2;
        if (max > 10000000) score += 1;
        if (score < 1) return; // không có dấu hiệu "giống boss" → bỏ qua

        if (bestComp == null || score > bestScore || (score == bestScore && max > bestMax))
        {
            bestComp = comp; bestCur = cur; bestMax = max; bestScore = score; bestPath = path;
        }
    }

    private static float TryParseHpFromComponent(object comp, out long maxHp)
    {
        maxHp = 0;
        try
        {
            if (comp is Text uiText)
            {
                if (uiText == null || !uiText.gameObject.activeInHierarchy) return -1f;
                if (!TryParseHpText(uiText.text, out long cur, out long max)) return -1f;
                maxHp = max;
                return (float)cur;
            }
            if (comp is TextMeshProUGUI tmpText)
            {
                if (tmpText == null || !tmpText.gameObject.activeInHierarchy) return -1f;
                if (!TryParseHpText(tmpText.text, out long cur, out long max)) return -1f;
                maxHp = max;
                return (float)cur;
            }
        }
        catch { }
        return -1f;
    }

    /// <summary>
    /// Parse text dạng "239.970/240.000" → trả về curHp và maxHp.
    /// Chỉ nhận cặp hợp lệ (max >= 100, cur >= 0, cur <= max*2) để tránh bắt nhầm "1/4" hay "X1".
    /// KHÔNG lọc theo độ lớn HP ở đây — việc chọn "có giống boss không" do nơi gọi
    /// quyết định dựa trên path của component, để boss nhỏ (HP <= 10 triệu) vẫn đọc được.
    /// </summary>
    private static bool TryParseHpText(string text, out long cur, out long max)
    {
        cur = 0; max = 0;
        if (string.IsNullOrEmpty(text)) return false;

        // Loại bỏ các thẻ HTML/Rich text như <color=#ff0000>, <b>, <i>...
        string cleanText = Regex.Replace(text, "<.*?>", string.Empty).Trim();

        var match = _hpPattern.Match(cleanText);
        if (!match.Success) return false;

        string curStr = match.Groups[1].Value.Replace(".", "").Replace(",", "");
        string maxStr = match.Groups[2].Value.Replace(".", "").Replace(",", "");

        if (!long.TryParse(curStr, out cur) || !long.TryParse(maxStr, out max))
            return false;

        if (max < 100 || cur < 0 || cur > max * 2) return false; // sanity check
        return true;
    }

    private static string GetTransformPathSafe(Transform t)
    {
        try
        {
            var parts = new List<string>();
            int guard = 0;
            while (t != null && guard++ < 8)
            {
                parts.Add(t.name ?? "?");
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
        catch { return "?"; }
    }

    private static bool TryClickSkillFromController(object controller, int targetIndex)
    {
        if (controller == null) return false;

        try
        {
            // Thử lấy skillButtons array
            var arrObj = ReflectionHelper.InvokeNoArg(controller, "get_skillButtons");
            if (arrObj is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item == null) continue;

                    if (ReflectionHelper.TryGetIntMember(item, out int idx, "get_index", "index") && idx == targetIndex)
                    {
                        if (TryClickSkillButton(item))
                            return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    private static bool TryClickSkillButton(object skillButton)
    {
        if (skillButton == null) return false;

        try
        {
            var view = ReflectionHelper.InvokeNoArg(skillButton, "get_view") ?? ReflectionHelper.InvokeNoArg(skillButton, "getView");
            if (view == null) return false;

            var buttonObj = ReflectionHelper.GetMemberValue(view, "button");
            if (buttonObj is UnityEngine.UI.Button button)
            {
                var go = button.gameObject;
                if (!go.activeInHierarchy || !button.interactable) return false;

                button.onClick.Invoke();
                return true;
            }
        }
        catch { }

        return false;
    }

    private static bool TryClickSkillButton(int index)
    {
        try
        {
            var buttons = Il2CppAPI.FindObjectsOfType(typeof(Button));
            if (buttons == null || buttons.Length == 0)
                buttons = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));

            if (buttons == null || buttons.Length == 0) return false;

            foreach (var obj in buttons)
            {
                var btn = obj as Button;
                if (btn == null) continue;

                string name = btn.gameObject?.name ?? "";
                string path = UIHelper.GetTransformPath(btn.transform);
                string text = UIHelper.GetButtonText(btn);

                // Match pattern: "Skill1", "skill_1", "SkillButton_1", etc.
                string lowerName = name.ToLowerInvariant();
                string lowerPath = path.ToLowerInvariant();
                string lowerText = text.ToLowerInvariant();

                bool match = false;
                if (lowerName.Contains($"skill") && lowerName.Contains(index.ToString())) match = true;
                if (lowerPath.Contains($"skill") && lowerPath.Contains(index.ToString())) match = true;
                if (lowerText == index.ToString() && (lowerPath.Contains("skill") || lowerName.Contains("skill"))) match = true;

                if (match && btn.gameObject != null && btn.gameObject.activeInHierarchy && btn.interactable)
                {
                    btn.onClick.Invoke();
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static bool TryCallPlayerUseSkill(int index)
    {
        try
        {
            var player = GameAPI.GetMyPlayer();
            if (player == null) return false;

            var t = player.GetType();

            // Try methods: useSkill(int), castSkill(int), doSkill(int), etc.
            foreach (var methodName in new[] { "useSkill", "UseSkill", "castSkill", "CastSkill",
                                                "doSkill", "DoSkill", "performSkill", "PerformSkill" })
            {
                var m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) continue;

                var parameters = m.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                {
                    m.Invoke(player, new object[] { index });
                    return true;
                }
            }
        }
        catch { }

        return false;
    }











    /// <summary>Dump tất cả methods + fields của boss object để tìm HP.</summary>
    private static void DumpBossType(object boss)
    {
        try
        {
            if (boss == null) return;
            var t = boss.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Plugin.Log.LogWarning($"[BossSkill] === DUMP TYPE {t.Name} ===");

            // *** GỌI THỬ TẤT CẢ NO-ARG METHODS TRẢ VỀ INT/LONG/FLOAT → TÌM HP ***
            var methods = t.GetMethods(F);
            var numericMethods = new System.Collections.Generic.List<string>();
            foreach (var m in methods)
            {
                if (m.GetParameters().Length == 0)
                {
                    var rt = m.ReturnType;
                    if (rt == typeof(int) || rt == typeof(long) || rt == typeof(float) || rt == typeof(double))
                    {
                        try
                        {
                            var val = m.Invoke(boss, null);
                            numericMethods.Add($"{m.Name}()={val}({rt.Name})");
                        }
                        catch { numericMethods.Add($"{m.Name}()=ERROR({rt.Name})"); }
                    }
                }
            }
            Plugin.Log.LogWarning($"[BossSkill] Numeric methods (all): {string.Join(", ", numericMethods)}");

            // Fields kiểu số
            var fields = t.GetFields(F);
            var numericFields = new System.Collections.Generic.List<string>();
            foreach (var f in fields)
            {
                if (f.FieldType == typeof(int) || f.FieldType == typeof(float) ||
                    f.FieldType == typeof(long) || f.FieldType == typeof(double))
                {
                    try
                    {
                        var val = f.GetValue(boss);
                        numericFields.Add($"{f.Name}={val}({f.FieldType.Name})");
                    }
                    catch { numericFields.Add($"{f.Name}=??({f.FieldType.Name})"); }
                }
            }
            Plugin.Log.LogWarning($"[BossSkill] Numeric fields (all): {string.Join(", ", numericFields)}");

            // *** DUMP MOBINFO (getSelfInfo) ***
            try
            {
                var info = ReflectionHelper.InvokeNoArg(boss, "getSelfInfo");
                if (info != null)
                {
                    var infoType = info.GetType();
                    Plugin.Log.LogWarning($"[BossSkill] === DUMP MobInfo {infoType.Name} ===");

                    var infoMethods = infoType.GetMethods(F);
                    var infoNumericMethods = new System.Collections.Generic.List<string>();
                    foreach (var m in infoMethods)
                    {
                        if (m.GetParameters().Length == 0)
                        {
                            var rt = m.ReturnType;
                            if (rt == typeof(int) || rt == typeof(long) || rt == typeof(float) || rt == typeof(double))
                            {
                                try
                                {
                                    var val = m.Invoke(info, null);
                                    infoNumericMethods.Add($"{m.Name}()={val}({rt.Name})");
                                }
                                catch { infoNumericMethods.Add($"{m.Name}()=ERROR({rt.Name})"); }
                            }
                        }
                    }
                    Plugin.Log.LogWarning($"[BossSkill] MobInfo numeric methods (all): {string.Join(", ", infoNumericMethods)}");
                }
            }
            catch (Exception ex2)
            {
                Plugin.Log.LogWarning($"[BossSkill] getSelfInfo dump fail: {ex2.Message}");
            }

            Plugin.Log.LogWarning($"[BossSkill] === END DUMP ===");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[BossSkill] DumpBossType fail: {ex.Message}");
        }
    }
}
