using BepInEx.Logging;
using Xunit;

namespace AutoBoss.Tests;

/// <summary>
/// Khoi tao moi truong test: Plugin.Log phai khac null vi cac class plugin
/// (GraphCache, BFSPathfinder, ItemFilterManager...) goi Plugin.Log truc tiep.
/// ManualLogSource co the tao doc lap, khong can BepInEx runtime.
/// </summary>
public static class TestBootstrap
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        AutoBossGrabber.Plugin.Log ??= new ManualLogSource("AutoBossTests");
        _initialized = true;
    }
}

/// <summary>Chay truoc moi test trong assembly - dam bao Plugin.Log san sang.</summary>
internal static class ModuleInit
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init() => TestBootstrap.Initialize();
}

[CollectionDefinition("PluginEnv", DisableParallelization = true)]
public class PluginEnvCollection
{
}
