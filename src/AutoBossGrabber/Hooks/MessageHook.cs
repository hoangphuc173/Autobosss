using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace AutoBossGrabber;

/// <summary>
/// Hook vào message handler của game thay vì scan UI text.
///
/// Bản đồ class (rút ra từ interop assembly, tên đã bị obfuscate):
///
///   MessageProcessManager                       <- tên CÒN SẠCH
///       ConcurrentQueue&lt;IDNILNNLAKF&gt; EJNKKJJHAPN     (queue package từ socket thread)
///       void handlePackage(IDNILNNLAKF pkg)     <- tên CÒN SẠCH = choke point mọi message
///       + 19 method khác cùng signature void(IDNILNNLAKF) = dispatch/decoy
///
///   IDNILNNLAKF  = package (envelope)
///       MLJMMJPDHFN FIFKOCOIFLL                 -> Message nằm trong đây
///
///   MLJMMJPDHFN  = Message  (khớp Java: Message{command, dis, dos})
///       sbyte        ENKKANMAAOG                -> command id (âm: -87/-92/-68/-71...)
///       KGKIOPEOGMJ  GCMLJMJKMJM                -> reader (DataInputStream)
///       HOGFIJONPLB  PAIJDNNHHFH                -> writer (DataOutputStream)
///
///   KGKIOPEOGMJ  = reader
///       Il2CppStructArray&lt;sbyte&gt; BOHANNCPFHP    -> BUFFER thô của packet
///       int NPDGAKAFFDJ / IEDAJJHMOAF           -> position / count
///       UTF8Encoding ENOPHAADJEK
///
/// Nguyên tắc an toàn:
///   1. KHÔNG gọi method read* của game (sẽ làm lệch stream position -> game parse sai).
///      Chỉ đọc property buffer rồi tự decode -> hoàn toàn read-only.
///   2. KHÔNG gọi Unity API trong prefix. handlePackage có thể chạy trên socket thread;
///      gọi UnityEngine từ thread khác sẽ crash. Mọi việc cần main thread dồn vào Tick().
///   3. Prefix không bao giờ throw ra ngoài; lỗi liên tiếp thì tự tắt hook.
/// </summary>
public static class MessageHook
{
    private static ManualLogSource _log;
    private static Harmony _harmony;
    private static bool _installed;
    private static volatile bool _disabled;
    private static int _consecutiveErrors;

    // ===== Reflection cache (resolve 1 lần lúc install) =====
    private static PropertyInfo _piPkgMessage;   // IDNILNNLAKF.FIFKOCOIFLL
    private static PropertyInfo _piCmd;          // MLJMMJPDHFN.ENKKANMAAOG (sbyte)

    /// <summary>
    /// Cac cap (stream property, buffer property) tren Message.
    /// Message co CA reader (KGKIOPEOGMJ) va writer (HOGFIJONPLB), va CA HAI deu co
    /// property Il2CppStructArray&lt;sbyte&gt;. Khong the dua vao thu tu reflection de doan,
    /// nen luu het roi lay buffer nao co du lieu that.
    /// </summary>
    private static readonly List<(PropertyInfo stream, PropertyInfo buffer)> _streams = new();

    // ===== Boss keywords =====
    private static readonly string[] SpawnKeywords =
    {
        "xuat hien", "xuathien", "da xuat hien", "appeared", "spawn", "trieu hoi"
    };
    private static List<string> _bossKeywords = new List<string>();

    /// <summary>Keyword của MỌI dòng world-broadcast (spawn + diệt boss), dùng để đếm số dòng trong packet.</summary>
    private static readonly string[] BroadcastKeywords =
    {
        "xuat hien", "xuathien", "da tieu diet", "tieu diet", "appeared", "spawn", "trieu hoi"
    };

    /// <summary>Thứ tự cmd đáng nghi mà user đã khoanh vùng: -87 / -92 / -68 / -71.</summary>
    private static readonly sbyte[] WatchedCommands = { -87, -92, -68, -71 };

    // ===== Chống thông báo boss CŨ (xem AcceptAnnounce) =====
    /// <summary>Packet announce sống chỉ 72-79 byte trong dump; packet lịch sử ~1550 byte.</summary>
    private const int MaxLiveAnnouncePacketLen = 256;
    private static readonly TimeSpan WarmupWindow = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SameTextCooldown = TimeSpan.FromMinutes(2);
    private static DateTime _installedAt = DateTime.MinValue;
    private static readonly Dictionary<string, DateTime> _lastAnnounceAt = new Dictionary<string, DateTime>();

    // ===== Log buffer (ghi từ socket thread, flush ở main thread) =====
    private static readonly object _sync = new object();
    private static readonly List<string> _pending = new List<string>();
    private static readonly Dictionary<int, int> _cmdCount = new Dictionary<int, int>();
    private static readonly Dictionary<int, int> _cmdLogged = new Dictionary<int, int>();
    private static string _logPath;
    private static DateTime _lastFlush = DateTime.MinValue;

    /// <summary>Số sample log tối đa cho mỗi cmd (tránh spam), cmd trong WatchedCommands được nhiều hơn.</summary>
    private const int SamplesPerCmd = 3;
    private const int SamplesPerWatchedCmd = 25;

    // ===== Trigger (set ở socket thread, đọc ở main thread) =====
    private static volatile bool _bossPending;
    private static string _bossPendingText;

    /// <summary>Bắn ra ở MAIN THREAD (từ Tick), an toàn để gọi Unity API.</summary>
    public static event Action<string> OnBossDetected;

    public static bool IsInstalled => _installed && !_disabled;

    public static void Install(ManualLogSource log, AutoBossConfig config)
    {
        if (_installed) return;
        _installed = true;
        _installedAt = DateTime.Now;
        _log = log;

        BuildBossKeywords(config);

        try
        {
            _logPath = ResolveLogPath();

            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (asm == null)
            {
                _log?.LogError("[MessageHook] Assembly-CSharp not found -> abort");
                _disabled = true;
                return;
            }

            var tMpm = asm.GetType("MessageProcessManager");
            if (tMpm == null)
            {
                _log?.LogError("[MessageHook] type MessageProcessManager not found -> abort");
                _disabled = true;
                return;
            }

            // handlePackage(pkg) - tên method này KHÔNG bị obfuscate nên bám vào được.
            var miHandle = tMpm.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "handlePackage" && m.GetParameters().Length == 1);
            if (miHandle == null)
            {
                _log?.LogError("[MessageHook] handlePackage(pkg) not found -> abort");
                _disabled = true;
                return;
            }

            var tPackage = miHandle.GetParameters()[0].ParameterType;
            if (!ResolveShape(tPackage))
            {
                _disabled = true;
                return;
            }

            _harmony = new Harmony("com.autobossgrabber.msghook");
            var prefix = new HarmonyMethod(typeof(MessageHook)
                .GetMethod(nameof(HandlePackagePrefix), BindingFlags.Static | BindingFlags.NonPublic));
            _harmony.Patch(miHandle, prefix: prefix);

            _log?.LogWarning("[MessageHook] *** PATCHED MessageProcessManager.handlePackage ***");
            _log?.LogInfo($"[MessageHook] Log file: {_logPath}");
            _log?.LogInfo($"[MessageHook] Watching cmd: {string.Join(", ", WatchedCommands)}");
        }
        catch (Exception ex)
        {
            _disabled = true;
            _log?.LogError($"[MessageHook] Install failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Tìm property theo HÌNH DẠNG (không hardcode tên obfuscate) để chịu được update game.
    /// Message = property của package mà type của nó có property sbyte (command)
    /// + ít nhất 1 property chứa buffer Il2CppStructArray&lt;sbyte&gt; (reader/writer).
    /// </summary>
    private static bool ResolveShape(Type tPackage)
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Prefix nhan tham so duoi dang Il2CppObjectBase, nen package phai la interop type.
        if (!typeof(Il2CppObjectBase).IsAssignableFrom(tPackage))
        {
            _log?.LogError($"[MessageHook] {tPackage.Name} is not an Il2CppObjectBase -> abort");
            return false;
        }

        foreach (var p in tPackage.GetProperties(F))
        {
            var t = p.PropertyType;
            if (t.IsPrimitive || t == typeof(string)) continue;

            var cmd = t.GetProperties(F).FirstOrDefault(x => x.PropertyType == typeof(sbyte));
            if (cmd == null) continue;

            // Gom MOI stream co buffer: reader va writer cung hinh dang nen phai thu ca hai.
            var pairs = new List<(PropertyInfo, PropertyInfo)>();
            foreach (var sp in t.GetProperties(F))
            {
                if (sp.PropertyType.IsPrimitive) continue;
                var buf = sp.PropertyType.GetProperties(F)
                    .FirstOrDefault(x => x.PropertyType == typeof(Il2CppStructArray<sbyte>));
                if (buf != null) pairs.Add((sp, buf));
            }
            if (pairs.Count == 0) continue;

            _piPkgMessage = p;
            _piCmd = cmd;
            _streams.Clear();
            _streams.AddRange(pairs);

            _log?.LogInfo($"[MessageHook] shape: {tPackage.Name}.{p.Name} -> {t.Name}" +
                          $" (cmd={cmd.Name}, streams=" +
                          string.Join(", ", pairs.Select(x => $"{x.Item1.Name}.{x.Item2.Name}")) + ")");
            return true;
        }

        _log?.LogError($"[MessageHook] cannot resolve message shape from {tPackage.Name} -> abort");
        return false;
    }

    private static void BuildBossKeywords(AutoBossConfig config)
    {
        _bossKeywords.Clear();
        if (config == null) return;

        // Thu thập tên từ BossProfiles (ưu tiên) + BossNames (legacy fallback)
        var allNames = new List<string>();
        if (config.BossProfiles != null)
            foreach (var p in config.BossProfiles)
                if (p?.BossNames != null)
                    allNames.AddRange(p.BossNames);
        if (config.BossNames != null)
            allNames.AddRange(config.BossNames);

        foreach (var name in allNames)
        {
            var full = Normalize(name);
            if (full.Length >= 3 && !_bossKeywords.Contains(full))
                _bossKeywords.Add(full);

            foreach (var w in full.Split(' '))
                if (w.Length >= 4 && !_bossKeywords.Contains(w))
                    _bossKeywords.Add(w);
        }
    }

    // ======================================================================
    //  PREFIX - có thể chạy trên SOCKET THREAD. Không Unity API, không throw.
    // ======================================================================
    /// <summary>
    /// Prefix cho handlePackage.
    ///
    /// QUAN TRONG - tham so PHAI khai bao la Il2CppObjectBase, KHONG duoc dung IntPtr.
    /// Il2CppInterop da wrap con tro native thanh managed object truoc khi goi patch;
    /// khai bao IntPtr khien Harmony emit ldarg cua mot reference type vao slot IntPtr
    /// -> IL khong verify duoc -> JIT nem InvalidProgramException ngay khi packet dau tien
    /// den, va prefix khong bao gio chay. Il2CppObjectBase la base class cua moi type
    /// interop nen ldarg luon hop le.
    /// </summary>
    private static void HandlePackagePrefix(Il2CppObjectBase __0)
    {
        if (_disabled) return;

        try
        {
            var pkg = __0;
            if (pkg == null || pkg.Pointer == IntPtr.Zero) return;

            var msg = _piPkgMessage.GetValue(pkg);
            if (msg == null) return;

            sbyte cmd = (sbyte)_piCmd.GetValue(msg);
            Inspect(cmd, ReadBuffer(msg));

            _consecutiveErrors = 0;
        }
        catch (Exception ex)
        {
            // Lỗi trong prefix không được phá game -> đếm và tự tắt nếu lặp lại.
            if (++_consecutiveErrors >= 10)
            {
                _disabled = true;
                Queue($"!! hook disabled after {_consecutiveErrors} errors: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Lay buffer thô của packet. Message co ca reader va writer cung hinh dang,
    /// nen thu lan luot va lay buffer dai nhat (writer thuong rong voi packet nhan vao).
    /// Chi DOC property, khong goi read* cua game -> khong lam lech stream position.
    /// </summary>
    private static byte[] ReadBuffer(object msg)
    {
        byte[] best = null;

        foreach (var (streamProp, bufProp) in _streams)
        {
            object stream;
            try { stream = streamProp.GetValue(msg); }
            catch { continue; }
            if (stream == null) continue;

            Il2CppStructArray<sbyte> arr;
            try { arr = bufProp.GetValue(stream) as Il2CppStructArray<sbyte>; }
            catch { continue; }
            if (arr == null) continue;

            int len = arr.Length;
            if (len <= 0 || len > 64 * 1024) continue;   // bỏ packet quá lớn cho nhẹ
            if (best != null && len <= best.Length) continue;

            var outBuf = new byte[len];
            for (int i = 0; i < len; i++) outBuf[i] = unchecked((byte)arr[i]);
            best = outBuf;
        }

        return best;
    }

    private static void Inspect(sbyte cmd, byte[] buf)
    {
        bool watched = Array.IndexOf(WatchedCommands, cmd) >= 0;

        // Chup luon so lan trong lock: prefix chay tren socket thread nen doc
        // Dictionary ngoai lock co the throw hoac tra ve rac.
        int seenCount;
        lock (_sync)
        {
            seenCount = _cmdCount.GetValueOrDefault(cmd) + 1;
            _cmdCount[cmd] = seenCount;
        }

        var strings = buf == null ? new List<string>() : ExtractStrings(buf);

        // 1) Detect boss: chỉ cần 1 string khớp keyword, nhưng phải là thông báo SỐNG.
        foreach (var s in strings)
        {
            if (!IsBossAnnounce(s)) continue;

            if (!AcceptAnnounce(cmd, s, strings, buf)) break;

            _bossPendingText = $"cmd={cmd} :: {s}";
            _bossPending = true;
            Queue($"*** BOSS MATCH *** cmd={cmd} text=\"{s}\"");
            break;
        }

        // 2) Log sample để user xác định cmd nào là boss announce.
        int limit = watched ? SamplesPerWatchedCmd : SamplesPerCmd;
        bool hasText = strings.Count > 0;
        lock (_sync)
        {
            int logged = _cmdLogged.GetValueOrDefault(cmd);
            // Message có text thì luôn đáng log hơn message thuần số.
            if (logged >= limit && !(hasText && logged < limit * 3)) return;
            _cmdLogged[cmd] = logged + 1;
        }

        var sb = new StringBuilder();
        sb.Append($"cmd={cmd,4} len={(buf?.Length ?? 0),5} n={seenCount,5}");
        if (watched) sb.Append(" [WATCHED]");
        if (hasText)
            sb.Append("  strings=").Append(string.Join(" | ", strings.Take(8).Select(s => "\"" + s + "\"")));
        else if (buf != null && buf.Length > 0)
            sb.Append("  hex=").Append(BitConverter.ToString(buf, 0, Math.Min(buf.Length, 48)));
        Queue(sb.ToString());
    }

    /// <summary>
    /// Rút string ra khỏi packet. Protocol kiểu Java: writeUTF = 2 byte big-endian length + UTF-8 bytes.
    /// Quét cả pattern đó lẫn các đoạn UTF-8 in được liên tiếp, rồi dedupe.
    /// </summary>
    private static List<string> ExtractStrings(byte[] buf)
    {
        var found = new List<string>();
        var seen = new HashSet<string>();

        // (a) Java writeUTF: [len_hi][len_lo][utf8...]
        for (int i = 0; i + 2 <= buf.Length; i++)
        {
            int len = (buf[i] << 8) | buf[i + 1];
            if (len < 4 || len > 512 || i + 2 + len > buf.Length) continue;

            var s = TryDecode(buf, i + 2, len);
            if (s == null) continue;
            if (seen.Add(s)) found.Add(s);
            i += 1 + len;
        }

        // (b) Fallback: chuỗi UTF-8 in được liên tiếp (khi length prefix khác kiểu).
        int start = -1;
        for (int i = 0; i <= buf.Length; i++)
        {
            bool printable = i < buf.Length && (buf[i] >= 0x20 || buf[i] >= 0xC0);
            if (printable)
            {
                if (start < 0) start = i;
                continue;
            }
            if (start >= 0 && i - start >= 6)
            {
                var s = TryDecode(buf, start, i - start);
                if (s != null && seen.Add(s)) found.Add(s);
            }
            start = -1;
        }

        return found;
    }

    private static string TryDecode(byte[] buf, int offset, int len)
    {
        try
        {
            var s = new UTF8Encoding(false, true).GetString(buf, offset, len);
            if (s.Length < 3) return null;

            // Phải chủ yếu là ký tự đọc được, nếu không thì đó là số/binary bị decode nhầm.
            int good = s.Count(c => !char.IsControl(c) && (char.IsLetterOrDigit(c) || char.IsPunctuation(c)
                                    || char.IsWhiteSpace(c) || char.IsSymbol(c)));
            if (good < s.Length * 0.9) return null;
            if (!s.Any(char.IsLetter)) return null;

            return s.Trim();
        }
        catch
        {
            return null;   // không phải UTF-8 hợp lệ
        }
    }

    /// <summary>
    /// Loc thong bao boss CU ra khoi thong bao boss SONG.
    ///
    /// BUG da xay ra: ngay khi vao game, server day xuong LICH SU world-chat trong MOT
    /// packet cmd=-87 duy nhat. Dump 10:48 va 10:49 deu cho thay match o n=2 (packet -87
    /// thu hai sau khi ket noi) voi len=1551 / len=1548, chua ca chuc dong cu lan lon:
    ///   "Nguoi choi Gohanvipz da tieu diet Perfect Cellion" | "Semi-Perfect Cellion vua
    ///    xuat hien..." | ... | "Vua Vegiita Ao Anh vua xuat hien tai Cung Dien Do Nat"
    /// Dong Vegita do da cu tu lau, nhung hook van bat -> tool tu bat -> tele vo nghia.
    ///
    /// Thong bao SONG co hinh dang khac han (dump 09:58 / 10:50):
    ///   cmd=-87 len=79 n=9  strings="Vua Vegiita Ao Anh vua xuat hien tai Cung Dien Do Nat"
    /// tuc la packet NHO va chi CHO DUNG MOT dong broadcast.
    ///
    /// Ba lop loc, theo do tin cay giam dan:
    ///   1. Packet lon / nhieu dong broadcast -> lich su, bo.
    ///   2. Trong 20s dau sau khi install -> con dang nhan backlog luc login, bo.
    ///   3. Cung mot text trong vong 2 phut -> server gui lai, bo (dump 10:48 co payload
    ///      779 byte giong het byte tai 10:48:16.796 va 10:48:25.702).
    /// </summary>
    private static bool AcceptAnnounce(sbyte cmd, string text, List<string> strings, byte[] buf)
    {
        int len = buf?.Length ?? 0;

        // 1) Packet lich su: to, hoac chua nhieu dong broadcast.
        int broadcastLines = CountDistinctBroadcastLines(strings);
        if (len > MaxLiveAnnouncePacketLen || broadcastLines > 1)
        {
            Queue($"-- ignore STALE (history packet len={len} broadcastLines={broadcastLines}) cmd={cmd} text=\"{text}\"");
            return false;
        }

        // 2) Backlog luc moi vao game.
        var age = DateTime.Now - _installedAt;
        if (age < WarmupWindow)
        {
            Queue($"-- ignore STALE (warmup {age.TotalSeconds:F1}s < {WarmupWindow.TotalSeconds:F0}s) cmd={cmd} text=\"{text}\"");
            return false;
        }

        // 3) Server gui lai cung mot dong.
        var key = Normalize(text);
        lock (_sync)
        {
            if (_lastAnnounceAt.TryGetValue(key, out var prev) && DateTime.Now - prev < SameTextCooldown)
            {
                Queue($"-- ignore DUPLICATE ({(DateTime.Now - prev).TotalSeconds:F1}s ago) cmd={cmd} text=\"{text}\"");
                return false;
            }
            _lastAnnounceAt[key] = DateTime.Now;
        }

        return true;
    }

    /// <summary>
    /// Dem so dong broadcast KHAC NHAU trong packet.
    ///
    /// ExtractStrings tra ve trung lap cho cung mot dong: pattern writeUTF cho ban sach,
    /// con fallback quet UTF-8 in duoc cho ban dinh rac dau/cuoi. Dump 10:50 la vi du:
    ///   "Perfect Cellion vua xuat hien tai Vung Dat Hoang Tan"
    ///   "APerfect Cellion vua xuat hien tai Vung Dat Hoang TanjyJ"
    /// Dem tho se ra 2 -> packet announce SONG bi chan oan. Nen coi hai chuoi la MOT dong
    /// khi cai nay chua cai kia (sau khi bo dau).
    /// </summary>
    private static int CountDistinctBroadcastLines(List<string> strings)
    {
        var lines = new List<string>();

        foreach (var s in strings)
        {
            var n = Normalize(s);
            if (!BroadcastKeywords.Any(k => n.Contains(k))) continue;
            if (lines.Any(x => x.Contains(n) || n.Contains(x))) continue;
            lines.Add(n);
        }

        return lines.Count;
    }

    private static bool IsBossAnnounce(string text)
    {
        var n = Normalize(text);
        if (n.Length < 6) return false;

        bool spawn = SpawnKeywords.Any(k => n.Contains(k));
        if (!spawn) return false;

        return _bossKeywords.Any(k => n.Contains(k));
    }

    /// <summary>Bỏ dấu tiếng Việt + lowercase để so khớp không phụ thuộc dấu.</summary>
    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        var form = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(form.Length);
        foreach (var c in form)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Replace('đ', 'd').Replace("  ", " ").Trim();
    }

    private static void Queue(string line)
    {
        lock (_sync)
        {
            if (_pending.Count > 500) return;   // chặn phình bộ nhớ nếu Tick không chạy
            _pending.Add($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
        }
    }

    // ======================================================================
    //  TICK - gọi từ MAIN THREAD (AutoBoss.Update)
    // ======================================================================
    public static void Tick()
    {
        if (!_installed) return;

        // Bắn event ở main thread để handler được phép gọi Unity API.
        if (_bossPending)
        {
            _bossPending = false;
            var text = _bossPendingText;
            _log?.LogWarning($"[MessageHook] *** BOSS DETECTED via message hook *** {text}");
            try { OnBossDetected?.Invoke(text); }
            catch (Exception ex) { _log?.LogError($"[MessageHook] handler error: {ex.Message}"); }
        }

        if ((DateTime.Now - _lastFlush).TotalSeconds < 2) return;
        _lastFlush = DateTime.Now;
        Flush();
    }

    public static void Flush()
    {
        List<string> lines;
        lock (_sync)
        {
            if (_pending.Count == 0) return;
            lines = new List<string>(_pending);
            _pending.Clear();
        }

        foreach (var l in lines) _log?.LogInfo("[MSG] " + l);

        if (string.IsNullOrEmpty(_logPath)) return;
        try { File.AppendAllLines(_logPath, lines, Encoding.UTF8); }
        catch { /* ghi file lỗi thì bỏ, log console vẫn còn */ }
    }

    /// <summary>Bảng tổng hợp cmd đã thấy - dùng để chốt cmd nào là boss announce.</summary>
    public static void DumpCommandStats()
    {
        Dictionary<int, int> snapshot;
        lock (_sync) snapshot = new Dictionary<int, int>(_cmdCount);

        var sb = new StringBuilder();
        sb.AppendLine("=== MESSAGE COMMAND STATS ===");
        sb.AppendLine($"installed={_installed} disabled={_disabled} distinctCmd={snapshot.Count}");
        foreach (var kv in snapshot.OrderByDescending(k => k.Value))
        {
            bool watched = Array.IndexOf(WatchedCommands, (sbyte)kv.Key) >= 0;
            sb.AppendLine($"  cmd={kv.Key,4} count={kv.Value,6}{(watched ? "  [WATCHED]" : "")}");
        }

        _log?.LogWarning(sb.ToString());
        if (string.IsNullOrEmpty(_logPath)) return;
        try { File.AppendAllText(_logPath, sb.ToString(), Encoding.UTF8); }
        catch { }
    }

    private static string ResolveLogPath()
    {
        var dir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "..", "dump");
        try
        {
            Directory.CreateDirectory(dir);
            return Path.GetFullPath(Path.Combine(dir, $"msg_hook_{DateTime.Now:yyyyMMdd_HHmmss}.txt"));
        }
        catch
        {
            return null;
        }
    }
}
