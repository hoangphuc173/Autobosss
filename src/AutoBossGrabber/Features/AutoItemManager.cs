using System;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// Quản lý logic Tự Động Sử Dụng (Mở rương/hộp) và Tự Động Vứt (Item có HSD).
/// Lớp này nên được gọi trong Update loop của Plugin hoặc AutoBoss.
/// </summary>
public class AutoItemManager
{
    private static AutoItemManager _instance;
    public static AutoItemManager Instance => _instance ??= new AutoItemManager();

    private float _lastItemUseTime = 0f;
    private float _tickTimer = 0f;

    public void Update(AutoBossConfig config)
    {
        if (config == null || !config.Enabled) return;

        // Giới hạn tần suất check hành trang (ví dụ mỗi 0.5s một lần thay vì mỗi frame)
        _tickTimer += Time.deltaTime;
        if (_tickTimer < 0.5f) return;
        _tickTimer = 0f;

        // 1. Logic Tự Động Vứt Item (Không có delay)
        if (config.AutoDropItemIds != null && config.AutoDropItemIds.Count > 0)
        {
            foreach (int dropId in config.AutoDropItemIds)
            {
                // Gọi API để tìm và vứt (option 93 = HSD)
                GameAPI.TryDropItem(dropId, 93);
            }
        }

        // 2. Logic Tự Động Sử Dụng Item (Có delay)
        if (config.AutoUseItemIds != null && config.AutoUseItemIds.Count > 0)
        {
            if (Time.time - _lastItemUseTime >= config.AutoItemDelaySec)
            {
                foreach (int useId in config.AutoUseItemIds)
                {
                    // Chỉ thử gọi mở hộp đầu tiên tìm thấy rồi break chờ delay tiếp theo
                    // (Tránh spam nhiều hộp cùng lúc gây lag server)
                    GameAPI.TryUseItem(useId);
                }
                
                // Cập nhật lại thời gian sử dụng
                _lastItemUseTime = Time.time;
            }
        }
    }
}
