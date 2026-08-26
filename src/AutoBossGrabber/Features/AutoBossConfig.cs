using System.Collections.Generic;

namespace AutoBossGrabber;

/// <summary>
/// Profile cho một boss cụ thể: chứa tên nhận dạng + thông tin địa điểm + route di chuyển.
/// Tool sẽ tự chọn profile phù hợp khi nhận thông báo boss xuất hiện.
/// </summary>
public class BossProfile
{
    /// <summary>Tên hiển thị, dùng trong log.</summary>
    public string ProfileName = "";

    /// <summary>Các biến thể tên boss trong thông báo (không phân biệt dấu/hoa-thường).</summary>
    public List<string> BossNames = new List<string>();

    /// <summary>Map nơi boss đứng (đích cuối cùng, cũng là map ZoneScanLoop chạy).</summary>
    public List<string> BossMapNames = new List<string>();

    /// <summary>
    /// Map teleport nhanh đến trước (neo đầu của hành trình).
    /// Rỗng = logic mặc định cũ (Planet Plant cho Cung, else teleport thẳng BossMap).
    /// </summary>
    public string FastTravelAnchorMap = "";

    /// <summary>
    /// Các phòng trung gian phải đi bộ qua theo thứ tự sau khi đến FastTravelAnchorMap.
    /// Rỗng = không có bước đi bộ trung gian.
    /// </summary>
    public List<string> PortalChainMaps = new List<string>();
}

public class AutoBossConfig
{
    public bool Enabled = false;
    public bool AutoDetectBossNotification = true;
    public bool AutoFusion = true;  // Tự động hợp thể (nút P) khi tool chạy

    // === Farm loop (task 13) ===
    public bool EnableAutoZoneSwitch = true;   // tu dong sang khu ke tiep khi khu het quai
    public bool EnableAutoReward = true;       // tu dong bam popup nhan thuong
    public bool EnableAutoSatellite = false;   // tu dong dung item satellite (tang exp)
    public List<int> SatelliteItemIds = new List<int>(); // id item satellite trong hanh trang

    // === Multi-boss profiles ===
    // Khi nhận thông báo boss, tool tự khớp text với BossNames của từng profile
    // rồi áp dụng route tương ứng.
    // Thêm profile mới ở đây để hỗ trợ thêm boss.
    public List<BossProfile> BossProfiles = new List<BossProfile>
    {
        new BossProfile
        {
            ProfileName    = "Vegiita",
            BossNames      = new List<string> { "Vua Vegita", "Vua Vegiita" },
            BossMapNames   = new List<string> { "Cung" },
            FastTravelAnchorMap = "",          // dùng logic Planet Plant cũ
            PortalChainMaps = new List<string>(),
        },
        new BossProfile
        {
            ProfileName    = "Cooler",
            BossNames      = new List<string> { "Cooler", "Coller" },
            BossMapNames   = new List<string> { "Pháo đài Frizar" },
            FastTravelAnchorMap = "Trạm Frizar",
            PortalChainMaps = new List<string> { "Phòng tinh anh", "Phòng tinh nhuệ" },
        },
    };

    // === Active fields (được ghi đè bởi ApplyBossProfile) ===
    // Khi không dùng BossProfiles (legacy), đặt trực tiếp ở đây.
    public List<string> BossNames = new List<string> { "Vua Vegita", "Vua Vegiita" };
    public List<string> BossMapNames = new List<string> { "Cung" };
    public string HomeMapName = "Quay";
    public string TownMapName = "Ngoai";
    public string FastTravelAnchorMap = "";
    public List<string> PortalChainMaps = new List<string>();

    public int MaxZoneAttempts = 5;
    public float AttackRange = 2.5f;
    public float CombatTimeoutSec = 60f;
    public float LootIdleTimeoutSec = 3f;
    public float RetreatHpPct = 20f;
    public float ScanBossEverySec = 3f;
    public float TeleportTimeoutSec = 15f;
    public float LootRadius = 200f;
    public string KeyToggle = "F1";
    public string KeyDump = "F2";
    public string KeyTestTeleport = "F3";
    public string KeyTestZone = "F4";
    public string KeyTestGoBack = "F5";

    // === Skill tự động khi HP boss xuống ngưỡng (HP THẬT, không phải %) ===
    // Mỗi entry: { HpThreshold, SkillKey }
    // Kiểm tra theo thứ tự trong list -> đặt ngưỡng thấp nhất TRƯỚC nếu muốn ưu tiên.
    public List<SkillTrigger> BossSkillTriggers = new List<SkillTrigger>
    {
        new SkillTrigger { HpThreshold = 500000f, SkillKey = 1 },
        new SkillTrigger { HpThreshold = 200000f, SkillKey = 2 },
        new SkillTrigger { HpThreshold = 50000f,  SkillKey = 3 },
        new SkillTrigger { HpThreshold = 10000f,  SkillKey = 4 },
    };

    // === Auto Use & Drop Items ===
    public List<int> AutoUseItemIds = new List<int>();
    public List<int> AutoDropItemIds = new List<int>();
    public float AutoItemDelaySec = 4.0f;
}
