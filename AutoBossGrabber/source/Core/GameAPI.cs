using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// Wrapper cho các class thật trong game.
/// Pattern tham khảo từ NpcScanner (decompile từ Tool_Om_Boss):
///   - MainPlayer myPlayer = GameRuntimeCompat.GetMyPlayer();
///   - Il2CppArrayBase<Mob> mobs = Object.FindObjectsOfType<Mob>();
///   - GameManager.gI() singleton
///   - ((Character)myPlayer).getX() / getY()
///
/// AutoBossGrabber dùng reflection (Type.GetType) để CHỊU ĐỰNG đổi tên class
/// sau mỗi bản cập nhật game, nhưng khi đã biết tên class thật (cache),
/// sẽ dùng typed reference qua Il2CppInterop để nhanh hơn.
/// </summary>
public static class GameAPI
{
    // ===== Type cache (resolved lazily) =====
    private static Type _tGameManager = null;
    private static Type _tMainPlayer = null;
    private static Type _tCharacter = null;
    private static Type _tMob = null;
    private static Type _tNPC = null;
    private static Type _tCapsulePanel = null;
    private static Type _tItemMap = null;
    private static Type _tInventoryPanel = null;
    private static Type _tZonePanel = null;
    private static Type _tDeathPanel = null;
    private static Type _tChangeMap = null;

    // ===== MethodInfo cache =====
    private static MethodInfo _miGi = null;
    private static MethodInfo _miGetMyPlayer = null;
    private static MethodInfo _miPlayerMoveTo = null;
    private static MethodInfo _miGetX = null;
    private static MethodInfo _miGetY = null;
    private static MethodInfo _miGetHp = null;
    private static MethodInfo _miGetMaxHp = null;
    private static MethodInfo _miGetInfoInBar = null;
    private static readonly List<MethodInfo> _miInfoBarStringMethods = new List<MethodInfo>();
    private static Type _infoBarStringMethodsType = null;
    private static MethodInfo _miGetInfo = null;

    // ===== State cache =====
    private static object _cachedPlayer = null;
    private static bool _warmedUp = false;
    private static bool _warmingUp = false;

    public static bool WarmupTypeCache()
    {
        if (_warmedUp || _warmingUp) return true;
        _warmingUp = true;
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (asm == null) return false;

            _tGameManager = asm.GetType("GameManager");
            _tMainPlayer = asm.GetType("MainPlayer");
            _tCharacter = asm.GetType("Character");
            _tMob = asm.GetType("Mob");
            _tNPC = asm.GetType("NPC");
            _tCapsulePanel = asm.GetType("CapsulePanel");
            _tItemMap = asm.GetType("ItemMapObject") ?? asm.GetType("ItemMap") ?? asm.GetType("ItemDrop");
            _tInventoryPanel = asm.GetType("InventoryPanel");
            _tZonePanel = asm.GetType("ZoneMenu") ?? asm.GetType("ZonePanel") ?? asm.GetType("ChangeZone");
            _tDeathPanel = asm.GetType("DeathPanel");
            _tChangeMap = asm.GetType("ChangeMap") ?? asm.GetType("MapChangePanel");

            // GameManager.gI()
            _miGi = _tGameManager?.GetMethod("gI", BindingFlags.Public | BindingFlags.Static);

            // MainPlayer instance method (alias list)
            string[] myPlayerAliases = { "getMyPlayer", "GFBGLCKOJIF", "HDCPFPAPIMJ", "NHDNFHIKNHN", "PIJLJGEMAIA", "getMainPlayer" };
            foreach (var alias in myPlayerAliases)
            {
                var m = _tGameManager?.GetMethod(alias, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null && m.GetParameters().Length == 0)
                {
                    _miGetMyPlayer = m;
                    break;
                }
            }

            // Resolve coordinate / HP getters even when moveTo is available.
            if (_tMainPlayer != null)
            {
                _miGetX = _tMainPlayer.GetMethod("getX", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? _tCharacter?.GetMethod("getX", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _miGetY = _tMainPlayer.GetMethod("getY", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? _tCharacter?.GetMethod("getY", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                // Thử nhiều alias cho HP getter (game có thể đổi tên sau update)
                string[] hpAliases = { "getHp", "hp", "HP", "getHP", "get_hp", "get_HP", "getCurrentHp", "currentHp" };
                foreach (var alias in hpAliases)
                {
                    var m = _tMainPlayer.GetMethod(alias, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null && m.GetParameters().Length == 0)
                    {
                        _miGetHp = m;
                        Plugin.Log?.LogInfo($"[GameAPI] Found HP method: MainPlayer.{alias}()");
                        break;
                    }
                }
                if (_miGetHp == null && _tCharacter != null)
                {
                    foreach (var alias in hpAliases)
                    {
                        var m = _tCharacter.GetMethod(alias, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (m != null && m.GetParameters().Length == 0)
                        {
                            _miGetHp = m;
                            Plugin.Log?.LogInfo($"[GameAPI] Found HP method: Character.{alias}()");
                            break;
                        }
                    }
                }

                string[] maxHpAliases = { "getMaxHp", "maxHp", "MaxHp", "maxHP", "MaxHP", "getMaxHP", "get_maxHp", "get_MaxHp" };
                foreach (var alias in maxHpAliases)
                {
                    var m = _tMainPlayer.GetMethod(alias, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null && m.GetParameters().Length == 0)
                    {
                        _miGetMaxHp = m;
                        Plugin.Log?.LogInfo($"[GameAPI] Found MaxHP method: MainPlayer.{alias}()");
                        break;
                    }
                }
                if (_miGetMaxHp == null && _tCharacter != null)
                {
                    foreach (var alias in maxHpAliases)
                    {
                        var m = _tCharacter.GetMethod(alias, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (m != null && m.GetParameters().Length == 0)
                        {
                            _miGetMaxHp = m;
                            Plugin.Log?.LogInfo($"[GameAPI] Found MaxHP method: Character.{alias}()");
                            break;
                        }
                    }
                }

                _miGetInfoInBar = _tCharacter?.GetMethod("getInfoInBar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_miGetInfoInBar != null)
                    Plugin.Log?.LogInfo($"[GameAPI] Found Character.getInfoInBar() -> {_miGetInfoInBar.ReturnType.Name}");

                // Character.getInfoInBar() is the same source used by the working boss tools.
                // Its obfuscated info object exposes one or more strings in the form curHp/maxHp.
                if (_miGetInfoInBar != null)
                {
                    var infoBarType = _miGetInfoInBar.ReturnType;
                    if (infoBarType != null)
                    {
                        _infoBarStringMethodsType = infoBarType;
                        foreach (var m in infoBarType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (m.GetParameters().Length == 0 && m.ReturnType == typeof(string))
                                _miInfoBarStringMethods.Add(m);
                        }
                        Plugin.Log?.LogInfo($"[GameAPI] InfoInBar string getters: {_miInfoBarStringMethods.Count}");
                    }
                }

                // Heuristic fallback: nếu alias không match (game obfuscate tên), scan tất cả no-arg int methods
                // và chọn cặp có ratio hp/maxHp hợp lý. Cần có instance player thật để invoke.
                if (_miGetHp == null || _miGetMaxHp == null)
                {
                    Plugin.Log?.LogWarning("[GameAPI] HP methods not found via alias — trying heuristic scan on MainPlayer...");
                    var playerInst = GetMyPlayer();
                    TryFindHpMethodsHeuristic(_tMainPlayer, out _miGetHp, out _miGetMaxHp, playerInst);
                }
                if ((_miGetHp == null || _miGetMaxHp == null) && _tCharacter != null)
                {
                    Plugin.Log?.LogWarning("[GameAPI] Still not found — trying heuristic scan on Character...");
                    var playerInst = GetMyPlayer();
                    TryFindHpMethodsHeuristic(_tCharacter, out _miGetHp, out _miGetMaxHp, playerInst);
                }
                if (_miGetHp == null || _miGetMaxHp == null)
                {
                    Plugin.Log?.LogWarning("[GameAPI] HP methods still not found after heuristic, dumping for manual inspection...");
                    Il2CppAPI.DumpTypeMethods(_tMainPlayer, "MainPlayer");
                    if (_tCharacter != null)
                    {
                        Plugin.Log?.LogWarning("[GameAPI] Dumping Character methods...");
                        Il2CppAPI.DumpTypeMethods(_tCharacter, "Character");
                    }
                }
            }

            // Ưu tiên 1: MainPlayer.moveTo (Concrete class - từ Tool_Om_Boss GameRuntimeCompat)
            // Lý do: Character là abstract base; MainPlayer là concrete impl có method thực
            if (_tMainPlayer != null)
            {
                // PHẢI chỉ định parameter types - vì MainPlayer.moveTo có 2 overloads:
                //   moveTo(float,float) và moveTo(Vector2,Action,Action)
                var mFloat = _tMainPlayer.GetMethod("moveTo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(float), typeof(float) }, null);
                if (mFloat != null)
                {
                    _miPlayerMoveTo = mFloat;
                    Plugin.Log.LogInfo($"[GameAPI] MoveTo uses MainPlayer.moveTo(float,float)");
                }
                else
                {
                    var m = _tMainPlayer.GetMethod("moveTo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null)
                        Plugin.Log.LogWarning($"[GameAPI] MainPlayer.moveTo exists but signature mismatch: {string.Join(",", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name))}");
                }
            }

            // Fallback 2: Character.moveTo (Abstract base)
            if (_miPlayerMoveTo == null && _tCharacter != null)
            {
                _miGetX ??= _tCharacter.GetMethod("getX", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _miGetY ??= _tCharacter.GetMethod("getY", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _miGetHp ??= _tCharacter.GetMethod("getHp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _miGetMaxHp ??= _tCharacter.GetMethod("getMaxHp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var mFloat = _tCharacter.GetMethod("moveTo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(float), typeof(float) }, null);
                if (mFloat != null)
                {
                    _miPlayerMoveTo = mFloat;
                    Plugin.Log.LogInfo($"[GameAPI] MoveTo uses Character.moveTo(float,float)");
                }
            }

            // Mob.getInfo() returns MobInfo (có NDNPKEBNEEG method for name)
            _miGetInfo = _tMob?.GetMethod("getInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            _warmedUp = true;
            Plugin.Log.LogInfo($"[GameAPI] Warmup OK. GameManager={_tGameManager?.Name}, MainPlayer={_tMainPlayer?.Name}, Mob={_tMob?.Name}, NPC={_tNPC?.Name}, CapsulePanel={_tCapsulePanel?.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[GameAPI] Warmup FAIL: {ex.Message}");
            return false;
        }
        finally
        {
            _warmingUp = false;
        }
    }

    // ===== Player API =====
    public static object GetMyPlayer()
    {
        if (!_warmedUp) WarmupTypeCache();
        try
        {
            if (_miGi == null || _miGetMyPlayer == null) return null;
            var gm = _miGi.Invoke(null, null);
            if (gm == null) return null;
            var p = _miGetMyPlayer.Invoke(gm, null);
            _cachedPlayer = p;
            return p;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameAPI] GetMyPlayer fail: {ex.Message}");
            return null;
        }
    }

    public static Vector2 GetPlayerPosition()
    {
        try
        {
            var p = GetMyPlayer();
            if (p == null) return Vector2.zero;

            if (_miGetX != null && _miGetY != null)
            {
                try
                {
                    float x = Convert.ToSingle(_miGetX.Invoke(p, null));
                    float y = Convert.ToSingle(_miGetY.Invoke(p, null));
                    if (Mathf.Abs(x) > 0.001f || Mathf.Abs(y) > 0.001f)
                        return new Vector2(x, y);
                }
                catch { }
            }

            if (p is MonoBehaviour mb)
                return mb.transform.position;
            if (p is Component comp)
                return comp.transform.position;
        }
        catch { return Vector2.zero; }
        return Vector2.zero;
    }

    // Regex cho chuỗi HP trong info bar: "1.234/5.678" hoặc "1,234 / 5,678"
    private static readonly Regex _infoBarHpPattern =
        new Regex(@"^\s*([0-9][0-9.,]*)\s*/\s*([0-9][0-9.,]*)\s*$", RegexOptions.Compiled);

    private static float _lastGoodHpPct = 100f;
    private static float _lastHpLogAt = -999f;
    private static int _lastLoggedZone = -1;
    private static float _lastZoneLogAt = -999f;

    // Cache kết quả IsCurrentZoneChaotic: khi zone panel đóng, dùng giá trị cache
    // thay vì trả false (gây bug không restore về khu loạn chiến).
    private static bool _cachedChaoticZone = false;
    private static bool _hasCachedChaoticZone = false;

    /// <summary>
    /// Đọc HP player qua Character.getInfoInBar() — nguồn giống các tool boss đang chạy tốt.
    /// Trả về -1 nếu không đọc được.
    /// </summary>
    private static float TryReadHpPctFromInfoBar(object player)
    {
        try
        {
            if (player == null || _miGetInfoInBar == null) return -1f;

            var infoBar = _miGetInfoInBar.Invoke(player, null);
            if (infoBar == null) return -1f;

            // Nếu instance thật là subclass khác lúc warmup, resolve lại string getters.
            var t = infoBar.GetType();
            if (_infoBarStringMethodsType == null || !_infoBarStringMethodsType.IsAssignableFrom(t))
            {
                _miInfoBarStringMethods.Clear();
                _infoBarStringMethodsType = t;
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.GetParameters().Length == 0 && m.ReturnType == typeof(string))
                        _miInfoBarStringMethods.Add(m);
                }
            }

            foreach (var m in _miInfoBarStringMethods)
            {
                string s;
                try { s = m.Invoke(infoBar, null) as string; }
                catch { continue; }

                if (string.IsNullOrEmpty(s) || s.IndexOf('/') < 0) continue;

                string clean = Regex.Replace(s, "<.*?>", string.Empty).Trim();
                var match = _infoBarHpPattern.Match(clean);
                if (!match.Success) continue;

                string curStr = match.Groups[1].Value.Replace(".", "").Replace(",", "");
                string maxStr = match.Groups[2].Value.Replace(".", "").Replace(",", "");
                if (!long.TryParse(curStr, out long cur) || !long.TryParse(maxStr, out long max))
                    continue;

                // Loại bỏ các chuỗi kiểu "1/4" (số slot, cấp sao...) — HP thật luôn lớn.
                if (max < 100 || cur < 0 || cur > max) continue;

                float pct = (cur * 100f) / max;
                if (Time.time - _lastHpLogAt >= 5f)
                {
                    _lastHpLogAt = Time.time;
                    Plugin.Log?.LogInfo($"[GameAPI] Player HP (infoBar.{m.Name}) = {cur}/{max} ({pct:F1}%)");
                }
                return pct;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[GameAPI] TryReadHpPctFromInfoBar fail: {ex.Message}");
        }
        return -1f;
    }

    public static float GetPlayerHpPct()
    {
        try
        {
            var p = GetMyPlayer();
            if (p == null)
            {
                Plugin.Log?.LogWarning("[GameAPI] GetPlayerHpPct: player is null");
                return _lastGoodHpPct;
            }

            // Ưu tiên 1: info bar (đáng tin, không phụ thuộc tên method bị obfuscate)
            float barPct = TryReadHpPctFromInfoBar(p);
            if (barPct >= 0f)
            {
                _lastGoodHpPct = barPct;
                return barPct;
            }

            // Ưu tiên 2: getHp()/getMaxHp() nếu bản game có thật (không dùng kết quả đoán mò)
            if (_miGetHp != null && _miGetMaxHp != null)
            {
                int hp = Convert.ToInt32(_miGetHp.Invoke(p, null));
                int max = Convert.ToInt32(_miGetMaxHp.Invoke(p, null));
                if (max > 0)
                {
                    float pct = (hp * 100f) / max;
                    if (Time.time - _lastHpLogAt >= 5f)
                    {
                        _lastHpLogAt = Time.time;
                        Plugin.Log?.LogInfo($"[GameAPI] Player HP (method) = {hp}/{max} ({pct:F1}%)");
                    }
                    _lastGoodHpPct = pct;
                    return pct;
                }
            }

            // Không có nguồn nào tin cậy -> giữ giá trị cuối, KHÔNG ép 100%
            if (Time.time - _lastHpLogAt >= 5f)
            {
                _lastHpLogAt = Time.time;
                Plugin.Log?.LogWarning($"[GameAPI] GetPlayerHpPct: no reliable HP source, keeping last={_lastGoodHpPct:F1}%");
            }
            return _lastGoodHpPct;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[GameAPI] GetPlayerHpPct fail: {ex.Message}");
            return _lastGoodHpPct;
        }
    }

    public static bool MoveTo(float x, float y)
    {
        try
        {
            var p = GetMyPlayer();
            if (p == null) return false;
            float px = 0f, py = 0f;
            try
            {
                if (_miGetX != null) px = Convert.ToSingle(_miGetX.Invoke(p, null));
                if (_miGetY != null) py = Convert.ToSingle(_miGetY.Invoke(p, null));
            }
            catch { }

            bool ok = false;

            // Ưu tiên 1: MainPlayer.moveTo(float,float) - concrete class
            if (_miPlayerMoveTo != null)
            {
                try { _miPlayerMoveTo.Invoke(p, new object[] { x, y }); ok = true; }
                catch (Exception ex) { Plugin.Log.LogWarning($"[GameAPI] MainPlayer.moveTo fail: {ex.Message}"); }
            }

            // Ưu tiên 2: Player.move(Vector2 direction) - từ Tool_Om_Boss TryIssueMoveTo
            try
            {
                var miMove = p.GetType().GetMethod("move", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (miMove != null)
                {
                    var ps = miMove.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType.Name == "Vector2")
                    {
                        float dx = x - px, dy = y - py;
                        float mag = Mathf.Sqrt(dx * dx + dy * dy);
                        if (mag > 0.001f)
                        {
                            var v = new Vector2(dx / mag, dy / mag);
                            miMove.Invoke(p, new object[] { v });
                            ok = true;
                        }
                    }
                }
            }
            catch { }

            return ok;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameAPI] MoveTo fail: {ex.Message}");
        }
        return false;
    }

    public static bool TryIssueMoveTo(float x, float y) => MoveTo(x, y);

    /// <summary>
    /// Path-only move (pattern từ Tool_Om_Boss GameRuntimeCompat.TryIssueMoveToPathOnly).
    /// Gọi CẢ 2 overloads moveTo (float,float) + moveTo (Vector2, Action, Action).
    /// Không gọi Player.move(Vector2 direction) - vì đó chỉ dùng cho movement thường.
    /// Phù hợp cho portal-walk vì server nhận packet từ cả 2 methods.
    /// </summary>
    public static bool MoveToPathOnly(float x, float y)
    {
        try
        {
            var p = GetMyPlayer();
            if (p == null) return false;
            bool flag = false;

            // moveTo(float, float) - thường là server packet
            if (_miPlayerMoveTo != null)
            {
                try { _miPlayerMoveTo.Invoke(p, new object[] { x, y }); flag = true; }
                catch { }
            }
            else
            {
                // Resolve lại - fallback khi warmup miss
                try
                {
                    var m = _tMainPlayer?.GetMethod("moveTo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new Type[] { typeof(float), typeof(float) }, null);
                    if (m != null) { m.Invoke(p, new object[] { x, y }); flag = true; }
                }
                catch { }
            }

            // moveTo(Vector2, Action, Action) - thường là client cache + callback
            try
            {
                var mVec = _tMainPlayer?.GetMethod("moveTo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(Vector2), typeof(Action), typeof(Action) }, null);
                if (mVec != null) { mVec.Invoke(p, new object[] { new Vector2(x, y), null, null }); flag = true; }
            }
            catch { }

            return flag;
        }
        catch { return false; }
    }

    // ===== Mobs / Boss =====
    public static List<object> FindAllMobs()
    {
        var result = new List<object>();
        try
        {
            if (!_warmedUp) WarmupTypeCache();
            if (_tMob == null) return result;
            var seen = new HashSet<int>();

            void AddRange(object[] arr)
            {
                if (arr == null) return;
                foreach (var item in arr)
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
                    result.Add(item);
                }
            }

            // Object.FindObjectsOfType<Mob>() - active mobs
            AddRange(Il2CppAPI.FindObjectsOfType(_tMob));

            // Fallback: include inactive / hidden mobs too
            if (result.Count == 0)
                AddRange(Il2CppAPI.FindObjectsOfTypeAll(_tMob));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameAPI] FindAllMobs fail: {ex.Message}");
        }
        return result;
    }

    public static List<object> FindAllNPCs()
    {
        var result = new List<object>();
        try
        {
            if (!_warmedUp) WarmupTypeCache();
            if (_tNPC == null) return result;
            var seen = new HashSet<int>();

            void AddRange(object[] arr)
            {
                if (arr == null) return;
                foreach (var item in arr)
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
                    result.Add(item);
                }
            }

            AddRange(Il2CppAPI.FindObjectsOfType(_tNPC));
            if (result.Count == 0)
                AddRange(Il2CppAPI.FindObjectsOfTypeAll(_tNPC));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameAPI] FindAllNPCs fail: {ex.Message}");
        }
        return result;
    }

    public static string GetMobName(object mob)
    {
        return GetEntityDisplayName(mob);
    }

    public static string GetEntityDisplayName(object entity, IEnumerable<string> preferredPatterns = null)
    {
        if (entity == null) return "";
        try
        {
            var candidates = new List<string>();
            CollectStringCandidates(entity, candidates);

            var info = GetEntityInfo(entity);
            if (info != null && !ReferenceEquals(info, entity))
                CollectStringCandidates(info, candidates);

            string best = PickBestStringCandidate(candidates, entity.GetType().Name, preferredPatterns);
            if (!string.IsNullOrEmpty(best))
                return best;
        }
        catch { }
        return entity.GetType().Name;
    }

    // Cache kết quả đọc HP từ Character.getInfoInBar() theo entity.
    // Không dùng heuristic theo tên obfuscate: ví dụ AENFAHPGCJK chứa "hp" ngẫu nhiên.
    private static readonly Dictionary<Type, (MethodInfo hp, MethodInfo maxHp)> _mobHpMethodCache =
        new Dictionary<Type, (MethodInfo, MethodInfo)>();

    private static bool TryReadHpFromInfoBar(object character, out long curHp, out long maxHp)
    {
        curHp = 0;
        maxHp = 0;
        try
        {
            if (character == null || _miGetInfoInBar == null) return false;

            var infoBar = _miGetInfoInBar.Invoke(character, null);
            if (infoBar == null) return false;

            var type = infoBar.GetType();
            if (_infoBarStringMethodsType == null || !_infoBarStringMethodsType.IsAssignableFrom(type))
            {
                _miInfoBarStringMethods.Clear();
                _infoBarStringMethodsType = type;
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.GetParameters().Length == 0 && method.ReturnType == typeof(string))
                        _miInfoBarStringMethods.Add(method);
                }
            }

            foreach (var method in _miInfoBarStringMethods)
            {
                string value;
                try { value = method.Invoke(infoBar, null) as string; }
                catch { continue; }
                if (string.IsNullOrEmpty(value) || value.IndexOf('/') < 0) continue;

                var match = _infoBarHpPattern.Match(Regex.Replace(value, "<.*?>", string.Empty).Trim());
                if (!match.Success) continue;

                var cur = match.Groups[1].Value.Replace(".", "").Replace(",", "");
                var max = match.Groups[2].Value.Replace(".", "").Replace(",", "");
                if (!long.TryParse(cur, out curHp) || !long.TryParse(max, out maxHp)) continue;
                if (curHp < 0 || maxHp < 100 || curHp > maxHp) continue;
                return true;
            }
        }
        catch { }

        curHp = 0;
        maxHp = 0;
        return false;
    }

    public static int GetMobHp(object mob)
    {
        if (mob == null) return 0;
        try
        {
            // Character.getInfoInBar() là nguồn HP đã được game dùng để vẽ thanh HP.
            if (TryReadHpFromInfoBar(mob, out var hp, out _))
                return hp > int.MaxValue ? int.MaxValue : (int)hp;

            // Chỉ giữ fallback theo tên API rõ ràng; tuyệt đối không đoán tên obfuscate.
            if (ReflectionHelper.TryGetIntMember(mob, out int value, "getHp", "getHP", "hp", "HP", "curHp", "currentHp", "realHp", "health"))
                return value;

            var info = GetEntityInfo(mob);
            if (info != null && ReflectionHelper.TryGetIntMember(info, out value, "getHp", "getHP", "hp", "HP", "curHp", "currentHp", "realHp", "health"))
                return value;
        }
        catch { }
        return 0;
    }

    public static int GetMobMaxHp(object mob)
    {
        if (mob == null) return 0;
        try
        {
            if (TryReadHpFromInfoBar(mob, out _, out var maxHp))
                return maxHp > int.MaxValue ? int.MaxValue : (int)maxHp;

            if (ReflectionHelper.TryGetIntMember(mob, out int value, "getMaxHp", "getHPMax", "maxHp", "MaxHp", "hpMax", "HPMax", "maxHP", "maxHealth"))
                return value;

            var info = GetEntityInfo(mob);
            if (info != null && ReflectionHelper.TryGetIntMember(info, out value, "getMaxHp", "getHPMax", "maxHp", "MaxHp", "hpMax", "HPMax", "maxHP", "maxHealth"))
                return value;
        }
        catch { }
        return 0;
    }

    public static Vector2 GetMobPosition(object mob)
    {
        if (mob == null) return Vector2.zero;
        try
        {
            var mb = mob as MonoBehaviour;
            if (mb != null) return mb.transform.position;
            var t = mob.GetType();
            var xGetter = t.GetMethod("getX", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var yGetter = t.GetMethod("getY", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (xGetter != null && yGetter != null)
                return new Vector2(Convert.ToSingle(xGetter.Invoke(mob, null)), Convert.ToSingle(yGetter.Invoke(mob, null)));
        }
        catch { }
        return Vector2.zero;
    }

    public static bool IsMobAlive(object mob)
    {
        if (mob == null) return false;
        // Primary: HP > 0
        if (GetMobHp(mob) > 0) return true;
        // Fallback: explicit death flags on Mob / MobInfo
        if (TryReadDeathFlag(mob, out bool died)) return !died;
        var info = GetEntityInfo(mob);
        if (info != null && TryReadDeathFlag(info, out died)) return !died;
        // Fallback: HP có thể = 0 trong dying animation nhưng gameObject vẫn active
        try
        {
            var mb = mob as MonoBehaviour;
            if (mb != null) return mb.gameObject.activeInHierarchy;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Lấy Mob.getSelfInfo() trả về thông tin từ template (MobInfo/DIBKOBAIJDH).
    /// Dùng cho boss detection theo pattern Tool_Om_Boss GetMobTypeCompat.
    /// </summary>
    public static object GetMobSelfInfo(object mob)
    {
        if (mob == null) return null;
        try
        {
            var info = GetEntityInfo(mob);
            if (info != null) return info;

            // Fallback: Mob.getSelfInfo() từ type cache cũ
            var m = _tMob?.GetMethod("getSelfInfo",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null) return m.Invoke(mob, null);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Đọc field mobType từ MobInfo (alias "DLDADDIDNPM" của Tool_Om_Boss).
    /// Trả về: -1 = unknown, 1 = normal mob, others = boss/elite type.
    /// </summary>
    public static int GetMobType(object mob)
    {
        try
        {
            var info = GetEntityInfo(mob);
            if (info == null) return -1;
            var t = info.GetType();
            var f = t.GetField("DLDADDIDNPM", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return Convert.ToInt32(f.GetValue(info));
            var p = t.GetProperty("DLDADDIDNPM", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) return Convert.ToInt32(p.GetValue(info, null));
        }
        catch { }
        return -1;
    }

    /// <summary>
    /// Scan fields/properties/methods có tên chứa "boss", "elite", "leader", "isBoss" trả về true.
    /// Pattern từ Tool_Om_Boss IsBossFlagCompat / HasBossLikeFlag.
    /// </summary>
    public static bool HasBossFlag(object obj)
    {
        if (obj == null) return false;
        try
        {
            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            // Fields
            foreach (var f in t.GetFields(flags))
            {
                if (f == null) continue;
                var n = f.Name.ToLowerInvariant();
                if ((n.Contains("boss") || n.Contains("elite") || n.Contains("leader") || n.Contains("isboss"))
                    && TryReadBoolish(f.GetValue(obj)))
                    return true;
            }
            // Properties
            foreach (var p in t.GetProperties(flags))
            {
                if (p == null || p.GetIndexParameters().Length != 0 || !p.CanRead) continue;
                var n = p.Name.ToLowerInvariant();
                if ((n.Contains("boss") || n.Contains("elite") || n.Contains("leader") || n.Contains("isboss"))
                    && TryReadBoolish(p.GetValue(obj, null)))
                    return true;
            }
            // Methods
            foreach (var m in t.GetMethods(flags))
            {
                if (m == null || m.GetParameters().Length != 0) continue;
                var n = m.Name.ToLowerInvariant();
                if ((n.Contains("boss") || n.Contains("elite") || n.Contains("leader") || n.Contains("isboss"))
                    && TryReadBoolish(m.Invoke(obj, null)))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool TryReadBoolish(object value)
    {
        try
        {
            if (value == null) return false;
            if (value is bool b) return b;
            if (value is string s)
            {
                var lower = s.ToLowerInvariant();
                return lower == "true" || lower == "boss" || lower == "elite";
            }
            try { return Convert.ToInt32(value) != 0; } catch { }
        }
        catch { }
        return false;
    }

    public static int GetMobLevel(object mob)
    {
        if (mob == null) return 0;
        try
        {
            if (_miGetInfo == null) return 0;
            var info = _miGetInfo.Invoke(mob, null);
            if (info == null) return 0;
            var infoType = info.GetType();
            // Thử các alias level phổ biến
            foreach (var n in new[] { "getLevel", "GetLevel", "getLv", "level", "lv" })
            {
                var m = infoType.GetMethod(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null && m.GetParameters().Length == 0)
                {
                    var v = m.Invoke(info, null);
                    if (v != null) return Convert.ToInt32(v);
                }
                var f = infoType.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return Convert.ToInt32(f.GetValue(info));
            }
        }
        catch { }
        return 0;
    }

    // ===== Items =====
    public static List<object> FindItemsOnMap()
    {
        var result = new List<object>();
        try
        {
            if (!_warmedUp) WarmupTypeCache();
            if (_tItemMap == null) return result;
            var arr = Il2CppAPI.FindObjectsOfType(_tItemMap);
            foreach (var item in arr) if (item != null) result.Add(item);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameAPI] FindItemsOnMap fail: {ex.Message}");
        }
        return result;
    }

    public static Vector2 GetItemPosition(object item)
    {
        if (item == null) return Vector2.zero;
        try
        {
            // Ưu tiên tọa độ game (getX/getY) - cùng hệ với GetPlayerPosition.
            // transform.position là tọa độ render Unity, khác hệ → di chuyển sai chỗ.
            var t = item.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var mx = t.GetMethod("getX", flags);
            var my = t.GetMethod("getY", flags);
            if (mx != null && my != null && mx.GetParameters().Length == 0 && my.GetParameters().Length == 0)
            {
                float x = Convert.ToSingle(mx.Invoke(item, null));
                float y = Convert.ToSingle(my.Invoke(item, null));
                if (Mathf.Abs(x) > 0.001f || Mathf.Abs(y) > 0.001f)
                    return new Vector2(x, y);
            }

            // Fallback: transform (ít nhất không crash)
            var mb = item as MonoBehaviour;
            if (mb != null) return mb.transform.position;
        }
        catch { }
        return Vector2.zero;
    }

    // ===== Panel helpers =====
    public static object FindCapsulePanel()
    {
        if (!_warmedUp) WarmupTypeCache();
        if (_tCapsulePanel == null) return null;
        try
        {
            // FindObjectsOfTypeAll bắt cả panel đang ẩn (active=false)
            var arr = Il2CppAPI.FindObjectsOfTypeAll(_tCapsulePanel);
            foreach (var item in arr)
            {
                if (item is UnityEngine.Component comp && comp != null && comp.gameObject.activeInHierarchy)
                    return comp;
                if (item is UnityEngine.GameObject go && go != null && go.activeInHierarchy)
                    return go;
            }
            return arr.Length > 0 ? arr[0] : null;
        }
        catch { return null; }
    }

    public static object FindInventoryPanel()
    {
        if (!_warmedUp) WarmupTypeCache();
        if (_tInventoryPanel == null) return null;
        try
        {
            var arr = Il2CppAPI.FindObjectsOfTypeAll(_tInventoryPanel);
            return arr.Length > 0 ? arr[0] : null;
        }
        catch { return null; }
    }

    public static object FindZonePanel()
    {
        if (!_warmedUp) WarmupTypeCache();
        if (_tZonePanel == null) return null;
        try
        {
            // FindObjectsOfTypeAll bắt cả panel đang ẩn
            var arr = Il2CppAPI.FindObjectsOfTypeAll(_tZonePanel);
            foreach (var item in arr)
            {
                if (item is UnityEngine.Component comp && comp != null && comp.gameObject.activeInHierarchy)
                    return comp;
                if (item is UnityEngine.GameObject go && go != null && go.activeInHierarchy)
                    return go;
            }
            return arr.Length > 0 ? arr[0] : null;
        }
        catch { return null; }
    }

    public static object FindDeathPanel()
    {
        if (!_warmedUp) WarmupTypeCache();
        if (_tDeathPanel == null) return null;
        try
        {
            // FindObjectsOfTypeAll để bắt DeathPanel kể cả khi đang ẩn
            var arr = Il2CppAPI.FindObjectsOfTypeAll(_tDeathPanel);
            foreach (var item in arr)
            {
                if (item is UnityEngine.Component comp && comp != null && comp.gameObject.activeInHierarchy)
                    return comp;
                if (item is UnityEngine.GameObject go && go != null && go.activeInHierarchy)
                    return go;
            }
            return arr.Length > 0 ? arr[0] : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Tìm ChangeMap object - cổng dịch chuyển giữa các map (vd: Planet Plant -> Cung).
    /// Có thể là GameObject scene chứa class ChangeMap, hoặc trigger zone.
    /// </summary>
    public static object[] FindChangeMaps()
    {
        if (!_warmedUp) WarmupTypeCache();
        if (_tChangeMap == null) return new object[0];
        try
        {
            // Dùng FindObjectsOfTypeAll để bắt cả object đang ẩn (xuyên map)
            return Il2CppAPI.FindObjectsOfTypeAll(_tChangeMap);
        }
        catch { return new object[0]; }
    }

    // ===== GameManager helpers =====
    public static object GetGameManager()
    {
        if (!_warmedUp) WarmupTypeCache();
        try
        {
            return _miGi?.Invoke(null, null);
        }
        catch { return null; }
    }

    // ===== Public type accessors (cho các class khác dùng) =====
    public static Type TypeGameManager => _tGameManager;
    public static Type TypeMainPlayer => _tMainPlayer;
    public static Type TypeMob => _tMob;
    public static Type TypeNPC => _tNPC;
    public static Type TypeCapsulePanel => _tCapsulePanel;
    public static Type TypeZonePanel => _tZonePanel;
    public static Type TypeItemMap => _tItemMap;

    public static string GetCurrentMapName()
    {
        try
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }
        catch { return ""; }
    }

    /// <summary>
    /// Đọc tên map hiện tại từ MiniMap (TextMeshProUGUI trong MiniMap MonoBehaviour).
    /// Pattern từ Tool_Om_Boss AutoRedRibbon.IsInWildAreaFromMiniMap.
    /// Trả về "" nếu không tìm thấy MiniMap hoặc không có text.
    /// </summary>
    public static string GetCurrentMapFromMiniMap()
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            var tMiniMap = asm?.GetType("MiniMap");
            if (tMiniMap == null) return "";

            var arr = Il2CppAPI.FindObjectsOfTypeAll(tMiniMap);
            if (arr == null || arr.Length == 0) return "";

            var mb = arr[0] as MonoBehaviour;
            if (mb == null) return "";

            // Dùng FindObjectsOfType cho TextMeshProUGUI trong scene
            var tTmpro = typeof(TMPro.TextMeshProUGUI);
            var allTmpro = Il2CppAPI.FindObjectsOfTypeAll(tTmpro);
            foreach (var t in allTmpro)
            {
                var tmp = t as TMPro.TextMeshProUGUI;
                if (tmp == null || string.IsNullOrWhiteSpace(tmp.text)) continue;
                // Check it's child of MiniMap
                if (tmp.transform.IsChildOf(mb.transform) || tmp.transform == mb.transform)
                    return tmp.text.Trim();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameAPI] GetCurrentMapFromMiniMap: {ex.Message}");
        }
        return "";
    }

    /// <summary>
    /// Đọc khu hiện tại từ HUD/MiniMap.
    /// Trả về -1 nếu không xác định được.
    /// </summary>
    public static int GetCurrentZoneIndexFromHUD()
    {
        return ReadZoneFromHUD(out _);
    }

    /// <summary>
    /// Đọc thẳng text nút ZoneObject trên HUD (ví dụ: "▼ Khu Lc 1" hoặc "▼ Khu 3").
    /// Đây là cách đáng tin cậy NHẤT - nút này LUÔN hiển thị, LUÔN đúng, không cần mở panel.
    /// Trả về: true = loạn chiến (text chứa "Lc"), false = khu thường, null = không tìm thấy nút.
    /// </summary>
    public static bool? IsCurrentZoneChaoticFromZoneHUDButton()
    {
        try
        {
            string[] parentNames = { "ZoneObject", "ZoneBtn", "KhuObject", "ZoneBar", "ZoneHUD", "ZoneInfo", "MapZone" };
            var lcRx = new System.Text.RegularExpressions.Regex(@"lc[\s:]*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var allBtnObjects = Il2CppAPI.FindObjectsOfTypeAll(typeof(UnityEngine.UI.Button));
            if (allBtnObjects == null) return null;

            foreach (var obj in allBtnObjects)
            {
                var btn = obj as UnityEngine.UI.Button;
                if (btn == null || btn.gameObject == null) continue;
                if (!btn.gameObject.activeInHierarchy) continue;

                string parentName = btn.transform?.parent?.gameObject?.name ?? "";
                bool matched = false;
                foreach (var pn in parentNames)
                    if (parentName.Equals(pn, StringComparison.OrdinalIgnoreCase)) { matched = true; break; }
                if (!matched) continue;

                string text = UIHelper.GetButtonText(btn);
                if (string.IsNullOrEmpty(text)) continue;

                bool isChaotic = lcRx.IsMatch(text);
                Plugin.Log.LogInfo($"[GameAPI] ZoneHUDButton: text='{text}' → chaotic={isChaotic}");
                return isChaotic;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameAPI] IsCurrentZoneChaoticFromZoneHUDButton: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// <summary>
    /// Detect khu loạn chiến:
    /// 1. ƯU TIÊN: đọc nút HUD "▼ Khu Lc N" (nhanh, không cần mở panel)
    /// 2. Fallback: mở zone panel, đọc tab đang active, rồi đóng lại
    /// Trả về: true = loạn chiến, false = khu thường.
    /// </summary>
    public static bool DetectChaoticByOpeningPanel()
    {
        try
        {
            // ===== BƯỚC 1: ĐỌC NÚT HUD "▼ Khu Lc N" =====
            // Đây là nguồn NHANH NHẤT và ĐÁNG TIN NHẤT - không cần mở panel.
            // Nút này luôn hiển thị trên màn hình game khi đang ở map farm.
            bool? hudResult = IsCurrentZoneChaoticFromZoneHUDButton();
            if (hudResult.HasValue)
            {
                _cachedChaoticZone = hudResult.Value;
                _hasCachedChaoticZone = true;
                Plugin.Log.LogInfo($"[GameAPI] DetectChaoticByOpeningPanel: HUD button → {hudResult.Value}");
                return hudResult.Value;
            }

            // ===== BƯỚC 2: FALLBACK - Mở panel =====
            // Chỉ dùng khi không tìm thấy nút HUD (hiếm gặp).
            bool panelWasAlreadyOpen = IsZonePanelVisible();

            // Mở panel nếu chưa mở
            if (!panelWasAlreadyOpen)
            {
                // Click button ZoneObject trên HUD để mở panel
                bool opened = false;
                try
                {
                    var allBtnObjects = Il2CppAPI.FindObjectsOfTypeAll(typeof(UnityEngine.UI.Button));
                    if (allBtnObjects != null)
                    {
                        string[] parentNames = { "ZoneObject", "ZoneBtn", "KhuObject", "ZoneBar", "ZoneHUD", "ZoneInfo", "MapZone" };
                        foreach (var obj in allBtnObjects)
                        {
                            var btn = obj as UnityEngine.UI.Button;
                            if (btn == null) continue;
                            string parentName = btn.transform?.parent?.gameObject?.name ?? "";
                            bool matched = false;
                            foreach (var pn in parentNames)
                                if (parentName.Equals(pn, StringComparison.OrdinalIgnoreCase)) { matched = true; break; }
                            if (!matched) continue;
                            btn.onClick.Invoke();
                            opened = true;
                            Plugin.Log.LogInfo("[GameAPI] DetectChaoticByOpeningPanel: opened panel via ZoneObject button");
                            break;
                        }
                    }
                }
                catch { }

                // Fallback: gọi Show() trực tiếp
                if (!opened)
                {
                    try
                    {
                        var panelObj = FindZonePanel();
                        if (panelObj != null)
                        {
                            var t = panelObj.GetType();
                            foreach (var name in new[] { "Show", "show", "Open", "open", "showPanel" })
                            {
                                var m = t.GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                if (m == null) continue;
                                var ps = m.GetParameters();
                                if (ps.Length == 0) { m.Invoke(panelObj, null); opened = true; break; }
                                if (ps.Length == 1 && ps[0].ParameterType == typeof(bool)) { m.Invoke(panelObj, new object[] { true }); opened = true; break; }
                            }
                        }
                    }
                    catch { }
                }

                if (!opened)
                {
                    Plugin.Log.LogWarning("[GameAPI] DetectChaoticByOpeningPanel: cannot open panel, fallback to IsCurrentZoneChaotic()");
                    return IsCurrentZoneChaotic();
                }
            }

            // Panel đang mở → đọc tab qua activeInHierarchy (chính xác nhất)
            bool? result = IsChaoticZoneFromPanel();

            // Đóng panel nếu tự mở
            if (!panelWasAlreadyOpen)
            {
                try
                {
                    var panelObj = FindZonePanel();
                    if (panelObj != null)
                    {
                        // Tìm nút đóng trong panel
                        UnityEngine.UI.Button[] btns = System.Array.Empty<UnityEngine.UI.Button>();
                        if (panelObj is Component pc) btns = pc.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                        else if (panelObj is GameObject pg) btns = pg.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                        foreach (var btn in btns)
                        {
                            if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                            string name = btn.gameObject.name ?? "";
                            if (name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                btn.onClick.Invoke();
                                Plugin.Log.LogInfo("[GameAPI] DetectChaoticByOpeningPanel: closed panel");
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            bool isChaotic = result ?? false;
            _cachedChaoticZone = isChaotic;
            _hasCachedChaoticZone = true;
            Plugin.Log.LogInfo($"[GameAPI] DetectChaoticByOpeningPanel: result={isChaotic}");
            return isChaotic;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameAPI] DetectChaoticByOpeningPanel fail: {ex.Message}");
            return IsCurrentZoneChaotic();
        }
    }

    public static bool IsCurrentZoneChaotic()
    {
        // ===== BƯỚC 1: ĐỌC NÚT HUD "▼ Khu Lc N" =====
        // Nguồn chính xác NHẤT - nút này luôn hiển thị, text luôn đúng.
        bool? hudBtn = IsCurrentZoneChaoticFromZoneHUDButton();
        if (hudBtn.HasValue)
        {
            _cachedChaoticZone = hudBtn.Value;
            _hasCachedChaoticZone = true;
            if (hudBtn.Value)
                Plugin.Log.LogInfo("[GameAPI] IsCurrentZoneChaotic: TRUE (HUD button 'Khu Lc N')");
            return hudBtn.Value;
        }

        // ===== BƯỚC 2: Panel buttons (khi panel đang mở) =====
        bool? chaoticFromPanel = IsChaoticZoneFromPanel();
        if (chaoticFromPanel.HasValue)
        {
            _cachedChaoticZone = chaoticFromPanel.Value;
            _hasCachedChaoticZone = true;
            if (chaoticFromPanel.Value)
                Plugin.Log.LogInfo("[GameAPI] IsCurrentZoneChaotic: TRUE (panel button 'Khu Lc N')");
            return chaoticFromPanel.Value;
        }

        // ===== BƯỚC 3: HUD text scan (ReadZoneFromHUD) =====
        ReadZoneFromHUD(out bool chaoticFromHud);
        if (chaoticFromHud)
        {
            _cachedChaoticZone = true;
            _hasCachedChaoticZone = true;
            Plugin.Log.LogInfo("[GameAPI] IsCurrentZoneChaotic: TRUE (HUD text scan)");
            return true;
        }

        // ===== BƯỚC 4: Cache =====
        if (_hasCachedChaoticZone)
        {
            Plugin.Log.LogInfo($"[GameAPI] IsCurrentZoneChaotic: using cached = {_cachedChaoticZone}");
            return _cachedChaoticZone;
        }

        return false;
    }

    /// <summary>
    /// Check panel đổi khu có button "Khu Lc N" nào đang active/selected không.
    /// </summary>
    /// <summary>
    /// Check panel đổi khu xem đang ở tab loạn chiến hay thường.
    /// Trả về: true = loạn chiến, false = thường, null = không xác định được (panel không tồn tại).
    ///
    /// FIX: Code cũ chỉ check activeInHierarchy → khi panel ĐÓNG, tất cả button
    /// đều có activeInHierarchy=false → luôn trả false → SaveCurrentFarmContext()
    /// luôn lưu tab=Normal → restore về sai khu.
    ///
    /// Code mới: Khi panel đóng (hidden), dùng activeSelf để phân biệt:
    /// - Button "Khu Lc N" có activeSelf=true → tab loạn chiến đang được chọn
    /// - Chỉ có button "Khu N" (normal) activeSelf=true → tab thường
    /// </summary>
    private static bool? IsChaoticZoneFromPanel()
    {
        try
        {
            var panelObj = FindZonePanel();
            if (panelObj == null) return null;

            UnityEngine.UI.Button[] buttons = System.Array.Empty<UnityEngine.UI.Button>();
            if (panelObj is Component panelComp)
                buttons = panelComp.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            else if (panelObj is GameObject panelGo)
                buttons = panelGo.GetComponentsInChildren<UnityEngine.UI.Button>(true);

            if (buttons.Length == 0) return null;

            // Panel đang hiển thị (activeInHierarchy) hay đang ẩn?
            bool panelVisible = false;
            try
            {
                if (panelObj is Component pc) panelVisible = pc.gameObject.activeInHierarchy;
                else if (panelObj is GameObject pg) panelVisible = pg.activeInHierarchy;
            }
            catch { }

            var lcRx = new System.Text.RegularExpressions.Regex(@"(?:khu\s+)?lc[:\s]*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var normalRx = new System.Text.RegularExpressions.Regex(@"khu[:\s]*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            bool hasLcButton = false;
            bool hasNormalButton = false;

            foreach (var btn in buttons)
            {
                if (btn == null || btn.gameObject == null) continue;

                // Khi panel HIỆN: dùng activeInHierarchy (chính xác nhất)
                // Khi panel ẨN: dùng activeSelf (button của tab đang chọn sẽ có activeSelf=true)
                bool isRelevant = panelVisible
                    ? btn.gameObject.activeInHierarchy
                    : btn.gameObject.activeSelf;

                if (!isRelevant) continue;

                string text = UIHelper.GetButtonText(btn);
                if (string.IsNullOrEmpty(text)) continue;

                if (lcRx.IsMatch(text))
                {
                    hasLcButton = true;
                    Plugin.Log.LogInfo($"[GameAPI] IsChaoticZoneFromPanel: found '{text}' (panelVisible={panelVisible}, activeSelf={btn.gameObject.activeSelf})");
                }
                else if (normalRx.IsMatch(text))
                {
                    hasNormalButton = true;
                }
            }

            // Nếu tìm thấy cả 2 loại button (hiếm, có thể do activeSelf không đủ phân biệt tab)
            // → ưu tiên Lc nếu số Lc button < số Normal (panel thường có ít khu Lc hơn Normal)
            if (hasLcButton)
            {
                Plugin.Log.LogInfo($"[GameAPI] IsChaoticZoneFromPanel: Lc buttons found → TRUE (panelVisible={panelVisible})");
                return true;
            }

            if (hasNormalButton)
            {
                return false;
            }

            // Không tìm thấy zone button nào → không xác định
            return null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[GameAPI] IsChaoticZoneFromPanel: {ex.Message}");
        }

        return null;
    }



    /// <summary>
    /// Đọc khu hiện tại + loại khu (thường / loạn chiến) từ HUD.
    ///
    /// CẢNH BÁO - bug đã từng xảy ra: regex cũ là "khu[:\s]*(\d+)" nên text HUD
    /// "Khu Lc 0" KHÔNG match ("Lc" chen giữa) → trả -1 → tool không lưu được khu
    /// loạn chiến, và sau khi săn boss thì restore về sai khu.
    /// </summary>
    private static int ReadZoneFromHUD(out bool isChaotic)
    {
        isChaotic = false;
        try
        {
            // (lc) là optional group: "Khu 3" → group1 rỗng; "Khu Lc 0" → group1="Lc".
            var zoneRx = new Regex(@"khu[:\s]*(lc)?[:\s]*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var candidates = new List<(int Zone, bool Chaotic, int Score, string Text, string Path)>();


            bool IsHudZonePath(string path)
            {
                if (string.IsNullOrEmpty(path)) return false;
                string lower = path.ToLowerInvariant();
                if (lower.Contains("zonepanel") || lower.Contains("zonemenu") || lower.Contains("changezone"))
                    return false;
                if (lower.Contains("capsulepanel") || lower.Contains("settingmenu") || lower.Contains("inventorypanel") ||
                    lower.Contains("deathpanel") || lower.Contains("chat") || lower.Contains("notify") || lower.Contains("popup"))
                    return false;
                return lower.Contains("minimap") || lower.Contains("mapname") || lower.Contains("objectmapinfobarmanager") ||
                       lower.Contains("zoneobject") || lower.Contains("hudcanvas");
            }

            int ScoreHudZonePath(string path, string text)
            {
                if (string.IsNullOrEmpty(path)) return 0;
                string lower = path.ToLowerInvariant();
                int score = 0;
                if (lower.Contains("minimap")) score += 1000;
                if (lower.Contains("mapname")) score += 900;
                if (lower.Contains("objectmapinfobarmanager")) score += 800;
                if (lower.Contains("zoneobject")) score += 700;
                if (lower.Contains("hudcanvas")) score += 600;
                if (!string.IsNullOrEmpty(text) && text.IndexOf("Khu", StringComparison.OrdinalIgnoreCase) >= 0) score += 100;
                if (!string.IsNullOrEmpty(text) && text.IndexOf(':') >= 0) score += 10;
                if (!string.IsNullOrEmpty(text) && text.Trim().Length <= 8) score += 10;
                return score;
            }

            void ScanTextObject(object obj, bool isTmp)
            {
                try
                {
                    if (obj == null) return;
                    var comp = obj as Component;
                    if (comp == null || comp.gameObject == null || !comp.gameObject.activeInHierarchy) return;

                    string text = "";
                    if (isTmp)
                    {
                        var tmp = obj as TMPro.TextMeshProUGUI;
                        if (tmp != null) text = tmp.text;
                    }
                    else
                    {
                        var uiText = obj as UnityEngine.UI.Text;
                        if (uiText != null) text = uiText.text;
                    }

                    if (string.IsNullOrWhiteSpace(text)) return;
                    Match m = zoneRx.Match(text);
                    // group(1) = "lc" (optional), group(2) = digits
                    if (!m.Success || !int.TryParse(m.Groups[2].Value, out int zone)) return;
                    bool chaotic = m.Groups[1].Success && m.Groups[1].Value.Length > 0;

                    string path = UIHelper.GetTransformPath(comp.transform);
                    if (!IsHudZonePath(path)) return;

                    candidates.Add((zone, chaotic, ScoreHudZonePath(path, text), text.Trim(), path));
                }
                catch { }
            }

            var texts = Il2CppAPI.FindObjectsOfTypeAll(typeof(UnityEngine.UI.Text));
            foreach (var obj in texts) ScanTextObject(obj, false);

            var tmps = Il2CppAPI.FindObjectsOfTypeAll(typeof(TMPro.TextMeshProUGUI));
            foreach (var obj in tmps) ScanTextObject(obj, true);

            if (candidates.Count == 0) return -1;
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            isChaotic = candidates[0].Chaotic;
            var winner = candidates[0];
            // Chỉ log khi zone ĐỔI hoặc mỗi 5s — hàm này gọi mỗi tick, log mọi lần làm ngập LogOutput
            if (winner.Zone != _lastLoggedZone || Time.time - _lastZoneLogAt >= 5f)
            {
                _lastLoggedZone = winner.Zone;
                _lastZoneLogAt = Time.time;
                Plugin.Log.LogInfo($"[GameAPI] ReadZoneFromHUD: zone={winner.Zone}, chaotic={winner.Chaotic}, text='{winner.Text}', score={winner.Score}");
            }
            return candidates[0].Zone;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GameAPI] ReadZoneFromHUD: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Đang ở map có tên chứa <paramref name="mapName"/> hay không.
    ///
    /// CẢNH BÁO - bug đã từng xảy ra: bản cũ quét MỌI text UI đang active để tìm
    /// substring. Thông báo boss "... vừa xuất hiện tại Cung Điện Đổ Nát" hiện trên
    /// HUD cũng chứa "Cung", nên IsInMap("Cung") trả về true khi nhân vật vẫn đang ở
    /// Sa Mạc → tool tưởng đã tới map boss, nhảy sang ZoneScanLoop và quay vòng đổi khu
    /// ở map sai. Vì vậy MiniMap là nguồn QUYẾT ĐỊNH: đọc được minimap thì tin nó,
    /// KHÔNG fallback xuống quét text (fallback chính là chỗ sinh ra false positive).
    /// </summary>
    public static bool IsInMap(string mapName)
    {
        try
        {
            if (string.IsNullOrEmpty(mapName)) return false;

            // 1) MiniMap = tên map thật đang đứng. Đọc được thì đây là câu trả lời cuối cùng.
            string minimap = GetCurrentMapFromMiniMap();
            if (!string.IsNullOrEmpty(minimap))
                return minimap.IndexOf(mapName, StringComparison.OrdinalIgnoreCase) >= 0;

            // 2) Không có minimap → thử tên scene Unity.
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(sceneName) &&
                sceneName.IndexOf(mapName, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // 3) Fallback cuối: quét text UI, nhưng CHỈ text nằm ở vùng hiển thị tên map
            // trên HUD. Panel menu đang mở thì không tin gì cả.
            if (IsCapsulePanelVisible() || IsZonePanelVisible() || IsChangeMapPanelVisible())
                return false;

            foreach (var obj in Il2CppAPI.FindObjectsOfTypeAll(typeof(UnityEngine.UI.Text)))
            {
                var t = obj as UnityEngine.UI.Text;
                if (t == null || string.IsNullOrEmpty(t.text) || !t.gameObject.activeInHierarchy) continue;
                if (!IsMapNameHudPath(UIHelper.GetTransformPath(t.transform))) continue;
                if (t.text.IndexOf(mapName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            foreach (var obj in Il2CppAPI.FindObjectsOfTypeAll(typeof(TMPro.TextMeshProUGUI)))
            {
                var t = obj as TMPro.TextMeshProUGUI;
                if (t == null || string.IsNullOrEmpty(t.text) || !t.gameObject.activeInHierarchy) continue;
                if (!IsMapNameHudPath(UIHelper.GetTransformPath(t.transform))) continue;
                if (t.text.IndexOf(mapName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Text này có nằm ở ô hiển thị tên map trên HUD không.
    /// Loại thẳng chat / thông báo / popup - đó là nơi thông báo boss xuất hiện,
    /// chứa tên map nhưng KHÔNG có nghĩa là đang ở map đó.
    /// </summary>
    private static bool IsMapNameHudPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string lower = path.ToLowerInvariant();

        if (lower.Contains("chat") || lower.Contains("notify") || lower.Contains("notification") ||
            lower.Contains("popup") || lower.Contains("message") || lower.Contains("announce") ||
            lower.Contains("toast") || lower.Contains("tooltip") ||
            lower.Contains("zonepanel") || lower.Contains("zonemenu") || lower.Contains("changezone") ||
            lower.Contains("capsulepanel") || lower.Contains("inventorypanel") || lower.Contains("deathpanel"))
            return false;

        return lower.Contains("minimap") || lower.Contains("mapname") ||
               lower.Contains("objectmapinfobarmanager") || lower.Contains("mapinfo");
    }



    // ===== Panel visibility checks (cheap, dùng để tránh false-positive khi quét text) =====
    private static bool IsPanelActive(Type panelType)
    {
        if (panelType == null) return false;
        try
        {
            var arr = Il2CppAPI.FindObjectsOfTypeAll(panelType);
            foreach (var obj in arr)
            {
                if (obj is UnityEngine.Component comp && comp != null && comp.gameObject.activeInHierarchy)
                    return true;
                if (obj is UnityEngine.GameObject go && go != null && go.activeInHierarchy)
                    return true;
            }
        }
        catch { }
        return false;
    }

    public static bool IsCapsulePanelVisible() => IsPanelActive(_tCapsulePanel);
    public static bool IsZonePanelVisible() => IsPanelActive(_tZonePanel);
    public static bool IsChangeMapPanelVisible() => IsPanelActive(_tChangeMap);



    /// <summary>
    /// Heuristic: scan tất cả no-arg int/long methods trên type, invoke với instance,
    /// chọn cặp (hp, maxHp) sao cho: cả hai > 0, hp &lt;= maxHp.
    /// Nếu không có instance thì dùng tên chứa "hp" để gợi ý.
    /// Chỉ gọi 1 lần rồi cache bởi caller.
    /// </summary>
    internal static void TryFindHpMethodsHeuristic(
        Type type, out MethodInfo hpMethod, out MethodInfo maxHpMethod,
        object instance = null)
    {
        hpMethod = null;
        maxHpMethod = null;
        if (type == null) return;
        try
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var methods = type.GetMethods(F);

            if (instance != null)
            {
                // Debug log các Fields
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => f.FieldType == typeof(int) || f.FieldType == typeof(long));
                foreach (var f in fields)
                {
                    try
                    {
                        var raw = f.GetValue(instance);
                        if (raw != null)
                        {
                            long fVal = Convert.ToInt64(raw);
                            if (fVal > 0) Plugin.Log?.LogInfo($"[HeuristicDump] FIELD {type.Name}.{f.Name} = {fVal}");
                        }
                    }
                    catch { }
                }

                // Debug log các Properties
                var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(p => p.CanRead && (p.PropertyType == typeof(int) || p.PropertyType == typeof(long)) && p.GetIndexParameters().Length == 0);
                foreach (var p in props)
                {
                    try
                    {
                        var raw = p.GetValue(instance);
                        if (raw != null)
                        {
                            long pVal = Convert.ToInt64(raw);
                            if (pVal > 0) Plugin.Log?.LogInfo($"[HeuristicDump] PROP {type.Name}.{p.Name} = {pVal}");
                        }
                    }
                    catch { }
                }
            }

            // Bước 1: thu thập tất cả no-arg int/long candidates, filter bỏ method không liên quan
            var candidates = new List<(MethodInfo m, long val, bool nameHasHp)>();
            foreach (var m in methods)
            {
                if (m.GetParameters().Length != 0) continue;
                if (m.ReturnType != typeof(int) && m.ReturnType != typeof(long)) continue;

                var nm = m.Name.ToLowerInvariant();
                // Loại bỏ rõ ràng các field không liên quan HP
                if (nm.Contains("mapid") || nm.Contains("zoneid") || nm.Contains("serverid") ||
                    nm.Contains("charid") || nm.Contains("itemid") || nm.Contains("skilllevel") ||
                    nm.Contains("typeid") || nm.Contains("questid") || nm.Contains("npcid") ||
                    nm.Contains("mobid") || nm.Contains("gold") || nm.Contains("silver") ||
                    nm.Contains("hashcode") || nm.Contains("gethashcode"))
                    continue;

                bool nameHasHp = nm.Contains("hp") || nm.Contains("health") || nm.Contains("life");
                long val = 0;

                if (instance != null)
                {
                    try
                    {
                        var raw = m.Invoke(instance, null);
                        if (raw == null) continue;
                        val = Convert.ToInt64(raw);
                        
                        // Debug log TẤT CẢ các method trả về số để tìm ra method HP thật sự
                        if (val > 0)
                        {
                            Plugin.Log?.LogInfo($"[HeuristicDump] METHOD {type.Name}.{m.Name}() = {val}");
                        }
                        
                        if (val <= 0) continue; // loại bỏ method trả về giá trị âm hoặc 0
                    }
                    catch { continue; }
                }

                candidates.Add((m, val, nameHasHp));
            }

            if (candidates.Count == 0) return;

            // Bước 2: nếu có instance, tìm cặp (hp, maxHp) dựa trên giá trị
            if (instance != null && candidates.Count >= 2)
            {
                var validPairs = new List<(MethodInfo hp, MethodInfo max, long hpVal, long maxVal, bool nameHp, double ratio)>();

                for (int i = 0; i < candidates.Count; i++)
                {
                    for (int j = 0; j < candidates.Count; j++)
                    {
                        if (i == j) continue;
                        var lo = candidates[i];
                        var hi = candidates[j];
                        
                        if (lo.val <= 0 || hi.val <= 0) continue;
                        if (lo.val > hi.val) continue; // hp must be <= maxHp

                        double ratio = (double)lo.val / hi.val;
                        if (ratio < 0.01 || ratio > 1.0) continue;

                        // Chấp nhận nếu maxHp khá lớn (> 10) để tránh các enum properties (như Dir, State, Type = 1, 2, 3...)
                        if (hi.val <= 10 && !lo.nameHasHp && !hi.nameHasHp) continue;

                        validPairs.Add((lo.m, hi.m, lo.val, hi.val, lo.nameHasHp || hi.nameHasHp, ratio));
                    }
                }

                if (validPairs.Count > 0)
                {
                    // CHỈ chấp nhận cặp có tên chứa "hp"/"health"/"life".
                    // Không đoán mò theo giá trị: trước đây logic "maxval-based" đã chọn nhầm
                    // getSpaceShipId()/getSkinId() làm HP -> retreat sai hoàn toàn.
                    var namedPairs = validPairs.Where(p => p.nameHp).ToList();
                    if (namedPairs.Count > 0)
                    {
                        // Nếu có nhiều cặp, ưu tiên cặp có maxHp lớn nhất
                        var best = namedPairs.OrderByDescending(p => p.maxVal).First();
                        hpMethod = best.hp;
                        maxHpMethod = best.max;
                        Plugin.Log?.LogWarning($"[GameAPI] Heuristic found (name+val): {type.Name}.{hpMethod.Name}()={best.hpVal} / {type.Name}.{maxHpMethod.Name}()={best.maxVal}");
                        return;
                    }
                }

                // Không có cặp nào tên giống HP -> BỎ CUỘC (an toàn hơn là đoán sai).
                Plugin.Log?.LogWarning(
                    $"[GameAPI] Heuristic on {type.Name}: no HP-named pair found, refusing to guess (tránh chọn nhầm getSkinId/getSpaceShipId).");
                return;
            }

            // Bước 3: không có instance — dùng tên
            var hpNamed = candidates.Where(c => c.nameHasHp).ToList();
            if (hpNamed.Count >= 2)
            {
                hpMethod = hpNamed[0].m;
                maxHpMethod = hpNamed[1].m;
            }
            else if (hpNamed.Count == 1)
            {
                hpMethod = hpNamed[0].m;
                var others = candidates.Where(c => !c.nameHasHp).ToList();
                maxHpMethod = others.Count > 0 ? others[0].m : null;
            }

            if (hpMethod != null)
                Plugin.Log?.LogWarning(
                    $"[GameAPI] Heuristic HP candidate (name-only): {type.Name}.{hpMethod.Name}() / {type.Name}.{maxHpMethod?.Name}()");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[GameAPI] TryFindHpMethodsHeuristic fail: {ex.Message}");
        }
    }

    private static bool TryReadDeathFlag(object obj, out bool died)
    {
        died = false;
        if (obj == null) return false;
        try
        {
            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in new[] { "isDied", "IsDied", "isDead", "IsDead", "isLocalDied", "IsLocalDied" })
            {
                var m = t.GetMethod(name, flags);
                if (m != null && m.GetParameters().Length == 0)
                {
                    died = TryReadBoolish(m.Invoke(obj, null));
                    return true;
                }

                var p = t.GetProperty(name, flags);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                {
                    died = TryReadBoolish(p.GetValue(obj, null));
                    return true;
                }

                var f = t.GetField(name, flags);
                if (f != null)
                {
                    died = TryReadBoolish(f.GetValue(obj));
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static object GetEntityInfo(object entity)
    {
        if (entity == null) return null;
        try
        {
            var t = entity.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in new[] { "getInfo", "getSelfInfo", "getData" })
            {
                var m = t.GetMethod(name, flags);
                if (m != null && m.GetParameters().Length == 0)
                {
                    var v = m.Invoke(entity, null);
                    if (v != null) return v;
                }
            }
        }
        catch { }
        return null;
    }

    private static void CollectStringCandidates(object obj, List<string> candidates)
    {
        if (obj == null || candidates == null) return;
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var mi in t.GetMethods(flags))
            {
                if (mi == null || mi.GetParameters().Length != 0 || mi.ReturnType != typeof(string)) continue;
                if (mi.Name == nameof(ToString)) continue;
                try { AddStringCandidate(mi.Invoke(obj, null), candidates, seen); } catch { }
            }

            foreach (var p in t.GetProperties(flags))
            {
                if (p == null || !p.CanRead || p.GetIndexParameters().Length != 0 || p.PropertyType != typeof(string)) continue;
                try { AddStringCandidate(p.GetValue(obj, null), candidates, seen); } catch { }
            }

            foreach (var f in t.GetFields(flags))
            {
                if (f == null || f.FieldType != typeof(string)) continue;
                try { AddStringCandidate(f.GetValue(obj), candidates, seen); } catch { }
            }
        }
        catch { }
    }

    private static void AddStringCandidate(object value, List<string> candidates, HashSet<string> seen)
    {
        if (value == null) return;
        string s;
        try { s = value.ToString()?.Trim(); }
        catch { return; }
        if (string.IsNullOrEmpty(s)) return;
        if (s.StartsWith("System.", StringComparison.OrdinalIgnoreCase)) return;
        if (seen != null && !seen.Add(s)) return;
        candidates.Add(s);
    }

    private static string PickBestStringCandidate(List<string> candidates, string defaultTypeName, IEnumerable<string> preferredPatterns)
    {
        if (candidates == null || candidates.Count == 0) return "";

        string best = "";
        int bestScore = int.MinValue;

        foreach (var raw in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string s = raw.Trim();
            int score = ScoreStringCandidate(s, defaultTypeName, preferredPatterns);
            if (score > bestScore)
            {
                bestScore = score;
                best = s;
            }
        }

        return bestScore > int.MinValue / 2 ? best : "";
    }

    private static int ScoreStringCandidate(string s, string defaultTypeName, IEnumerable<string> preferredPatterns)
    {
        if (string.IsNullOrWhiteSpace(s)) return int.MinValue;

        string lower = s.Trim().ToLowerInvariant();
        string defaultLower = (defaultTypeName ?? "").Trim().ToLowerInvariant();
        if (lower.Length == 0) return int.MinValue;
        if (lower.StartsWith("system.")) return int.MinValue;

        int score = s.Length;
        if (s.Length <= 2) score -= 1000;
        if (s.Equals(defaultTypeName, StringComparison.OrdinalIgnoreCase)) score -= 1000;
        if (lower == "mob" || lower == "npc" || lower == "character" || lower == "player" || lower == "enemy")
            score -= 800;
        if (lower == "null" || lower == "true" || lower == "false")
            score -= 1000;
        if (s.IndexOf(' ') >= 0) score += 40;
        if (s.Any(ch => ch > 127)) score += 25;
        if (s.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0) score += 400;
        if (preferredPatterns != null)
        {
            foreach (var pattern in preferredPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                if (s.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 5000;
                    break;
                }
            }
        }
        if (!string.IsNullOrEmpty(defaultLower) && lower.Contains(defaultLower) && lower != defaultLower)
            score -= 100;
        return score;
    }

    // ===================================================================
    // Type / singleton lookup (dùng cho BossSkillManager tìm input handler)
    // ===================================================================

    private static readonly Dictionary<string, Type> _typeNameCache = new Dictionary<string, Type>();

    /// <summary>Tìm Type theo tên trong Assembly-CSharp (cache kết quả, kể cả khi null).</summary>
    public static Type FindTypeByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_typeNameCache.TryGetValue(name, out var cached)) return cached;

        Type found = null;
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            found = asm?.GetType(name);
        }
        catch { }

        _typeNameCache[name] = found;
        return found;
    }

    /// <summary>
    /// Lấy singleton instance của một type: thử static method gI()/Instance/instance,
    /// fallback sang FindObjectsOfType nếu là MonoBehaviour.
    /// </summary>
    public static object GetSingleton(Type t)
    {
        if (t == null) return null;
        try
        {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var name in new[] { "gI", "Instance", "instance", "GetInstance", "getInstance" })
            {
                var m = t.GetMethod(name, flags);
                if (m != null && m.GetParameters().Length == 0)
                {
                    var v = m.Invoke(null, null);
                    if (v != null) return v;
                }

                var p = t.GetProperty(name, flags);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                {
                    var v = p.GetValue(null, null);
                    if (v != null) return v;
                }

                var f = t.GetField(name, flags);
                if (f != null)
                {
                    var v = f.GetValue(null);
                    if (v != null) return v;
                }
            }

            // Fallback: scene lookup cho MonoBehaviour không expose singleton
            var objs = Il2CppAPI.FindObjectsOfType(t);
            if (objs != null && objs.Length > 0) return objs[0];
        }
        catch { }
        return null;
    }

    // ===== Item API (Reflection Heuristics) =====
    private static Type _tService = null;
    private static MethodInfo _miServiceGi = null;
    private static MethodInfo _miServiceUseItem = null;
    private static bool _itemHeuristicWarmedUp = false;

    private static void WarmupItemHeuristics()
    {
        if (_itemHeuristicWarmedUp) return;
        _itemHeuristicWarmedUp = true;
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (asm == null) return;

            foreach (var type in asm.GetTypes())
            {
                var giMethod = type.GetMethod("gI", BindingFlags.Public | BindingFlags.Static);
                if (giMethod != null && giMethod.ReturnType == type)
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var p = method.GetParameters();
                        if (p.Length == 4 && 
                            p[0].ParameterType == typeof(sbyte) &&
                            p[1].ParameterType == typeof(sbyte) &&
                            p[2].ParameterType == typeof(sbyte) &&
                            p[3].ParameterType == typeof(short))
                        {
                            _tService = type;
                            _miServiceGi = giMethod;
                            _miServiceUseItem = method;
                            Plugin.Log?.LogInfo($"[GameAPI] Found Service.useItem heuristic: {type.Name}.{method.Name}");
                            break;
                        }
                    }
                    if (_tService != null) break;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[GameAPI] WarmupItemHeuristics fail: {ex.Message}");
        }
    }

    /// <summary>
    /// Cache: lưu lại member đã tìm thấy có chứa templateId cho type cụ thể.
    /// Key = type name, Value = (memberOwnerGetter, memberInfo) — null nếu chưa scan.
    /// </summary>
    private static readonly Dictionary<string, (Func<object, object> getOwner, MemberInfo member, string debugPath)?> _itemIdMemberCache
        = new Dictionary<string, (Func<object, object>, MemberInfo, string)?>();

    private static bool TryReadItemId(object item, out int itemId)
    {
        itemId = -1;
        if (item == null) return false;

        // 1. Check named members trước (nhanh nếu game không obfuscate)
        if (ReflectionHelper.TryGetIntMember(item, out itemId, "templateId", "templateID", "id", "itemId", "item_id", "ItemID", "TemplateID", "key", "Key")) 
        {
            if (itemId > 0) return true;
        }

        // 2. Check deep named members (sub-object.templateId)
        foreach (var p in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.PropertyType.IsPrimitive && p.PropertyType != typeof(string))
            {
                try {
                    var subObj = p.GetValue(item);
                    if (subObj != null && ReflectionHelper.TryGetIntMember(subObj, out itemId, "templateId", "templateID", "id", "itemId", "item_id", "ItemID", "TemplateID", "type")) 
                    {
                        if (itemId > 0) return true;
                    }
                } catch {}
            }
        }
        foreach (var f in item.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!f.FieldType.IsPrimitive && f.FieldType != typeof(string))
            {
                try {
                    var subObj = f.GetValue(item);
                    if (subObj != null && ReflectionHelper.TryGetIntMember(subObj, out itemId, "templateId", "templateID", "id", "itemId", "item_id", "ItemID", "TemplateID", "type")) 
                    {
                        if (itemId > 0) return true;
                    }
                } catch {}
            }
        }

        // 3. HEURISTIC: game obfuscated — quét tất cả int-returning members
        return TryReadItemIdHeuristic(item, out itemId);
    }

    /// <summary>
    /// Heuristic: quét tất cả no-arg int/short-returning members trên item và sub-objects.
    /// Chọn member trả về giá trị dương trong range 1-99999 (typical template ID).
    /// Cache kết quả theo type để lần sau không quét lại.
    /// </summary>
    private static bool TryReadItemIdHeuristic(object item, out int itemId)
    {
        itemId = -1;
        if (item == null) return false;
        var typeName = item.GetType().FullName ?? item.GetType().Name;

        // Check cache
        if (_itemIdMemberCache.TryGetValue(typeName, out var cached))
        {
            if (cached == null) return false; // đã scan, không tìm thấy
            try
            {
                var owner = cached.Value.getOwner(item);
                if (owner != null)
                {
                    var val = ReadMemberAsInt(owner, cached.Value.member);
                    if (val.HasValue && val.Value > 0)
                    {
                        itemId = val.Value;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // Scan: tìm int member trên item trực tiếp
        var directResult = ScanForIntIdMember(item);
        if (directResult.HasValue)
        {
            _itemIdMemberCache[typeName] = (o => o, directResult.Value.member, $"direct.{directResult.Value.member.Name}");
            itemId = directResult.Value.value;
            return true;
        }

        // Scan: tìm int member trên sub-objects (item.XXXXX.someIntMember)
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var prop in item.GetType().GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (prop.PropertyType.IsPrimitive || prop.PropertyType == typeof(string) || prop.PropertyType.IsEnum) continue;
            if (typeof(UnityEngine.Object).IsAssignableFrom(prop.PropertyType)) continue;
            try
            {
                var subObj = prop.GetValue(item);
                if (subObj == null) continue;
                var subResult = ScanForIntIdMember(subObj);
                if (subResult.HasValue)
                {
                    var propCapture = prop;
                    _itemIdMemberCache[typeName] = (o => propCapture.GetValue(o), subResult.Value.member, $"{prop.Name}.{subResult.Value.member.Name}");
                    itemId = subResult.Value.value;
                    Plugin.Log?.LogInfo($"[GameAPI] TryReadItemId heuristic: locked {prop.Name}.{subResult.Value.member.Name} = {itemId} for type {typeName}");
                    return true;
                }
            }
            catch { }
        }

        foreach (var field in item.GetType().GetFields(flags))
        {
            if (field.FieldType.IsPrimitive || field.FieldType == typeof(string) || field.FieldType.IsEnum) continue;
            if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
            try
            {
                var subObj = field.GetValue(item);
                if (subObj == null) continue;
                var subResult = ScanForIntIdMember(subObj);
                if (subResult.HasValue)
                {
                    var fieldCapture = field;
                    _itemIdMemberCache[typeName] = (o => fieldCapture.GetValue(o), subResult.Value.member, $"{field.Name}.{subResult.Value.member.Name}");
                    itemId = subResult.Value.value;
                    Plugin.Log?.LogInfo($"[GameAPI] TryReadItemId heuristic: locked {field.Name}.{subResult.Value.member.Name} = {itemId} for type {typeName}");
                    return true;
                }
            }
            catch { }
        }

        // Không tìm thấy — cache null để không quét lại
        _itemIdMemberCache[typeName] = null;
        return false;
    }

    /// <summary>Quét tất cả no-arg int/short/long-returning members trên object.
    /// Trả về member có giá trị dương trong range item ID hợp lệ (1-99999).</summary>
    private static (MemberInfo member, int value)? ScanForIntIdMember(object obj)
    {
        if (obj == null) return null;
        var t = obj.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Thu thập tất cả candidates
        var candidates = new List<(MemberInfo member, int value)>();

        foreach (var m in t.GetMethods(flags))
        {
            if (m.GetParameters().Length != 0) continue;
            if (!IsIntLikeReturn(m.ReturnType)) continue;
            if (m.IsSpecialName) continue; // skip property getters
            try
            {
                var v = m.Invoke(obj, null);
                if (v != null)
                {
                    int intVal = Convert.ToInt32(v);
                    if (intVal > 0 && intVal < 100000000)
                        candidates.Add((m, intVal));
                }
            }
            catch { }
        }

        foreach (var p in t.GetProperties(flags))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (!IsIntLikeReturn(p.PropertyType)) continue;
            try
            {
                var v = p.GetValue(obj);
                if (v != null)
                {
                    int intVal = Convert.ToInt32(v);
                    if (intVal > 0 && intVal < 100000000)
                        candidates.Add((p, intVal));
                }
            }
            catch { }
        }

        foreach (var f in t.GetFields(flags))
        {
            if (!IsIntLikeReturn(f.FieldType)) continue;
            try
            {
                var v = f.GetValue(obj);
                if (v != null)
                {
                    int intVal = Convert.ToInt32(v);
                    if (intVal > 0 && intVal < 100000000)
                        candidates.Add((f, intVal));
                }
            }
            catch { }
        }

        if (candidates.Count == 0) return null;

        // Lọc bỏ hashCode, next (thường là struct Dictionary Entry)
        candidates.RemoveAll(c => 
            c.member.Name.Equals("hashCode", StringComparison.OrdinalIgnoreCase) || 
            c.member.Name.Equals("hash", StringComparison.OrdinalIgnoreCase) || 
            c.member.Name.Equals("next", StringComparison.OrdinalIgnoreCase));

        if (candidates.Count == 0) return null;

        // Ưu tiên: member có tên chứa "id", "template", "type", "key" (case insensitive)
        foreach (var c in candidates)
        {
            string name = c.member.Name.ToLowerInvariant();
            if (name.Contains("id") || name.Contains("template") || name.Contains("type") || name.Contains("key"))
                return c;
        }

        // Fallback: trả về candidate đầu tiên (thường là member đầu tiên trong struct — templateId)
        return candidates[0];
    }

    private static bool IsIntLikeReturn(Type t)
    {
        return t == typeof(int) || t == typeof(short) || t == typeof(long)
            || t == typeof(Int16) || t == typeof(Int32) || t == typeof(Int64)
            || t == typeof(ushort) || t == typeof(uint);
    }

    private static int? ReadMemberAsInt(object owner, MemberInfo member)
    {
        try
        {
            object v = null;
            if (member is MethodInfo mi) v = mi.Invoke(owner, null);
            else if (member is PropertyInfo pi) v = pi.GetValue(owner);
            else if (member is FieldInfo fi) v = fi.GetValue(owner);
            if (v != null) return Convert.ToInt32(v);
        }
        catch { }
        return null;
    }

    /// <summary>Cache tìm bag — tránh quét lại mỗi tick.</summary>
    private static System.Collections.IEnumerable _cachedBag = null;
    private static string _cachedBagPath = null;
    private static float _lastBagCacheTime = -999f;

    private static System.Collections.IEnumerable GetItemBag(object player)
    {
        if (player == null) return null;

        // Cache 5 giây — tránh quét lại liên tục mỗi tick
        if (_cachedBag != null && Time.time - _lastBagCacheTime < 5f)
            return _cachedBag;

        try
        {
            // ========= LEVEL 1: Quét trực tiếp player properties/fields =========
            var level1Values = new List<(string path, object val)>();
            CollectMemberValues(player, "player", level1Values);

            var result = FindBestBagInCandidates(level1Values);
            if (result.bag != null)
            {
                Plugin.Log?.LogInfo($"[GameAPI] GetItemBag: found bag at LEVEL 1 path='{result.path}' valid={result.validItems} total={result.totalItems}");
                _cachedBag = result.bag;
                _cachedBagPath = result.path;
                _lastBagCacheTime = Time.time;
                return result.bag;
            }

            // ========= LEVEL 2: Quét sub-objects =========
            // Bag nằm sâu trong player.XXXXX.items (obfuscated name)
            var level2Values = new List<(string path, object val)>();
            foreach (var (path, val) in level1Values)
            {
                if (val == null) continue;
                if (val is string || val.GetType().IsPrimitive || val.GetType().IsEnum) continue;
                // Bỏ qua Unity types lớn — chỉ quét game data objects
                if (val is UnityEngine.Object) continue;
                try
                {
                    CollectMemberValues(val, path, level2Values);
                }
                catch { }
            }

            result = FindBestBagInCandidates(level2Values);
            if (result.bag != null)
            {
                Plugin.Log?.LogInfo($"[GameAPI] GetItemBag: found bag at LEVEL 2 path='{result.path}' valid={result.validItems} total={result.totalItems}");
                _cachedBag = result.bag;
                _cachedBagPath = result.path;
                _lastBagCacheTime = Time.time;
                return result.bag;
            }

            // ========= LEVEL 3: Quét thêm 1 cấp nữa (cho game obfuscate sâu) =========
            var level3Values = new List<(string path, object val)>();
            foreach (var (path, val) in level2Values)
            {
                if (val == null) continue;
                if (val is string || val.GetType().IsPrimitive || val.GetType().IsEnum) continue;
                if (val is UnityEngine.Object) continue;
                if (val is System.Collections.IEnumerable) continue; // đã check ở level 2
                try
                {
                    CollectMemberValues(val, path, level3Values);
                }
                catch { }
            }

            result = FindBestBagInCandidates(level3Values);
            if (result.bag != null)
            {
                Plugin.Log?.LogInfo($"[GameAPI] GetItemBag: found bag at LEVEL 3 path='{result.path}' valid={result.validItems} total={result.totalItems}");
                _cachedBag = result.bag;
                _cachedBagPath = result.path;
                _lastBagCacheTime = Time.time;
                return result.bag;
            }

            Plugin.Log?.LogWarning($"[GameAPI] GetItemBag: NOT FOUND after 3 levels. L1={level1Values.Count} L2={level2Values.Count} L3={level3Values.Count} candidates");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[GameAPI] GetItemBag fail: {ex.Message}");
        }
        return null;
    }

    /// <summary>Thu thập tất cả member values (property + field) từ một object, KHÔNG bao gồm string/primitive.</summary>
    private static void CollectMemberValues(object obj, string parentPath, List<(string path, object val)> results)
    {
        if (obj == null) return;
        var t = obj.GetType();
        var seen = new HashSet<string>();

        while (t != null && t != typeof(object))
        {
            foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                if (prop.PropertyType == typeof(string)) continue;
                string key = $"p:{prop.Name}";
                if (!seen.Add(key)) continue;
                try
                {
                    var val = prop.GetValue(obj);
                    if (val != null)
                        results.Add(($"{parentPath}.{prop.Name}", val));
                }
                catch { }
            }
            foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(string)) continue;
                string key = $"f:{field.Name}";
                if (!seen.Add(key)) continue;
                try
                {
                    var val = field.GetValue(obj);
                    if (val != null)
                        results.Add(($"{parentPath}.{field.Name}", val));
                }
                catch { }
            }
            t = t.BaseType;
        }
    }

    /// <summary>Tìm IEnumerable có nhiều item hợp lệ nhất (có templateId) trong danh sách candidates.</summary>
    private static (System.Collections.IEnumerable bag, string path, int validItems, int totalItems) FindBestBagInCandidates(List<(string path, object val)> candidates)
    {
        System.Collections.IEnumerable bestBag = null;
        string bestPath = null;
        int maxValidItems = -1;
        int maxTotalItems = -1;

        foreach (var (path, val) in candidates)
        {
            if (val == null) continue;
            if (!(val is System.Collections.IEnumerable enumerable) || val is string) continue;

            int validItems = 0;
            int totalItems = 0;
            try
            {
                foreach (var rawItem in enumerable)
                {
                    totalItems++;
                    if (totalItems > 200) break; // safety limit
                    if (rawItem == null) continue;
                    
                    var item = rawItem;
                    if (item == null || item.GetType().IsPrimitive || item is string) continue;
                    
                    if (TryReadItemId(item, out _))
                        validItems++;
                }
            }
            catch { continue; }

            if (validItems > 0 && (validItems > maxValidItems || (validItems == maxValidItems && totalItems > maxTotalItems)))
            {
                maxValidItems = validItems;
                maxTotalItems = totalItems;
                bestBag = enumerable;
                bestPath = path;
            }
        }

        return (bestBag, bestPath, maxValidItems, maxTotalItems);
    }

    /// <summary>Dump tất cả int-returning members của một object (dùng cho diagnostic).</summary>
    private static void DumpItemIntMembers(object item, System.Text.StringBuilder sb, string indent)
    {
        if (item == null) return;
        try
        {
            var t = item.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Properties
            foreach (var p in t.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                try
                {
                    if (IsIntLikeReturn(p.PropertyType))
                    {
                        var v = p.GetValue(item);
                        sb.AppendLine($"{indent}Prop {p.Name} ({p.PropertyType.Name}) = {v}");
                    }
                    else if (p.PropertyType == typeof(string))
                    {
                        var v = p.GetValue(item) as string;
                        if (!string.IsNullOrEmpty(v) && v.Length < 100)
                            sb.AppendLine($"{indent}Prop {p.Name} (String) = \"{v}\"");
                    }
                    else if (!p.PropertyType.IsPrimitive && p.PropertyType != typeof(string)
                             && !typeof(UnityEngine.Object).IsAssignableFrom(p.PropertyType))
                    {
                        var sub = p.GetValue(item);
                        if (sub != null)
                        {
                            sb.AppendLine($"{indent}Prop {p.Name} -> {p.PropertyType.Name} (sub-object)");
                            DumpItemIntMembers(sub, sb, indent + "  ");
                        }
                    }
                }
                catch { }
            }

            // Fields
            foreach (var f in t.GetFields(flags))
            {
                try
                {
                    if (IsIntLikeReturn(f.FieldType))
                    {
                        var v = f.GetValue(item);
                        sb.AppendLine($"{indent}Field {f.Name} ({f.FieldType.Name}) = {v}");
                    }
                    else if (f.FieldType == typeof(string))
                    {
                        var v = f.GetValue(item) as string;
                        if (!string.IsNullOrEmpty(v) && v.Length < 100)
                            sb.AppendLine($"{indent}Field {f.Name} (String) = \"{v}\"");
                    }
                    else if (!f.FieldType.IsPrimitive && f.FieldType != typeof(string)
                             && !typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
                    {
                        var sub = f.GetValue(item);
                        if (sub != null)
                        {
                            sb.AppendLine($"{indent}Field {f.Name} -> {f.FieldType.Name} (sub-object)");
                            // Dump int members of sub-object (only 1 level deep to avoid infinite loop)
                            if (indent.Length < 10)
                                DumpItemIntMembers(sub, sb, indent + "  ");
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }
    public static void DumpBagItemsToLog()
    {
        try
        {
            if (!_itemHeuristicWarmedUp) WarmupItemHeuristics();
            var p = GetMyPlayer();
            if (p == null) { AutoBossUI.AddLog("[Bag] Lỗi: Chưa tìm thấy nhân vật."); return; }
            
            // ================== ALWAYS DO DEEP DUMP FOR DEBUG ==================
            try {
                var sbDump = new System.Text.StringBuilder();
                sbDump.AppendLine("=== DEEP INVENTORY SCAN DIAGNOSTIC ===");
                sbDump.AppendLine($"Scan time: {DateTime.Now}");
                sbDump.AppendLine($"Player type: {p.GetType().Name}");
                sbDump.AppendLine();

                // Dump player structure
                sbDump.AppendLine("--- PLAYER PROPERTIES ---");
                var t = p.GetType();
                while (t != null && t != typeof(object))
                {
                    sbDump.AppendLine($"\n  Type: {t.Name}");
                    foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        sbDump.AppendLine($"    Prop: {prop.Name} -> {prop.PropertyType.Name}");
                    }
                    foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        sbDump.AppendLine($"    Field: {field.Name} -> {field.FieldType.Name}");
                    }
                    t = t.BaseType;
                }

                // Deep scan: tìm tất cả IEnumerable ở 3 levels
                sbDump.AppendLine("\n--- ALL IENUMERABLE CANDIDATES (3 LEVELS) ---");
                var level1 = new List<(string path, object val)>();
                CollectMemberValues(p, "player", level1);

                int enumCount = 0;
                void DumpEnumerables(List<(string path, object val)> values, string levelName)
                {
                    foreach (var (path, val) in values)
                    {
                        if (val == null || !(val is System.Collections.IEnumerable en) || val is string) continue;
                        enumCount++;
                        sbDump.AppendLine($"\n  [{levelName}] #{enumCount} Path: {path}");
                        sbDump.AppendLine($"    Container type: {val.GetType().Name}");
                        int total = 0;
                        int nonNull = 0;
                        Type itemType = null;
                        try
                        {
                            foreach (var rawItem in en)
                            {
                                total++;
                                if (total > 30) { sbDump.AppendLine($"    ... (truncated, >30 items)"); break; }
                                if (rawItem == null) continue;
                                nonNull++;
                                
                                var item = rawItem;
                                if (item == null) continue;
                                
                                if (itemType == null) itemType = item.GetType();
                                
                                // Dump tất cả int values của item
                                if (nonNull <= 3) // chỉ dump 3 sample items
                                {
                                    sbDump.AppendLine($"    Item[{total-1}] type={item.GetType().Name} (Raw: {rawItem.GetType().Name})");
                                    DumpItemIntMembers(item, sbDump, "      ");
                                }
                            }
                        }
                        catch (Exception ex) { sbDump.AppendLine($"    ERROR iterating: {ex.Message}"); }
                        sbDump.AppendLine($"    Total={total} NonNull={nonNull} ItemType={itemType?.Name ?? "?"}");
                    }
                }

                DumpEnumerables(level1, "L1");

                var level2 = new List<(string path, object val)>();
                foreach (var (path, val) in level1)
                {
                    if (val == null || val is string || val.GetType().IsPrimitive || val.GetType().IsEnum || val is UnityEngine.Object) continue;
                    try { CollectMemberValues(val, path, level2); } catch { }
                }
                DumpEnumerables(level2, "L2");

                var level3 = new List<(string path, object val)>();
                foreach (var (path, val) in level2)
                {
                    if (val == null || val is string || val.GetType().IsPrimitive || val.GetType().IsEnum || val is UnityEngine.Object || val is System.Collections.IEnumerable) continue;
                    try { CollectMemberValues(val, path, level3); } catch { }
                }
                DumpEnumerables(level3, "L3");

                sbDump.AppendLine($"\n=== TỔNG: {enumCount} IEnumerable candidates ===");

                string dumpPathPlayer = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "PlayerDump.txt");
                System.IO.File.WriteAllText(dumpPathPlayer, sbDump.ToString());
                Plugin.Log?.LogInfo($"[Bag] Dumped deep diagnostic to {dumpPathPlayer} ({enumCount} enumerables found)");
                AutoBossUI.AddLog($"[Bag] Xuất {enumCount} collections vào PlayerDump.txt");
            } catch (Exception dumpEx) { Plugin.Log?.LogWarning($"[Bag] Dump fail: {dumpEx.Message}"); }
            // ===================================================================

            var bag = GetItemBag(p);
            if (bag == null) 
            { 
                AutoBossUI.AddLog("[Bag] Lỗi: Chưa tìm thấy hành trang."); 
                return; 
            }

            int count = 0;
            bool dumpedFirstItem = false;
            string dumpPath = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "BagDump.txt");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== CÁC VẬT PHẨM TRONG HÀNH TRANG ===");

            foreach (var rawItem in bag)
            {
                if (rawItem != null)
                {
                    var item = rawItem;
                    if (item == null) continue;
                    if (TryReadItemId(item, out int itemId))
                    {
                        string name = GetItemDisplayName(item);
                        int qty = GetItemQuantity(item);
                        string qtyStr = qty > 1 ? $" x{qty}" : "";
                        
                        sb.AppendLine($"- [ID: {itemId}] {name}{qtyStr}");
                        
                        if (!dumpedFirstItem)
                        {
                            dumpedFirstItem = true;
                            sb.AppendLine("\n--- CHI TIẾT ITEM ĐẦU TIÊN (DÙNG ĐỂ DEBUG TÊN) ---");
                            sb.AppendLine($"Item Type: {item.GetType().Name}");
                            DumpItemIntMembers(item, sb, "  ");
                            sb.AppendLine("--------------------------------------------------\n");
                        }
                        
                        count++;
                    }
                }
            }
            sb.AppendLine($"=== TỔNG CỘNG: {count} ITEM ===");
            System.IO.File.WriteAllText(dumpPath, sb.ToString());

            Plugin.Log?.LogInfo($"[Bag] Đã xuất {count} item ra file {dumpPath}");
            AutoBossUI.AddLog($"[Bag] Đã quét {count} item! Xem file BagDump.txt");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[GameAPI] DumpBagItemsToLog fail: {ex.Message}");
            AutoBossUI.AddLog($"[Bag] Lỗi: {ex.Message}");
        }
    }

    private static string GetItemDisplayName(object item)
    {
        if (item == null) return "";
        try
        {
            var candidates = new List<string>();
            CollectStringCandidates(item, candidates);

            // Also check sub-objects for strings (template, info, etc)
            var flags = BindingFlags.Public | BindingFlags.Instance;
            foreach (var p in item.GetType().GetProperties(flags))
            {
                if (p.GetIndexParameters().Length == 0 && !p.PropertyType.IsPrimitive && p.PropertyType != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(p.PropertyType))
                {
                    try
                    {
                        var sub = p.GetValue(item);
                        if (sub != null) CollectStringCandidates(sub, candidates);
                    }
                    catch { }
                }
            }
            foreach (var f in item.GetType().GetFields(flags))
            {
                if (!f.FieldType.IsPrimitive && f.FieldType != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
                {
                    try
                    {
                        var sub = f.GetValue(item);
                        if (sub != null) CollectStringCandidates(sub, candidates);
                    }
                    catch { }
                }
            }

            string best = PickBestStringCandidate(candidates, item.GetType().Name, null);
            if (!string.IsNullOrEmpty(best) && best != item.GetType().Name) 
                return best;
        }
        catch { }
        return item.GetType().Name;
    }

    private static int GetItemQuantity(object item)
    {
        if (item == null) return 1;
        try
        {
            if (ReflectionHelper.TryGetIntMember(item, out int qty, "quantity", "amount", "count", "num")) 
                return qty;
        }
        catch { }
        return 1;
    }

    public static void TryUseItem(int templateId)
    {
        try
        {
            if (!_itemHeuristicWarmedUp) WarmupItemHeuristics();
            var p = GetMyPlayer();
            if (p == null) return;
            
            var bag = GetItemBag(p);
            if (bag == null) return;

            int index = 0;
            foreach (var rawItem in bag)
            {
                if (rawItem != null)
                {
                    var item = rawItem;
                    if (item != null && TryReadItemId(item, out int itemId))
                    {
                        if (itemId == templateId)
                        {
                            Plugin.Log?.LogInfo($"[GameAPI] Found item to use (index: {index}, templateId: {templateId})");
                            if (_miServiceGi != null && _miServiceUseItem != null)
                            {
                                var svc = _miServiceGi.Invoke(null, null);
                                if (svc != null)
                                {
                                    // action=0 (use), type=1 (bag), index=index, templateId=templateId
                                    _miServiceUseItem.Invoke(svc, new object[] { (sbyte)0, (sbyte)1, (sbyte)index, (short)templateId });
                                    Plugin.Log?.LogInfo($"[GameAPI] Invoked Service.useItem(0, 1, {index}, {templateId})");
                                }
                            }
                            break; // Dùng 1 hộp rồi thoát, sẽ dùng hộp tiếp theo ở lần tick kế nếu logic lặp
                        }
                    }
                }
                index++;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[GameAPI] TryUseItem fail: {ex.Message}");
        }
    }

    public static void TryDropItem(int templateId, int optionId)
    {
        try
        {
            if (!_itemHeuristicWarmedUp) WarmupItemHeuristics();
            var p = GetMyPlayer();
            if (p == null) return;
            
            var bag = GetItemBag(p);
            if (bag == null) return;

            int index = 0;
            foreach (var item in bag)
            {
                if (item != null)
                {
                    if (TryReadItemId(item, out int itemId) && itemId == templateId)
                    {
                        // Kiểm tra itemOptions chứa optionId
                        bool hasOption = false;
                        var optsProp = item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(f => f.PropertyType.IsArray);
                        if (optsProp != null)
                        {
                            var opts = optsProp.GetValue(item) as System.Collections.IEnumerable;
                            if (opts != null)
                            {
                                foreach (var opt in opts)
                                {
                                    if (opt != null)
                                    {
                                        var optTempProp = opt.GetType().GetProperties().FirstOrDefault(f => !f.PropertyType.IsPrimitive && f.PropertyType != typeof(string));
                                        if (optTempProp != null)
                                        {
                                            var optTemp = optTempProp.GetValue(opt);
                                            if (optTemp != null && ReflectionHelper.TryGetIntMember(optTemp, out int optId, "id", "optionId", "templateId"))
                                            {
                                                if (optId == optionId)
                                                {
                                                    hasOption = true;
                                                    break;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Fallback
                                            if (ReflectionHelper.TryGetIntMember(opt, out int optId2, "optionTemplateId", "id"))
                                            {
                                                if (optId2 == optionId)
                                                {
                                                    hasOption = true;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (hasOption)
                        {
                            Plugin.Log?.LogInfo($"[GameAPI] Auto drop item {templateId} with option {optionId} executed.");
                            if (_miServiceGi != null && _miServiceUseItem != null)
                            {
                                var svc = _miServiceGi.Invoke(null, null);
                                if (svc != null)
                                {
                                    // action=1 (drop), type=1 (bag), index=index, templateId=templateId
                                    _miServiceUseItem.Invoke(svc, new object[] { (sbyte)1, (sbyte)1, (sbyte)index, (short)templateId });
                                    Plugin.Log?.LogInfo($"[GameAPI] Invoked Service.useItem(1, 1, {index}, {templateId})");
                                }
                            }
                            break; // Vứt 1 item rồi thoát
                        }
                    }
                }
                index++;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[GameAPI] TryDropItem fail: {ex.Message}");
        }
    }
}

/// <summary>
/// Helper gọi FindObjectsOfType&lt;T&gt;() với type động.
/// </summary>
public static class Il2CppAPI
{
    public static object[] FindObjectsOfType(Type t)
    {
        try
        {
            // Object.FindObjectsOfType<T>() - pattern từ NpcScanner
            var method = typeof(UnityEngine.Object).GetMethods()
                .FirstOrDefault(m => m.Name == "FindObjectsOfType"
                                  && m.IsGenericMethodDefinition
                                  && m.GetParameters().Length == 0);
            if (method == null) return new object[0];
            var generic = method.MakeGenericMethod(t);
            var arr = generic.Invoke(null, null) as System.Collections.IEnumerable;
            if (arr == null) return new object[0];
            var list = new List<object>();
            foreach (var item in arr) list.Add(item);
            return list.ToArray();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Il2CppAPI] FindObjectsOfType({t.Name}) fail: {ex.Message}");
            return new object[0];
        }
    }

    public static object[] FindObjectsOfTypeAll(Type t)
    {
        try
        {
            // Resources.FindObjectsOfTypeAll<T>() - pattern từ NpcScanner
            var method = typeof(Resources).GetMethods()
                .FirstOrDefault(m => m.Name == "FindObjectsOfTypeAll"
                                  && m.IsGenericMethodDefinition
                                  && m.GetParameters().Length == 0);
            if (method == null) return new object[0];
            var generic = method.MakeGenericMethod(t);
            var arr = generic.Invoke(null, null) as System.Collections.IEnumerable;
            if (arr == null) return new object[0];
            var list = new List<object>();
            foreach (var item in arr) list.Add(item);
            return list.ToArray();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Il2CppAPI] FindObjectsOfTypeAll({t.Name}) fail: {ex.Message}");
            return new object[0];
        }
    }

    /// <summary>
    /// Dump tất cả methods của một Type để debug (tìm tên method thay đổi sau update).
    /// </summary>
    public static void DumpTypeMethods(Type type, string typeName)
    {
        if (type == null) return;
        try
        {
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.GetParameters().Length == 0 && !m.IsSpecialName) // chỉ lấy getter không tham số
                .OrderBy(m => m.Name)
                .Take(50); // giới hạn 50 methods để không spam log

            Plugin.Log?.LogWarning($"[Il2CppAPI] {typeName} methods (no params, first 50):");
            foreach (var m in methods)
            {
                Plugin.Log?.LogWarning($"  {m.Name}() -> {m.ReturnType.Name}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Il2CppAPI] DumpTypeMethods fail: {ex.Message}");
        }
    }
}
