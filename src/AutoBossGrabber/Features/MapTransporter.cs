using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace AutoBossGrabber;

/// <summary>
/// Mở menu Dịch chuyển nhanh (CapsulePanel) → tìm/teleport theo tên map.
/// Dùng Resources.FindObjectsOfTypeAll để bắt cả panel đang ẩn.
/// Fallback scan Button UI nếu không có method phù hợp.
/// </summary>
public static class MapTransporter
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const byte VK_O = 0x4F;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // Shortcut của ô capsule trên HUD (SafetyUI/SlotItem/Layout/SlotUseItemButtonN).
    private const string CapsuleSlotShortcut = "O";

    // Chỉ dump danh sách ô dùng nhanh 1 lần khi không tìm được nút capsule.
    private static bool _dumpedSlotButtons = false;

    // Anti-spam: tránh bấm O liên tục mỗi frame
    private static float _lastOpenTryTime = -999f;
    private static float _lastCloseTryTime = -999f;
    private const float OpenRetryInterval = 2.5f; // tối đa thử mở menu 1 lần/2.5s
    private const float CloseRetryInterval = 1.5f; // chống spam đóng

    // Đánh dấu "đã click teleport rồi, đang chờ game xử lý"
    // Tránh bấm O lần nữa cho tới khi scene load xong
    private static bool _teleportClickedThisSession = false;
    private static float _teleportClickedAt = 0f;

    // Throttle cảnh báo "CapsulePanel not visible" — trước đây log mỗi frame (~80 dòng/giây)
    // khi đang ở boss map không có capsule menu.
    private static float _lastNotVisibleWarnAt = -999f;
    private const float NotVisibleWarnInterval = 3f;

    public static bool EnsureTeleportPanelVisible()
    {
        try
        {
            // === KHÓA CHỐNG SPAM ===
            // Nếu đã click teleport gần đây, KHÔNG bấm O nữa, KHÔNG mở lại menu.
            // Game đang load scene, panel biến mất là BÌNH THƯỜNG.
            // Việc bấm O lúc này sẽ mở lại menu ở scene cũ ngay trước khi load xong
            // → kẹt menu vĩnh viễn (Bug kẹt Sa Mạc).
            if (_teleportClickedThisSession)
            {
                if (Time.time - _teleportClickedAt < 6f)
                {
                    // Đang chờ teleport hoàn tất → bỏ qua hoàn toàn
                    return false;
                }
                // Quá 6s mà chưa load xong → reset, cho phép thử lại
                _teleportClickedThisSession = false;
            }

            var panel = GameAPI.FindCapsulePanel();
            if (panel == null || !((Component)panel).gameObject.activeInHierarchy)
            {
                // Chỉ thử mở mỗi 2.5s, không spam mỗi frame
                if (Time.time - _lastOpenTryTime < OpenRetryInterval) return false;
                _lastOpenTryTime = Time.time;

                // 1) Ưu tiên click thẳng ô capsule trên HUD.
                //    CẢNH BÁO - bug đã từng xảy ra: chỉ dùng keybd_event nên khi cửa sổ
                //    console BepInEx (hoặc app khác) đang focus thì phím O bay sang đó,
                //    game không nhận -> CapsulePanel không bao giờ active -> lặp vô hạn
                //    "CapsulePanel not visible" tới lúc teleport timeout.
                if (TryClickCapsuleSlotButton())
                    return false;   // đã click, chờ panel render ở nhịp sau

                // 2) Fallback keybd_event — chỉ có tác dụng khi game đang foreground.
                if (!IsGameForeground())
                {
                    Plugin.Log.LogWarning("[MapTransporter] CapsulePanel not visible, capsule slot button not found, and game window is NOT focused -> key 'O' would go to another window. Click vào cửa sổ game.");
                    return false;
                }

                Plugin.Log.LogWarning("[MapTransporter] CapsulePanel not visible. Simulating 'O' (max 1x/2.5s)...");
                keybd_event(VK_O, 0, 0, 0); // Key down
                keybd_event(VK_O, 0, KEYEVENTF_KEYUP, 0); // Key up

                return false;
            }

            var t = panel.GetType();
            foreach (var name in new[] { "Show", "show", "Open", "open", "EnsureVisible", "EnsureCapsulePanelVisible", "Refresh", "RefreshList" })
            {
                var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null && m.GetParameters().Length == 0)
                {
                    try { m.Invoke(panel, null); } catch { }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[MapTransporter] EnsureTeleportPanelVisible fail: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Cửa sổ game có đang là foreground window không.
    /// keybd_event gửi phím ở tầng OS nên chỉ tới được process đang focus;
    /// nếu user đang xem console BepInEx thì phím O vô tác dụng với game.
    /// </summary>
    private static bool IsGameForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        }
        catch { return true; }   // không xác định được thì cứ thử bấm
    }

    /// <summary>
    /// Click ô dùng nhanh chứa capsule dịch chuyển trên HUD.
    /// Từ dump ui_text_inspector: SafetyUI/SlotItem/Layout/SlotUseItemButton3 có
    /// ShortcutObject/ShortcutText = 'O' → chính là ô mở bản đồ dịch chuyển.
    /// onClick.Invoke() không phụ thuộc cửa sổ nào đang focus.
    /// </summary>
    private static bool TryClickCapsuleSlotButton()
    {
        try
        {
            var btn = FindCapsuleSlotButton();
            if (btn == null)
            {
                if (!_dumpedSlotButtons)
                {
                    _dumpedSlotButtons = true;
                    DumpSlotButtonsToLog();
                }
                return false;
            }

            Plugin.Log.LogInfo($"[MapTransporter] Clicking capsule slot button '{btn.gameObject.name}' (shortcut '{CapsuleSlotShortcut}')");
            btn.onClick.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[MapTransporter] TryClickCapsuleSlotButton fail: {ex.Message}");
            return false;
        }
    }

    private static Button FindCapsuleSlotButton()
    {
        var objs = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));
        if (objs == null) return null;

        Button fallback = null;
        foreach (var obj in objs)
        {
            var btn = obj as Button;
            if (btn == null) continue;

            string name = btn.gameObject?.name ?? "";
            if (name.IndexOf("SlotUseItemButton", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!string.Equals(UIHelper.GetShortcutText(btn), CapsuleSlotShortcut, StringComparison.OrdinalIgnoreCase)) continue;

            // Ô đang hiện trên HUD mới click được; giữ ô ẩn làm phương án cuối.
            if (btn.gameObject.activeInHierarchy && btn.interactable) return btn;
            fallback ??= btn;
        }

        return fallback;
    }

    /// <summary>Đọc text trong ShortcutObject/ShortcutText của một ô dùng nhanh.</summary>


    private static void DumpSlotButtonsToLog()
    {
        try
        {
            Plugin.Log.LogWarning("[MapTransporter] Capsule slot button not found. Dumping all SlotUseItemButton*:");
            var objs = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));
            if (objs == null) return;

            foreach (var obj in objs)
            {
                var btn = obj as Button;
                if (btn == null) continue;
                string name = btn.gameObject?.name ?? "";
                if (name.IndexOf("Slot", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Plugin.Log.LogWarning($"  go='{name}' shortcut='{UIHelper.GetShortcutText(btn)}' text='{UIHelper.GetButtonText(btn)}' active={btn.gameObject.activeInHierarchy}");
            }
        }
        catch { }
    }

    public static void CloseTeleportPanel()
    {
        // Chống spam đóng
        if (Time.time - _lastCloseTryTime < CloseRetryInterval) return;

        var panel = GameAPI.FindCapsulePanel();
        if (panel != null && ((Component)panel).gameObject.activeInHierarchy)
        {
            _lastCloseTryTime = Time.time;

            // Ưu tiên nút Close trong panel: không phụ thuộc focus như keybd_event.
            if (TryClickCloseButton(panel as Component))
                return;

            if (!IsGameForeground()) return;

            Plugin.Log.LogInfo("[MapTransporter] Closing CapsulePanel by pressing 'O'...");
            keybd_event(VK_O, 0, 0, 0); // Key down
            keybd_event(VK_O, 0, KEYEVENTF_KEYUP, 0); // Key up
        }
    }

    private static bool TryClickCloseButton(Component root)
    {
        try
        {
            if (root == null) return false;
            foreach (var btn in root.GetComponentsInChildren<Button>(true))
            {
                if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                string name = btn.gameObject.name ?? "";
                if (name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) < 0) continue;

                Plugin.Log.LogInfo($"[MapTransporter] Closing CapsulePanel via button '{name}'");
                btn.onClick.Invoke();
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Reset trạng thái teleport - gọi khi đã xác nhận vào map mới hoặc khi state machine reset.
    /// </summary>
    public static void ResetTeleportSession()
    {
        _teleportClickedThisSession = false;
        _lastOpenTryTime = -999f;
        _dumpedSlotButtons = false;
        _dumpedCapsuleButtons = false;
    }

    /// <summary>Teleport tới map có tên chứa mapName (case-insensitive).</summary>
    public static bool OpenMenuAndTeleport(string mapName)
    {
        try
        {
            if (string.IsNullOrEmpty(mapName)) return false;

            // === KHÓA TELEPORT ===
            // Nếu vừa click teleport trong session này mà chưa load xong → reject
            if (_teleportClickedThisSession && Time.time - _teleportClickedAt < 6f)
            {
                // Đang chờ teleport hoàn tất, không gọi menu
                return false;
            }

            if (!EnsureTeleportPanelVisible())
            {
                // Throttle: caller (RunTeleportToBossMap/RunReturnToFarmMap) gọi mỗi frame,
                // nếu không giới hạn sẽ spam hàng chục dòng/giây khi đứng ở map không mở được capsule.
                if (Time.time - _lastNotVisibleWarnAt >= NotVisibleWarnInterval)
                {
                    _lastNotVisibleWarnAt = Time.time;
                    Plugin.Log.LogWarning($"[MapTransporter] CapsulePanel not visible - cannot teleport to '{mapName}' (throttled log, max 1x/{NotVisibleWarnInterval:F0}s)");
                }
                return false;
            }

            var panel = GameAPI.FindCapsulePanel();
            // 1) Thử gọi trực tiếp method openByName/selectByName/teleportByName trên CapsulePanel
            if (panel != null && TryCallByName(panel, mapName))
            {
                _teleportClickedThisSession = true;
                _teleportClickedAt = Time.time;
                return true;
            }

            // 2) Fallback: tìm Button UI trong panel CapsulePanel có text chứa mapName
            bool clicked = ClickMapButtonInPanel(mapName, panel as Component);
            if (clicked)
            {
                _teleportClickedThisSession = true;
                _teleportClickedAt = Time.time;
            }
            return clicked;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[MapTransporter] OpenMenuAndTeleport('{mapName}') fail: {ex.Message}");
            return false;
        }
    }

    private static bool TryCallByName(object panel, string mapName)
    {
        try
        {
            var t = panel.GetType();
            foreach (var name in new[] { "openByName", "teleportByName", "selectMap", "cmdTeleport", "Teleport", "TeleportToMap", "SelectMapByName" })
            {
                var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                {
                    m.Invoke(panel, new object[] { mapName });
                    Plugin.Log.LogInfo($"[MapTransporter] Called {t.Name}.{name}('{mapName}')");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[MapTransporter] TryCallByName: {ex.Message}");
        }
        return false;
    }

    // Chống spam log "No button contains" — chỉ dump danh sách nút 1 lần
    private static bool _dumpedCapsuleButtons = false;
    private static float _lastNoButtonWarnAt = -999f;

    private static bool ClickMapButtonInPanel(string mapName, Component root)
    {
        try
        {
            if (root == null) root = GameAPI.FindCapsulePanel() as Component;
            if (root == null)
            {
                Plugin.Log.LogWarning("[MapTransporter] No CapsulePanel component for fallback");
                return false;
            }

            var buttons = root.GetComponentsInChildren<Button>(true);
            var candidates = new List<(Button btn, string text, int score)>();

            // Thu thập tất cả nút có text
            foreach (var btn in buttons)
            {
                if (btn == null) continue;
                string text = UIHelper.GetButtonText(btn);
                if (string.IsNullOrEmpty(text)) continue;
                candidates.Add((btn, text, 0));
            }

            if (candidates.Count == 0)
            {
                Plugin.Log.LogWarning("[MapTransporter] No buttons with text in CapsulePanel");
                return false;
            }

            // Normalize search term
            string searchNorm = NormalizeMapName(mapName);

            // Tính điểm cho từng nút
            foreach (var (btn, text, _) in candidates.ToArray())
            {
                string textNorm = NormalizeMapName(text);
                int score = 0;

                // 1. Exact match (sau khi normalize)
                if (textNorm == searchNorm) score = 1000;
                // 2. Contains full search term
                else if (textNorm.Contains(searchNorm)) score = 500;
                // 3. All tokens present
                else
                {
                    var searchTokens = searchNorm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var textTokens = textNorm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int matches = searchTokens.Count(st => textTokens.Any(tt => tt.Contains(st) || st.Contains(tt)));
                    if (matches == searchTokens.Length) score = 200 + matches * 10;
                }

                candidates[candidates.FindIndex(x => x.btn == btn)] = (btn, text, score);
            }

            // Sắp xếp và chọn nút có điểm cao nhất
            var best = candidates.OrderByDescending(x => x.score).FirstOrDefault();
            if (best.score > 0)
            {
                Plugin.Log.LogInfo($"[MapTransporter] Clicking button: '{best.text}' (score={best.score}) for map '{mapName}'");
                best.btn.onClick.Invoke();
                return true;
            }

            // Không tìm thấy → throttle log + dump danh sách 1 lần
            if (Time.time - _lastNoButtonWarnAt >= 3f)
            {
                _lastNoButtonWarnAt = Time.time;
                Plugin.Log.LogWarning($"[MapTransporter] No button matches '{mapName}' in CapsulePanel (throttled 1x/3s)");

                if (!_dumpedCapsuleButtons)
                {
                    _dumpedCapsuleButtons = true;
                    Plugin.Log.LogWarning($"[MapTransporter] Available capsule destinations ({candidates.Count}):");
                    foreach (var (_, text, _) in candidates.Take(20))
                        Plugin.Log.LogWarning($"  - '{text}'");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[MapTransporter] ClickMapButtonInPanel fail: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Normalize tên map: bỏ dấu tiếng Việt, lowercase, bỏ suffix (LvNN), collapse whitespace.
    /// Ví dụ: "Thị Trấn Cổ (Lv25)" → "thi tran co"
    /// </summary>
    private static string NormalizeMapName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        // Bỏ dấu tiếng Việt (FormD + drop NonSpacingMark + đ→d)
        var form = name.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(form.Length);
        foreach (var c in form)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        string result = sb.ToString().Replace('đ', 'd');

        // Bỏ suffix (LvNN) hoặc (Lv NN)
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s*\(?\s*lv\s*\d+\s*\)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Collapse whitespace
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");

        return result.Trim();
    }

    public static bool GoHome()
    {
        var config = Plugin.Instance?.Config;
        if (config == null) return false;
        string[] tries = { config.HomeMapName, "Quay", "Town" };
        foreach (var name in tries)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (OpenMenuAndTeleport(name))
            {
                Plugin.Log.LogInfo($"[MapTransporter] GoHome via '{name}'");
                return true;
            }
        }
        Plugin.Log.LogWarning("[MapTransporter] GoHome: all attempts failed");
        return false;
    }

    public static bool GoBack()
    {
        string[] tries = { "Trở về chỗ cũ", "Về chỗ cũ", "Trở về", "Về trạm trước" };
        foreach (var name in tries)
        {
            if (OpenMenuAndTeleport(name)) 
            {
                Plugin.Log.LogInfo($"[MapTransporter] GoBack via '{name}'");
                return true;
            }
        }
        Plugin.Log.LogWarning("[MapTransporter] GoBack: all attempts failed");
        return false;
    }

    public static bool GoToTownFarmMap()
    {
        var config = Plugin.Instance?.Config;
        if (config == null) return false;
        string[] tries = { config.TownMapName, "Ngoại Ô Thị Trấn", "Ngoai O" };
        foreach (var name in tries)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (OpenMenuAndTeleport(name)) return true;
        }
        return false;
    }


}