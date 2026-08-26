using BepInEx.Logging;
using Xunit;

namespace AutoBoss.Tests;

/// <summary>
/// Khoi tao moi truong test: Plugin.Log phai khac null vi cac class plugin
/// (GraphCache, BFSPathfinder...) goi Plugin.Log truc tiep.
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

[CollectionDefinition("PluginEnv", DisableParallelization = true)]
public class PluginEnvCollection
{
}
