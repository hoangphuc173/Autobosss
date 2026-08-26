using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace AutoBossGrabber;

[BepInPlugin("com.autobossgrabber.vtdc", "Auto Boss Grabber", "2.0.0")]
public class Plugin : BasePlugin
{
    public static Plugin Instance { get; private set; }
    public new static ManualLogSource Log;

    public AutoBossRunner Runner { get; internal set; }
    public new AutoBossConfig Config { get; private set; }
    public GameOptimizer Optimizer { get; private set; }
    public SocketClient SocketClient { get; private set; }

    private static readonly object pipeLock = new object();
    private static StreamWriter pipeWriter;

    private static void ConnectToLauncherPipe()
    {
        Task.Run(() =>
        {
            try
            {
                var pipeClient = new NamedPipeClientStream(".", "AutoBossLauncherPipe", PipeDirection.Out, PipeOptions.Asynchronous);
                pipeClient.Connect(3000); // Wait up to 3s for the launcher
                var writer = new StreamWriter(pipeClient) { AutoFlush = true };
                lock (pipeLock)
                {
                    pipeWriter = writer;
                }
                writer.WriteLine("[AutoBossGrabber] Plugin connected to Launcher!");
            }
            catch (Exception)
            {
                // Launcher not running or pipe error, ignore
            }
        });
    }

    public static void SendLogToLauncher(string message)
    {
        // Log events co the den tu bat ky thread nao -> phai lock de tranh
        // race voi viec connect/replace writer tu background task.
        StreamWriter writer;
        lock (pipeLock)
        {
            writer = pipeWriter;
        }

        if (writer == null)
        {
            return;
        }

        try
        {
            writer.WriteLine(message);
        }
        catch
        {
            lock (pipeLock)
            {
                if (ReferenceEquals(pipeWriter, writer))
                {
                    pipeWriter = null;
                }
            }
        }
    }

    public override void Load()
    {
        Instance = this;
        Log = base.Log;
        Config = new AutoBossConfig();

        ConnectToLauncherPipe();

        // Forward BepInEx log ? UI overlay and Launcher
        Log.LogEvent += (_, e) => 
        {
            string msg = e.Data.ToString();
            AutoBossUI.AddLog(msg);
            SendLogToLauncher(msg);
        };

        Log.LogInfo("=== Auto Boss Grabber v2.0.0 LOADED ===");
        Log.LogInfo("F1 = Toggle Auto Boss ON/OFF");
        Log.LogInfo("F2 = Dump all UI panels (auto) + runtime types");
        Log.LogInfo("F3 = Test open Teleport menu (CapsulePanel)");
        Log.LogInfo("F4 = Test open Zone switch menu");
        Log.LogInfo("F5 = Test 'Go back' (return home)");
        Log.LogInfo("F8 = Dump message command stats (xac dinh cmd boss announce)");

        try
        {
            GameAPI.WarmupTypeCache();

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<AutoBossRunner>();
                ClassInjector.RegisterTypeInIl2Cpp<AutoBossUI>();
                ClassInjector.RegisterTypeInIl2Cpp<VirtualMouse>();
                ClassInjector.RegisterTypeInIl2Cpp<AutoLoginController>();
                ClassInjector.RegisterTypeInIl2Cpp<GameOptimizer>();
                ClassInjector.RegisterTypeInIl2Cpp<SocketClient>();
                Log.LogInfo("[BOOT] Registered AutoBossRunner + AutoBossUI + VirtualMouse + AutoLoginController + GameOptimizer + SocketClient in Il2Cpp domain");
            }
            catch (Exception regEx)
            {
                Log.LogWarning($"[BOOT] ClassInjector warn: {regEx.Message}");
            }

            // Initialize Harmony for GameOptimizer
            var harmony = new Harmony("com.autobossgrabber.vtdc");
            
            // Pattern t? Tool_Om_Boss (verified Update() tick):
            // ((BasePlugin)this).AddComponent<T>() attach vï¿½o gameObject c?a BepInEx plugin ï¿½
            // gameObject dï¿½ n?m trong DontDestroyOnLoad vï¿½ Update() du?c Unity loop g?i bï¿½nh thu?ng.
            ((BasePlugin)this).AddComponent<AutoBossRunner>();
            ((BasePlugin)this).AddComponent<AutoBossUI>();
            ((BasePlugin)this).AddComponent<AutoLoginController>();
            
            // Add and initialize GameOptimizer
            Optimizer = ((BasePlugin)this).AddComponent<GameOptimizer>();
            Optimizer.Initialize(harmony);
            
            // Add SocketClient component for IPC with Manager
            SocketClient = ((BasePlugin)this).AddComponent<SocketClient>();
            
            Log.LogInfo($"[OK] AutoBossRunner + AutoBossUI + AutoLoginController + GameOptimizer + SocketClient attached via BasePlugin.AddComponent (frame={Time.frameCount})");
        }
        catch (Exception ex)
        {
            Log.LogError($"[FAIL] Load exception: {ex.Message}\n{ex.StackTrace}");
        }
    }
}