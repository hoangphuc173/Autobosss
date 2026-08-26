namespace AutoBossGrabber;

public enum AutoBossState
{
    Idle,
    FarmTown,
    TeleportToBossMap,
    WalkToPortal,
    ZoneScanLoop,
    MoveToBoss,
    CombatBoss,
    LootDrops,
    ReturnToFarmMap,
    ReverseWalkToFarm,
    RestoreFarmZone,
    ResumeFarming,
    TeleportHome,
    SolveCaptcha,
    DeadRecovery
}
