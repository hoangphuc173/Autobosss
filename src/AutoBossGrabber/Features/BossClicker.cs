using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// Click chuột vào boss để game TARGET boss: auto Q đánh đúng boss và
/// thanh HP mục tiêu hiện HP boss — không cần phân biệt boss vs quái thường
/// bằng chữ/HP nữa (BossDetector đã tìm ra đúng object boss).
///
/// Dùng PostMessage WM_LBUTTONDOWN/UP gửi thẳng vào HWND của game:
/// - KHÔNG di chuyển con trỏ chuột thật (người chơi vẫn dùng máy bình thường),
/// - vẫn nhận khi game không phải cửa sổ foreground.
/// Unity (legacy Input) đọc chuột từ message queue nên PostMessage có tác dụng.
/// </summary>
public static class BossClicker
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP   = 0x0202;
    private const uint MK_LBUTTON     = 1u;

    private static float _lastClickAt = -999f;
    private const float ClickInterval = 8.0f;

    private static IntPtr _hwnd;
    private static bool _loggedOnce;

    private static IntPtr GameHwnd
    {
        get
        {
            if (_hwnd == IntPtr.Zero)
                _hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            return _hwnd;
        }
    }

    /// <summary>
    /// Click vào boss nếu boss đang trong màn hình. Throttle ~1.2s/lần.
    /// force = true bỏ qua throttle (dùng khi vừa vào combat).
    /// </summary>
    public static void ClickBoss(object boss, bool force = false)
    {
        try
        {
            if (boss == null) return;
            if (!force && Time.time - _lastClickAt < ClickInterval) return;

            var hwnd = GameHwnd;
            if (hwnd == IntPtr.Zero) return;

            var mb = boss as MonoBehaviour;
            if (mb == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            // Nhích lên thân boss (transform thường ở chân) để trúng collider
            Vector3 world = mb.transform.position + new Vector3(0f, 0.5f, 0f);
            Vector3 sp = cam.WorldToScreenPoint(world);

            // Ngoài màn hình hoặc sau camera → bỏ qua
            if (sp.z <= 0f || sp.x < 0f || sp.x > Screen.width || sp.y < 0f || sp.y > Screen.height)
                return;

            // Unity: gốc dưới-trái → client coords gốc trên-trái
            int cx = Mathf.RoundToInt(sp.x);
            int cy = Mathf.RoundToInt(Screen.height - sp.y);
            IntPtr lParam = (IntPtr)((cy << 16) | (cx & 0xFFFF));

            _lastClickAt = Time.time;
            PostMessage(hwnd, WM_LBUTTONDOWN, (UIntPtr)MK_LBUTTON, lParam);
            PostMessage(hwnd, WM_LBUTTONUP, UIntPtr.Zero, lParam);

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                Plugin.Log.LogInfo($"[BossClicker] click boss tại client=({cx},{cy}) hwnd={hwnd}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[BossClicker] fail: {ex.Message}");
        }
    }
}
