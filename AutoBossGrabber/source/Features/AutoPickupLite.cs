using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace AutoBossGrabber;

/// <summary>
/// Nhặt đồ rơi sau khi đánh boss.
/// TapAttackKey() ưu tiên bấm nút AutoAttack (Q), fallback sang attack method / battle skill button.
/// PickupNearest() có rate-limit riêng 0.2s để không di chuyển liên tục.
/// </summary>
public static class AutoPickupLite
{
    private static float _lastPickupMove = 0f;
    private static float _lastAutoAttackLogAt = -999f;
    private static float _lastAutoAttackPressAt = -999f;
    private static MethodInfo _miItemPickup = null;

    // Anti-spam cho phím P (hợp thể) — không tap liên tục mỗi frame
    private static float _lastFusionTapAt = -999f;
    private const float FusionTapCooldown = 1.5f;

    /// <summary>
    /// Tap phím P (hợp thể) 1 lần.
    /// Gọi trước khi teleport đi săn boss và trước khi bật Q farm về.
    /// </summary>
    public static void TapFusionKey()
    {
        if (Time.time - _lastFusionTapAt < FusionTapCooldown) return;
        _lastFusionTapAt = Time.time;

        // Ưu tiên 1: tìm button hợp thể trên HUD (không phụ thuộc focus cửa sổ)
        if (TryClickFusionButton())
        {
            Plugin.Log.LogInfo("[AutoPickup] TapFusionKey: clicked fusion button (P)");
            return;
        }

        // Ưu tiên 2: gọi method fusion của player qua reflection
        if (TryCallFusionMethod())
        {
            Plugin.Log.LogInfo("[AutoPickup] TapFusionKey: called fusion method");
            return;
        }

        Plugin.Log.LogWarning("[AutoPickup] TapFusionKey: no fusion button/method found");
    }

    private static bool TryClickFusionButton()
    {
        try
        {
            var buttons = Il2CppAPI.FindObjectsOfType(typeof(Button));
            if (buttons == null || buttons.Length == 0)
                buttons = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));
            if (buttons == null) return false;

            foreach (var obj in buttons)
            {
                var btn = obj as Button;
                if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) continue;

                string name = btn.gameObject?.name ?? "";
                string path = UIHelper.GetTransformPath(btn.transform);
                string text = UIHelper.GetButtonText(btn);
                string shortcut = UIHelper.GetShortcutText(btn);

                string nameLower = name.ToLowerInvariant();
                string pathLower = path.ToLowerInvariant();
                string textLower = text.ToLowerInvariant();

                // Khớp theo shortcut 'P', hoặc tên/path chứa fusion/hop the
                bool match =
                    string.Equals(shortcut, "P", StringComparison.OrdinalIgnoreCase) ||
                    nameLower.Contains("fusion") || nameLower.Contains("hopthe") || nameLower.Contains("hop_the") ||
                    pathLower.Contains("fusion") || pathLower.Contains("hopthe") ||
                    textLower.Contains("hợp thể") || textLower.Contains("hop the") || textLower.Contains("fusion");

                if (match)
                {
                    btn.onClick.Invoke();
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AutoPickup] TryClickFusionButton fail: {ex.Message}");
        }
        return false;
    }

    private static bool TryCallFusionMethod()
    {
        try
        {
            var player = GameAPI.GetMyPlayer();
            if (player == null) return false;
            var t = player.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in new[] { "fusion", "Fusion", "hopThe", "HopThe", "onFusion", "OnFusion",
                                         "toggleFusion", "ToggleFusion", "useFusion", "UseFusion" })
            {
                var m = t.GetMethod(name, F, null, Type.EmptyTypes, null);
                if (m != null)
                {
                    m.Invoke(player, null);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AutoPickup] TryCallFusionMethod fail: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Bấm nút auto attack Q trên HUD, nếu không có thì gọi attack method / skill fallback.
    /// Throttle (0.4s) do AutoBossRunner quản lý.
    /// </summary>
    public static void TapAttackKey()
    {
        if (TryClickAutoAttackButton()) return;
        if (TryPressKey(KeyCode.Q)) return;
        if (TryCallAttack()) return;
        TryClickBattleSkillButton();
    }

    private static bool TryPressKey(KeyCode key)
    {
        try
        {
            var player = GameAPI.GetMyPlayer();
            if (player != null)
            {
                var t = player.GetType();
                foreach (var methodName in new[] { "onKeyDown", "OnKeyDown", "handleKey", "HandleKey" })
                {
                    var m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m == null) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(KeyCode))
                    {
                        m.Invoke(player, new object[] { key });
                        Plugin.Log.LogInfo($"[AutoPickupLite] Pressed key {key} via player method {methodName}");
                        return true;
                    }
                }
            }

            foreach (var typeName in new[] { "InputManager", "KeyboardManager", "HotkeyManager", "BattleInputHandler" })
            {
                var t = GameAPI.FindTypeByName(typeName);
                if (t == null) continue;

                var instance = GameAPI.GetSingleton(t);
                if (instance == null) continue;

                foreach (var methodName in new[] { "onKeyDown", "OnKeyDown", "pressKey", "PressKey" })
                {
                    var m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m == null) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(KeyCode))
                    {
                        m.Invoke(instance, new object[] { key });
                        Plugin.Log.LogInfo($"[AutoPickupLite] Pressed key {key} via {typeName}.{methodName}");
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AutoPickupLite] TryPressKey({key}) fail: {ex.Message}");
        }
        return false;
    }

    // ===================================================================
    // Đọc trạng thái auto attack (Q) THẬT từ game để bật/tắt không toggle mù
    // ===================================================================
    private static object _autoAttackStateOwner;
    private static MemberInfo _autoAttackStateMember;

    /// <summary>
    /// Bật/tắt auto attack Q theo trạng thái THẬT của game: chỉ bấm khi trạng thái
    /// hiện tại khác mong muốn. Sửa lỗi toggle mù: Q đang ON mà bấm thêm lần nữa
    /// thì thành OFF → nhân vật đứng yên cạnh boss tưởng đang đánh.
    /// Khi KHÔNG đọc được trạng thái: chỉ bấm 1 lần mỗi khi ĐỔI mong muốn
    /// (+ heartbeat 4s), tuyệt đối không bấm mỗi tick — bấm lặp liên tục sẽ
    /// toggle Q ON/OFF không ngừng → nhân vật đấm loạn xạ vào quái gần nhất.
    /// </summary>
    private static bool? _lastDesired;

    public static void SetAutoAttack(bool desired)
    {
        if (Time.time - _lastAutoAttackPressAt < 0.5f) return; // chờ game cập nhật trạng thái sau lần bấm trước
        var cur = TryReadAutoAttackOn();
        if (cur.HasValue)
        {
            if (cur.Value == desired) { _lastDesired = desired; return; } // đã đúng ý muốn → không bấm
        }
        else if (_lastDesired == desired)
        {
            return; // KHÔNG đọc được state: chỉ bấm khi ĐỔI ý định, tuyệt đối không bấm lặp
        }

        TapAttackKey();
        _lastAutoAttackPressAt = Time.time;
        _lastDesired = desired;
        if (Time.time - _lastAutoAttackLogAt >= 2f)
        {
            _lastAutoAttackLogAt = Time.time;
            Plugin.Log.LogInfo($"[AutoPickupLite] SetAutoAttack({desired}) cur={(cur.HasValue ? cur.Value.ToString() : "?")} -> pressed");
        }
    }

    /// <summary>Đọc isAutoAttackOn từ AutoAttackBlackBoardComponent / AutoAttackPanel / AutoAttackButton
    /// (fallback: quét mọi type chứa "AutoAttack" có member bool isAutoAttackOn). Null nếu không đọc được.</summary>
    private static Type[] _scannedAutoAttackTypes;
    private static bool _readerLockLogged;
    private static float _lastReaderFailLogAt = -999f;

    public static bool? TryReadAutoAttackOn()
    {
        try
        {
            if (_autoAttackStateOwner != null && _autoAttackStateMember != null)
            {
                var cached = ReadBool(_autoAttackStateOwner, _autoAttackStateMember);
                if (cached.HasValue) return cached;
                _autoAttackStateOwner = null;
                _autoAttackStateMember = null;
            }

            foreach (var typeName in new[] { "AutoAttackBlackBoardComponent", "AutoAttackPanel", "AutoAttackButton" })
            {
                var t = GameAPI.FindTypeByName(typeName);
                if (t == null) continue;
                var v = TryReadFromType(t);
                if (v.HasValue) return v;
            }

            // Fallback: quét Assembly-CSharp tìm type chứa "AutoAttack" có bool isAutoAttackOn (cache 1 lần)
            if (_scannedAutoAttackTypes == null)
            {
                var list = new System.Collections.Generic.List<Type>();
                try
                {
                    var asm = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                    if (asm != null)
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t == null) continue;
                            if (FindBoolMember(t, "isAutoAttackOn") != null) list.Add(t);
                        }
                    }
                }
                catch { }
                _scannedAutoAttackTypes = list.ToArray();
                Plugin.Log.LogInfo($"[AutoPickupLite] Q-reader scan: {list.Count} type(s) có isAutoAttackOn: {(list.Count > 0 ? string.Join(", ", list.Select(t => t.Name)) : "(không có)")}");
            }

            foreach (var t in _scannedAutoAttackTypes)
            {
                var v = TryReadFromType(t);
                if (v.HasValue) return v;
            }

            if (Time.time - _lastReaderFailLogAt >= 15f)
            {
                _lastReaderFailLogAt = Time.time;
                Plugin.Log.LogWarning("[AutoPickupLite] TryReadAutoAttackOn: KHÔNG đọc được trạng thái Q (không tìm thấy owner/instance nào)");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AutoPickupLite] TryReadAutoAttackOn fail: {ex.Message}");
        }
        return null;
    }

    /// <summary>Thử đọc isAutoAttackOn từ singleton / mọi instance của type. Lock owner đọc được lần đầu.</summary>
    private static bool? TryReadFromType(Type t)
    {
        var owners = new System.Collections.Generic.List<object>();
        var single = GameAPI.GetSingleton(t);
        if (single != null) owners.Add(single);
        if (typeof(UnityEngine.Object).IsAssignableFrom(t))
        {
            var objs = Il2CppAPI.FindObjectsOfType(t);
            if (objs != null)
                foreach (var o in objs)
                    if (o != null) owners.Add(o);
        }

        foreach (var owner in owners)
        {
            var member = FindBoolMember(owner.GetType(), "isAutoAttackOn");
            if (member == null) continue;
            var v = ReadBool(owner, member);
            if (v.HasValue)
            {
                _autoAttackStateOwner = owner;
                _autoAttackStateMember = member;
                if (!_readerLockLogged)
                {
                    _readerLockLogged = true;
                    Plugin.Log.LogInfo($"[AutoPickupLite] Q reader locked: owner={owner.GetType().Name} member={member.Name}");
                }
                return v;
            }
        }
        return null;
    }

    private static MemberInfo FindBoolMember(Type t, string name)
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var p = t.GetProperty(name, F);
        if (p != null && p.PropertyType == typeof(bool)) return p;
        var f = t.GetField(name, F);
        if (f != null && f.FieldType == typeof(bool)) return f;
        // IL2CPP interop: property thường chỉ sinh ra getter method "get_isAutoAttackOn"
        var m = t.GetMethod("get_" + name, F, null, Type.EmptyTypes, null);
        if (m != null && m.ReturnType == typeof(bool)) return m;
        return null;
    }

    private static bool? ReadBool(object owner, MemberInfo member)
    {
        try
        {
            if (owner == null) return null;
            object v = member is PropertyInfo pi ? pi.GetValue(owner)
                     : member is MethodInfo mi ? mi.Invoke(owner, null)
                     : ((FieldInfo)member).GetValue(owner);
            if (v is bool b) return b;
        }
        catch { }
        return null;
    }

    private static bool TryClickAutoAttackButton()
    {
        try
        {
            var buttons = Il2CppAPI.FindObjectsOfType(typeof(Button));
            if (buttons == null || buttons.Length == 0)
                buttons = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));

            if (buttons == null || buttons.Length == 0)
                return false;

            Button best = null;
            int bestScore = int.MinValue;
            string bestText = "";
            string bestPath = "";

            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var obj in buttons)
            {
                var btn = obj as Button;
                if (btn == null) continue;

                int id = 0;
                try { id = btn.GetInstanceID(); } catch { }
                if (id != 0 && !seen.Add(id)) continue;

                string text = UIHelper.GetButtonText(btn);
                string name = btn.gameObject?.name ?? "";
                string path = UIHelper.GetTransformPath(btn.transform);
                int score = ScoreAutoAttackCandidate(text, name, path, btn);
                if (score <= int.MinValue / 2) continue;
                if (best == null || score > bestScore)
                {
                    best = btn;
                    bestScore = score;
                    bestText = text;
                    bestPath = path;
                }
            }

            if (best != null)
            {
                var go = best.gameObject;
                var ped = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(go, ped, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(go, ped, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(go, ped, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);

                if (Time.time - _lastAutoAttackLogAt >= 2f)
                {
                    _lastAutoAttackLogAt = Time.time;
                    Plugin.Log.LogInfo($"[AutoPickupLite] Clicked auto attack button text='{bestText}' path='{bestPath}'");
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AutoPickupLite] TryClickAutoAttackButton fail: {ex.Message}");
        }

        return false;
    }

    private static bool TryCallAttack()
    {
        try
        {
            var player = GameAPI.GetMyPlayer();
            if (player == null) return false;

            var t = player.GetType();
            foreach (var n in new[] { "attack", "Attack", "doAttack", "OnAttack", "cmdAttack", "sendAttack" })
            {
                var m = t.GetMethod(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null && m.GetParameters().Length == 0)
                {
                    m.Invoke(player, null);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AutoPickupLite] TryCallAttack fail: {ex.Message}");
        }

        return false;
    }

    private static void TryClickBattleSkillButton()
    {
        try
        {
            var controllers = Il2CppAPI.FindObjectsOfType(typeof(ActionButtonController));
            if (controllers == null || controllers.Length == 0)
                controllers = Il2CppAPI.FindObjectsOfTypeAll(typeof(ActionButtonController));

            if (controllers == null || controllers.Length == 0)
                return;

            foreach (var ctrlObj in controllers)
            {
                if (ctrlObj == null) continue;
                if (TryClickSkillButtonFromController(ctrlObj, out int index))
                {
                    if (Time.time - _lastAutoAttackLogAt >= 2f)
                    {
                        _lastAutoAttackLogAt = Time.time;
                        Plugin.Log.LogInfo($"[AutoPickupLite] Clicked fallback battle skill button index={index}");
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AutoPickupLite] TryClickBattleSkillButton fail: {ex.Message}");
        }
    }

    private static bool TryClickSkillButtonFromController(object controller, out int clickedIndex)
    {
        clickedIndex = -1;
        if (controller == null) return false;

        try
        {
            var current = ReflectionHelper.InvokeNoArg(controller, "get_NKCOBJKGOKN");
            if (TryClickSkillButton(current, out clickedIndex))
                return true;

            var arrObj = ReflectionHelper.InvokeNoArg(controller, "get_skillButtons");
            if (arrObj is System.Collections.IEnumerable enumerable)
            {
                object first = null;
                foreach (var item in enumerable)
                {
                    if (item == null) continue;
                    if (first == null) first = item;

                    if (ReflectionHelper.TryGetIntMember(item, out int idx, "get_index", "index") && idx == 0)
                    {
                        if (TryClickSkillButton(item, out clickedIndex))
                            return true;
                    }
                }

                if (first != null && TryClickSkillButton(first, out clickedIndex))
                    return true;
            }
        }
        catch { }

        return false;
    }

    private static bool TryClickSkillButton(object skillButton, out int clickedIndex)
    {
        clickedIndex = -1;
        if (skillButton == null) return false;

        try
        {
            ReflectionHelper.TryGetIntMember(skillButton, out clickedIndex, "get_index", "index");

            var view = ReflectionHelper.InvokeNoArg(skillButton, "get_view") ?? ReflectionHelper.InvokeNoArg(skillButton, "getView");
            if (view == null) return false;

            var buttonObj = ReflectionHelper.GetMemberValue(view, "button");
            if (buttonObj is UnityEngine.UI.Button button)
            {
                button.onClick.Invoke();
                return true;
            }
        }
        catch { }

        return false;
    }

    private static int ScoreAutoAttackCandidate(string text, string name, string path, Button btn)
    {
        string lowerText = (text ?? "").Trim().ToLowerInvariant();
        string lowerName = (name ?? "").Trim().ToLowerInvariant();
        string lowerPath = (path ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(lowerText) && string.IsNullOrEmpty(lowerName) && string.IsNullOrEmpty(lowerPath))
            return int.MinValue / 2;

        int score = 0;
        if (lowerText == "q") score += 2000;
        else if (lowerText.Contains("q")) score += 300;

        if (lowerName.Contains("autoattack")) score += 1000;
        if (lowerPath.Contains("autoattack")) score += 800;
        if (lowerPath.Contains("skilllayout")) score += 200;
        if (btn != null && btn.gameObject != null && btn.gameObject.activeInHierarchy) score += 100;
        if (btn != null && btn.interactable) score += 20;

        return score;
    }













    /// <summary>Tìm item gần player nhất, gọi pickup() hoặc di chuyển tới. Rate-limit 0.2s.</summary>
    public static bool PickupNearest(float maxRadius)
    {
        try
        {
            if (Time.time - _lastPickupMove < 0.2f) return false;
            _lastPickupMove = Time.time;

            var items = GameAPI.FindItemsOnMap();
            if (items.Count == 0) return false;

            var myPos = GameAPI.GetPlayerPosition();
            object nearest = null;
            float nearestDist = maxRadius;
            foreach (var item in items)
            {
                if (item == null) continue;
                var pos = GameAPI.GetItemPosition(item);
                float d = Vector2.Distance(myPos, pos);
                if (d < nearestDist)
                {
                    nearest = item;
                    nearestDist = d;
                }
            }

            if (nearest == null) return false;

            // 1) Gọi item.pickup() qua reflection nếu có
            if (TryCallItemPickup(nearest)) return true;

            // 2) Fallback: di chuyển tới
            var np = GameAPI.GetItemPosition(nearest);
            GameAPI.MoveTo(np.x, np.y);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AutoPickupLite] PickupNearest fail: {ex.Message}");
            return false;
        }
    }

    private static bool TryCallItemPickup(object item)
    {
        try
        {
            if (item == null) return false;
            if (_miItemPickup == null)
            {
                var t = item.GetType();
                // "interact" là method nhặt đồ thật trong ItemMapObject (không có pickup/Collect).
                // Fallback sang các alias cũ để chịu được update game.
                foreach (var n in new[] { "interact", "pickup", "Pickup", "pickUp", "PickUp", "onPickup", "doPickup", "Collect" })
                {
                    var m = t.GetMethod(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null && m.GetParameters().Length == 0) { _miItemPickup = m; break; }
                }
            }
            if (_miItemPickup != null)
            {
                _miItemPickup.Invoke(item, null);
                return true;
            }
        }
        catch { }
        return false;
    }
}
