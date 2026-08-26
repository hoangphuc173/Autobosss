using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// Scan mobs trong scene, tìm mob là boss.
/// 3 lớp detection (pattern từ Tool_Om_Boss AutoRedRibbon):
///   Lớp 1: mobType từ MobInfo.DLDADDIDNPM (ưu tiên cao nhất - server-side flag)
///   Lớp 2: HasBossFlag - scan field/property có tên "boss"/"elite"/"leader"
///   Lớp 3: name match pattern trong BossNames config
/// </summary>
public static class BossDetector
{
    private readonly struct BossCandidate
    {
        public readonly object Entity;
        public readonly string Source;

        public BossCandidate(object entity, string source)
        {
            Entity = entity;
            Source = source;
        }
    }

    // Throttle log "Found boss" để không spam mỗi scan cycle
    private static float _lastFoundLogTime = -999f;
    private static float _lastNoBossSampleLogTime = -999f;
    private const float LogCooldown = 10f;
    private const float NoBossSampleLogCooldown = 8f;

    // Cache optimization: tránh scan lại candidates trong cùng frame/scene
    private static int _lastScanFrame = -1;
    private static List<BossCandidate> _cachedCandidates = null;
    private static int _lastSceneInstanceId = 0;

    // Hints mạnh (tên mob có chứa 1 trong các từ này → chắc chắn là boss)
    private static readonly string[] StrongBossNameHints = new[]
    {
        "boss", "truongboss", "bossicon", "trunguy", "daita", "daiuy",
        "daituong", "ninja", "thieutuong", "docnhan", "trungta", "tongtulenh", "thuongta"
    };

    public static object FindBoss(List<string> bossNames)
    {
        // Cache candidates trong cùng frame + scene để tránh scan lại
        int currentFrame = Time.frameCount;
        int currentSceneId = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetHashCode();

        if (_lastScanFrame != currentFrame || _lastSceneInstanceId != currentSceneId || _cachedCandidates == null)
        {
            _cachedCandidates = CollectCandidates();
            _lastScanFrame = currentFrame;
            _lastSceneInstanceId = currentSceneId;
        }

        var candidates = _cachedCandidates;
        if (candidates.Count == 0) return null;

        object bestMatch = null;
        string bestSource = "";
        float bestDist = float.MaxValue;
        var myPos = GameAPI.GetPlayerPosition();
        int aliveSeen = 0;
        var samples = new List<string>();

        foreach (var candidate in candidates)
        {
            var entity = candidate.Entity;
            if (entity == null) continue;
            if (!GameAPI.IsMobAlive(entity)) continue;

            // Lớp 1: mobType (server-side flag) - BEST DETECTION
            int mobType = GameAPI.GetMobType(entity);
            aliveSeen++;
            string displayName = GameAPI.GetEntityDisplayName(entity, bossNames);
            if (samples.Count < 6)
            {
                int hp = GameAPI.GetMobHp(entity);
                var samplePos = GameAPI.GetMobPosition(entity);
                samples.Add($"'{displayName}' src={candidate.Source} type={mobType} hp={hp} pos={samplePos}");
            }

            if (mobType != -1 && mobType != 1)
            {
                var pos = GameAPI.GetMobPosition(entity);
                float d = Vector2.Distance(myPos, pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestMatch = entity;
                    bestSource = candidate.Source;
                }
                continue;
            }

            // Lớp 2: HasBossFlag (field/property scanned)
            if (GameAPI.HasBossFlag(entity) || GameAPI.HasBossFlag(GameAPI.GetMobSelfInfo(entity)))
            {
                var pos = GameAPI.GetMobPosition(entity);
                float d = Vector2.Distance(myPos, pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestMatch = entity;
                    bestSource = candidate.Source;
                }
                continue;
            }

            // Lớp 3: tên khớp pattern user config
            if (bossNames != null && bossNames.Count > 0)
            {
                if (!string.IsNullOrEmpty(displayName))
                {
                    foreach (var pattern in bossNames)
                    {
                        if (string.IsNullOrEmpty(pattern)) continue;
                        if (displayName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var pos = GameAPI.GetMobPosition(entity);
                            float d = Vector2.Distance(myPos, pos);
                            if (d < bestDist)
                            {
                                bestDist = d;
                                bestMatch = entity;
                                bestSource = candidate.Source;
                            }
                            break;
                        }
                    }
                }
            }

            // Lớp 4: strong hints khi tên boss không nằm trong config
            if (!string.IsNullOrEmpty(displayName))
            {
                foreach (var hint in StrongBossNameHints)
                {
                    if (string.IsNullOrEmpty(hint)) continue;
                    if (displayName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var pos = GameAPI.GetMobPosition(entity);
                        float d = Vector2.Distance(myPos, pos);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestMatch = entity;
                            bestSource = candidate.Source;
                        }
                        break;
                    }
                }
            }
        }

        if (bestMatch != null && Time.time - _lastFoundLogTime >= LogCooldown)
        {
            _lastFoundLogTime = Time.time;
            string name = GameAPI.GetEntityDisplayName(bestMatch, bossNames);
            int type = GameAPI.GetMobType(bestMatch);
            Plugin.Log.LogInfo($"[BossDetector] Found boss '{name}' src={bestSource} type={type} at {GameAPI.GetMobPosition(bestMatch)} (dist={bestDist:F0})");
        }
        else if (bestMatch == null && aliveSeen > 0 && Time.time - _lastNoBossSampleLogTime >= NoBossSampleLogCooldown)
        {
            _lastNoBossSampleLogTime = Time.time;
            Plugin.Log.LogInfo($"[BossDetector] No boss match among {aliveSeen} alive entities. Sample: {string.Join("; ", samples)}");
        }

        return bestMatch;
    }

    /// <summary>True nếu mob đã chết / biến mất / hp <= 0.</summary>
    public static bool IsDeadOrMissing(object mob) => mob == null || !GameAPI.IsMobAlive(mob);

    public static string GetMobNameSafe(object mob) => GameAPI.GetMobName(mob);

    /// <summary>Invalidate cache khi đổi zone/map để force rescan.</summary>
    public static void InvalidateCache()
    {
        _cachedCandidates = null;
        _lastScanFrame = -1;
        _lastSceneInstanceId = 0;
    }

    private static List<BossCandidate> CollectCandidates()
    {
        var result = new List<BossCandidate>();
        var seen = new HashSet<int>();

        void AddRange(List<object> items, string source)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null) continue;
                try
                {
                    if (item is UnityEngine.Object uo)
                    {
                        int id = uo.GetInstanceID();
                        if (id != 0 && !seen.Add(id)) continue;
                    }
                }
                catch { }
                result.Add(new BossCandidate(item, source));
            }
        }

        AddRange(GameAPI.FindAllMobs(), "Mob");
        AddRange(GameAPI.FindAllNPCs(), "NPC");
        return result;
    }
}
