using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime;
using HarmonyLib;
using UnityEngine;
using BepInEx.Logging;

namespace AutoBossGrabber;

/// <summary>
/// Aggressive memory and CPU optimization for multi-instance support.
/// Reduces RAM from ~1200MB to <800MB per instance through:
/// - Windows API EmptyWorkingSet + SetProcessWorkingSetSize
/// - GC LatencyMode and LOH compaction
/// - Harmony patches disabling ParallaxBackground rendering
/// Target: Enable 10+ instances on 16GB RAM machine
/// Pattern from Tool_Up_Level_V111 GameOptimizer.cs
/// </summary>
public class GameOptimizer : MonoBehaviour
{
    // === P/Invoke Declarations ===
    
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);
    
    // === Configuration ===
    private const float OptimizationIntervalSec = 60f;
    private bool isEnabled = true;
    private float lastOptimizationTime = 0f;
    
    // === Initialization ===
    
    public void Initialize(Harmony harmony)
    {
        try
        {
            Plugin.Log.LogInfo("[GameOptimizer] Initializing...");
            
            ConfigureGarbageCollector();
            ApplyRenderingPatches(harmony);
            
            // Execute initial optimization immediately
            ExecuteOptimization();
            
            Plugin.Log.LogInfo("[GameOptimizer] Initialization complete");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[GameOptimizer] Initialization failed: {ex.Message}");
        }
    }
    
    // === Unity Lifecycle ===
    
    void Update()
    {
        if (!isEnabled) return;
        
        // Execute optimization every 60 seconds
        if (Time.time - lastOptimizationTime >= OptimizationIntervalSec)
        {
            ExecuteOptimization();
            lastOptimizationTime = Time.time;
        }
    }
    
    // === Memory Optimization ===
    
    private void ExecuteOptimization()
    {
        try
        {
            long memBefore = GC.GetTotalMemory(false);
            
            // Step 1: Force full GC with compaction
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            
            // Step 2: Windows API memory release
            IntPtr processHandle = Process.GetCurrentProcess().Handle;
            EmptyWorkingSet(processHandle);
            SetProcessWorkingSetSize(processHandle, new IntPtr(-1), new IntPtr(-1));
            
            long memAfter = GC.GetTotalMemory(false);
            long freed = memBefore - memAfter;
            
            // Log only if significant memory freed (> 1MB)
            if (freed > 1024 * 1024)
            {
                Plugin.Log.LogInfo($"[GameOptimizer] Memory optimized: freed {freed / 1024 / 1024}MB " +
                                    $"(Before: {memBefore / 1024 / 1024}MB, After: {memAfter / 1024 / 1024}MB)");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameOptimizer] Optimization failed: {ex.Message}");
        }
    }
    
    // === GC Configuration ===
    
    private void ConfigureGarbageCollector()
    {
        try
        {
            // SustainedLowLatency reduces GC pause times
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            
            // CompactOnce: compact Large Object Heap (>85KB objects) on next GC
            // Critical for multi-instance: Unity allocates many large textures/meshes
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            
            Plugin.Log.LogInfo("[GameOptimizer] GC configured: SustainedLowLatency + LOH CompactOnce");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameOptimizer] GC config failed: {ex.Message}");
        }
    }
    
    // === Harmony Patches for Rendering ===
    
    private void ApplyRenderingPatches(Harmony harmony)
    {
        try
        {
            // Patch 1: Disable ParallaxBackground (expensive shader rendering)
            var tParallax = GameAPI.FindTypeByName("ParallaxBackground");
            if (tParallax != null)
            {
                var mUpdate = tParallax.GetMethod("FixedUpdate", 
                    System.Reflection.BindingFlags.Instance | 
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.NonPublic);
                
                if (mUpdate != null)
                {
                    harmony.Patch(mUpdate, 
                        postfix: new HarmonyMethod(
                            typeof(GameOptimizer_Patches), 
                            nameof(GameOptimizer_Patches.ParallaxBackground_FixedUpdate_Postfix)));
                    
                    Plugin.Log.LogInfo("[GameOptimizer] Patched ParallaxBackground.FixedUpdate");
                }
            }
            
            // Patch 2: Reduce shadow quality via QualitySettings
            QualitySettings.shadowDistance = 0f;
            QualitySettings.shadows = ShadowQuality.Disable;
            
            Plugin.Log.LogInfo("[GameOptimizer] Rendering patches applied");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameOptimizer] Patch failed: {ex.Message}");
        }
    }
    
    // === Public Control ===
    
    public void SetEnabled(bool enabled)
    {
        isEnabled = enabled;
        Plugin.Log.LogInfo($"[GameOptimizer] Optimization {(enabled ? "enabled" : "disabled")}");
    }
    
    public void ForceOptimizeNow()
    {
        ExecuteOptimization();
    }
}

// === Harmony Patch Class ===

[HarmonyPatch]
public static class GameOptimizer_Patches
{
    /// <summary>
    /// Disable ParallaxBackground rendering after its FixedUpdate.
    /// This prevents expensive background shader execution.
    /// </summary>
    public static void ParallaxBackground_FixedUpdate_Postfix(MonoBehaviour __instance)
    {
        try
        {
            if (__instance != null && __instance.enabled)
            {
                __instance.enabled = false;
            }
        }
        catch
        {
            // Silently ignore - component may be destroyed
        }
    }
}
