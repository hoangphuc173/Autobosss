using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AutoBossGrabber;

/// <summary>
/// Component chinh: poll MMF (thread rieng) -> thuc thi click tren Unity main thread.
///
/// LUONG THUC HIEN CLICK (quan trong, doc truoc khi sua):
///   1. Python ghi {command,x,y,status=PENDING} vao MMF "GameBotSharedMem".
///   2. Worker thread doc lenh, chuyen sang main thread qua _reqSeq/_doneSeq.
///   3. Update() doi toa do: screen (goc trai-tren) -> client window -> Unity
///      screen point (goc trai-DUOI: y = Screen.height - clientY). Day chinh la
///      phep doi toa do cua AutoBossUI â€” lam sai buoc nay la click lech pixel.
///   4. Thu click bang uGUI: raycast tat ca BaseRaycaster tai diem do -> lay
///      object tren cung -> Button.onClick.Invoke() (hoac ExecuteHierarchy cho
///      handler tuy bien). Khong trung UI -> fallback PostMessage vao HWND game.
///   5. Worker ghi status DONE/ERROR de Python biet ket qua.
/// </summary>
public class VirtualMouse : MonoBehaviour
{
    public VirtualMouse(IntPtr ptr) : base(ptr) { }

    // â”€â”€ MMF protocol â€” phai khop 100% voi core/mmf_client.py â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private const string MMF_NAME = "GameBotSharedMem_CSharp";
    private const int    MMF_SIZE = 64;

    // Layout chinh thuc: xem docs/PROTOCOL.md (v1.1)
    private const int OFF_CMD    = 0;
    private const int OFF_X      = 4;
    private const int OFF_Y      = 8;
    private const int OFF_STATUS = 12;
    private const int OFF_DELAY  = 16;
    private const int OFF_ABS_X  = 20;   // toa do tuyet doi 0..65535 (GameBotDLL dung; plugin nay bo qua)
    private const int OFF_ABS_Y  = 24;
    private const int OFF_MODE   = 28;   // 0=auto, 1=chi UI, 2=chi PostMessage

    private const int CMD_IDLE     = 0;
    private const int CMD_CLICK    = 1;
    private const int CMD_DBLCLICK = 2;
    private const int CMD_SHUTDOWN = 3;

    private const int ST_PENDING = 0;
    private const int ST_DONE    = 1;
    private const int ST_ERROR   = 2;

    private const int MODE_AUTO = 0;
    private const int MODE_UI_ONLY = 1;
    private const int MODE_POST_ONLY = 2;

    // â”€â”€ Win32 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private const uint FILE_MAP_ALL_ACCESS = 0xF001F;
    private const uint PAGE_READWRITE = 0x04;
    private const uint ERROR_ALREADY_EXISTS = 183;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr OpenFileMappingA(uint access, bool inherit, string name);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr CreateFileMappingA(IntPtr hFile, IntPtr attrs, uint protect,
                                                    uint sizeHigh, uint sizeLow, string name);
    [DllImport("kernel32.dll")] private static extern IntPtr MapViewOfFile(IntPtr h, uint access,
                                                    uint offH, uint offL, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern bool UnmapViewOfFile(IntPtr p);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);

    public struct POINT { public int x; public int y; }


    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    private const uint WM_MOUSEMOVE    = 0x0200;
    private const uint WM_LBUTTONDOWN  = 0x0201;
    private const uint WM_LBUTTONUP    = 0x0202;
    private const int  MK_LBUTTON      = 0x0001;

    // â”€â”€ MMF state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private IntPtr _hMap = IntPtr.Zero;
    private IntPtr _pMap = IntPtr.Zero;
    private Thread _worker;
    private volatile bool _running;

    // hand-off worker -> main thread
    private int _reqSeq;                 // worker tang sau khi ghi request
    private volatile int _doneSeq;       // main tang sau khi xu ly xong
    private volatile int _reqCmd, _reqX, _reqY, _reqDelay, _reqMode;
    private volatile int _resultOk;

    // â”€â”€ UI raycast cache â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private System.Collections.Generic.List<BaseRaycaster> _raycasters =
        new System.Collections.Generic.List<BaseRaycaster>();

    // â”€â”€ HUD (F10) â€” chi dung GUI.Box + GUI.Label (IMGUI bi strip 1 phan,
    //    bai hoc tu AutoBossUI: khong dung GUILayout/GUI.Button) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private bool _showHud = true;
    private string _hudLine1 = "cho ket noi MMF...";
    private string _hudLine2 = "chua co lenh";
    private string _hudLine3 = "";
    private GUIStyle _hudStyle;

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Unity lifecycle
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    void Awake()
    {
        // GUILayout pass co the bi IL2CPP strip â€” tat di de tranh exception moi frame.
        try { useGUILayout = false; } catch { }

        if (!TryOpenMmf())
            Plugin.Log?.LogWarning("[VMouse] MMF chua mo duoc ngay, worker se thu lai moi 200ms");

        _running = true;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "VMouseWorker" };
        _worker.Start();
        Plugin.Log?.LogInfo("[VMouse] San sang nhan lenh click tu Python (MMF: " + MMF_NAME + ")");
    }

    void OnDestroy()
    {
        _running = false;
        try { _worker?.Join(500); } catch { }
        CloseMmf();
    }

    private float _pendingDoubleClickTime = -1f;
    private int _pendingDoubleClickX;
    private int _pendingDoubleClickY;
    private int _pendingDoubleClickMode;

    void Update()
    {
        if (_pendingDoubleClickTime > 0 && Time.unscaledTime >= _pendingDoubleClickTime)
        {
            string dummyInfo;
            DoClick(_pendingDoubleClickX, _pendingDoubleClickY, _pendingDoubleClickMode, out dummyInfo);
            _pendingDoubleClickTime = -1f;
        }

        // F10 an/hien HUD â€” doc truc tiep Input nhu AutoBossUI (luon chay trong game nay)
        if (Input.GetKeyDown(KeyCode.F10)) _showHud = !_showHud;

        int seq = _reqSeq;
        if (seq == _doneSeq) return;   // khong co lenh moi

        bool ok = false;
        string info = "";
        try
        {
            ok = ExecuteCommand(_reqCmd, _reqX, _reqY, _reqMode, out info);
            Plugin.Log?.LogInfo($"[VMouse] ExecuteCommand cmd={_reqCmd} ({_reqX},{_reqY}) -> {info}");
        }
        catch (Exception ex)
        {
            info = "EX: " + ex.Message;
            Plugin.Log?.LogError($"[VMouse] Execute fail: {ex.Message}");
        }

        _resultOk = ok ? 1 : 0;
        _hudLine2 = $"cmd={CmdName(_reqCmd)} ({_reqX},{_reqY}) -> {(ok ? "OK" : "FAIL")}";
        _hudLine3 = info;
        _doneSeq = seq;                // bao cho worker biet da xong (ghi KQ truoc, seq sau)
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Worker thread: poll MMF, cho main thread xu ly, tra status
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    void WorkerLoop()
    {
        while (_running)
        {
            if (_pMap == IntPtr.Zero)
            {
                TryOpenMmf();
                Thread.Sleep(200);
                continue;
            }

            try
            {
                if (ReadInt(OFF_STATUS) == ST_PENDING)
                {
                    int cmd  = ReadInt(OFF_CMD);
                    int x    = ReadInt(OFF_X);
                    int y    = ReadInt(OFF_Y);
                    int dly  = ReadInt(OFF_DELAY);
                    int mode = ReadInt(OFF_MODE);

                    if (cmd == CMD_SHUTDOWN)
                    {
                        // Plugin BepInEx khong tu unload nhu DLL inject â€” chi bao DONE.
                        Plugin.Log?.LogInfo("[VMouse] Nhan CMD_SHUTDOWN (plugin van chay tiep)");
                        WriteInt(OFF_STATUS, ST_DONE);
                    }
                    else if (cmd == CMD_CLICK || cmd == CMD_DBLCLICK)
                    {
                        _reqCmd = cmd; _reqX = x; _reqY = y; _reqDelay = dly; _reqMode = mode;
                        int mySeq = Interlocked.Increment(ref _reqSeq);

                        // Cho Update() xu ly (toi da 3s â€” loading scene co the lam game dung)
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (_running && _doneSeq != mySeq && sw.ElapsedMilliseconds < 3000)
                            Thread.Sleep(2);

                        bool ok = _doneSeq == mySeq && _resultOk == 1;
                        if (ok && dly > 0) Thread.Sleep(Math.Min(dly, 2000));
                        WriteInt(OFF_STATUS, ok ? ST_DONE : ST_ERROR);
                        _hudLine1 = $"MMF OK - lenh gan nhat {(ok ? "thanh cong" : "LOI")}";
                    }
                    else
                    {
                        WriteInt(OFF_STATUS, ST_ERROR);   // lenh la
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[VMouse] Worker loop err: {ex.Message}");
                try { WriteInt(OFF_STATUS, ST_ERROR); } catch { }
            }

            Thread.Sleep(5);
        }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Thuc thi click (main thread)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    bool ExecuteCommand(int cmd, int screenX, int screenY, int mode, out string info)
    {
        if (cmd == CMD_CLICK)
            return DoClick(screenX, screenY, mode, out info);

        if (cmd == CMD_DBLCLICK)
        {
            if (!DoClick(screenX, screenY, mode, out info)) return false;
            
            _pendingDoubleClickX = screenX;
            _pendingDoubleClickY = screenY;
            _pendingDoubleClickMode = mode;
            _pendingDoubleClickTime = Time.unscaledTime + 0.09f;
            
            return true;
        }

        info = "cmd khong ho tro: " + cmd;
        return false;
    }

    /// <summary>
    /// 1 click: doi toa do -> uGUI raycast/Invoke -> fallback PostMessage.
    /// screenX/Y: toa do MAN HINH tuyet doi (goc trai-tren) tu Python gui sang.
    /// </summary>
    bool DoClick(int screenX, int screenY, int mode, out string info)
    {
        // Quan trong: Khong dung GetActiveWindow() vi no se bi sai neu game dang chay ngam (AFK)
        // phai lay truc tiep MainWindowHandle cua tien trinh hien tai.
        IntPtr hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        if (hwnd == IntPtr.Zero)
        {
            info = "khong tim duoc cua so game (MainWindowHandle = Zero)";
            return false;
        }

        // screen -> client (toa do trong vung ve cua game, bo qua vien/tieu de)
        POINT pt = new POINT { x = screenX, y = screenY };
        ScreenToClient(hwnd, ref pt);
        int cx = pt.x;
        int cy = pt.y;

        // â”€â”€ Buoc 1: click qua uGUI (chuan xac nhat, khong can focus) â”€â”€â”€â”€â”€â”€â”€â”€
        if (mode != MODE_POST_ONLY)
        {
            // client -> Unity screen point: y lat nguoc (goc duoi-trai).
            // Screen.height = chieu cao vung client â€” dung phep doi nay y het
            // nhu AutoBossUI.cs:127.
            Vector2 unityPt = new Vector2(cx, Screen.height - cy);

            if (cx >= 0 && cy >= 0 && cx < Screen.width && cy < Screen.height
                && TryUiClick(unityPt, out info))
                return true;
        }

        // â”€â”€ Buoc 2: fallback PostMessage vao cua so game â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (mode != MODE_UI_ONLY)
        {
            PostClick(hwnd, cx, cy);
            info = $"PostMessage client=({cx},{cy})";
            return true;
        }

        info = "khong co UI hit (mode UI_ONLY)";
        return false;
    }

    /// <summary>
    /// Raycast toan bo uGUI tai unityPt, goi click len object tren cung.
    /// Tra ve false neu khong co UI nao tai do (de fallback PostMessage).
    /// </summary>
    bool TryUiClick(Vector2 unityPt, out string info)
    {
        info = "";
        var es = EventSystem.current;
        if (es == null) { info = "EventSystem null"; return false; }

        // Khong cache raycaster theo frame nua vi neu FPS thap, 90 frame co the la vai giay,
        // khien click vao popup moi mo bi xit (vi raycaster chua kip cap nhat).
        // Ham nay chi goi khi thuc hien click (rat it khi xay ra) nen performance khong anh huong.
        var list = new System.Collections.Generic.List<BaseRaycaster>();
        try
        {
            // FindObjectsOfType<T> trong IL2CPP tra ve Il2CppArrayBase<T>
            var arr = UnityEngine.Object.FindObjectsOfType<BaseRaycaster>();
            if (arr != null)
                foreach (var rc in arr)
                    if (rc != null) list.Add(rc);
        }
        catch (Exception ex) { info = "FindRaycasters: " + ex.Message; }
        _raycasters = list;

        if (_raycasters.Count == 0) { info = "khong co raycaster"; return false; }

        var ped = new PointerEventData(es);
        ped.position = unityPt;

        var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        foreach (var rc in _raycasters)
        {
            if (rc == null) continue;
            try { rc.Raycast(ped, results); } catch { /* raycaster hong -> bo qua */ }
        }
        if (results.Count == 0) { info = $"khong UI hit tai {unityPt}"; return false; }

        // Chon object "tren cung": uu tien sortOrder cua raycaster module, sau do depth
        RaycastResult best = results[0];
        float bestKey = Key(best);
        for (int i = 1; i < results.Count; i++)
        {
            float k = Key(results[i]);
            if (k > bestKey) { bestKey = k; best = results[i]; }
        }

        var target = best.gameObject;
        if (target == null) { info = "target null"; return false; }

        // MÃ´ phá»ng Ä‘áº§y Ä‘á»§ vÃ²ng Ä‘á»i click cá»§a uGUI: Down -> Up -> Click
        try
        {
            // 1. Pointer Down
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerDownHandler);
            // 2. Pointer Up
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerUpHandler);
            // 3. Pointer Click (quan trá»ng nháº¥t Ä‘á»ƒ trigger Button)
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerClickHandler);
            
            info = $"Full uGUI Click (Down/Up/Click) '{target.name}' @ {unityPt}";
            return true;
        }
        catch (Exception ex)
        {
            info = "hierarchy fail: " + ex.Message;
            return false;
        }

        float Key(RaycastResult r)
        {
            // sortOrderPriority: GraphicRaycaster tra ve canvas.sortOrder
            // (BaseRaycaster mac dinh = int.MinValue)
            float order = 0;
            try { if (r.module != null) order = r.module.sortOrderPriority; } catch { }
            float depth = 0;
            try { depth = r.depth; } catch { }
            return order * 100000f + depth;
        }
    }

    /// <summary>Gui day du mouse-move/down/up vao cua so game (client coords).</summary>
    void PostClick(IntPtr hwnd, int cx, int cy)
    {
        IntPtr lp = (IntPtr)((cy << 16) | (cx & 0xFFFF));
        PostMessage(hwnd, WM_MOUSEMOVE,   IntPtr.Zero, lp);
        Thread.Sleep(30);
        PostMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lp);
        Thread.Sleep(60);
        PostMessage(hwnd, WM_LBUTTONUP,   IntPtr.Zero, lp);
        Thread.Sleep(40);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // MMF open/close/read/write
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    bool TryOpenMmf()
    {
        if (_pMap != IntPtr.Zero) return true;
        try
        {
            _hMap = OpenFileMappingA(FILE_MAP_ALL_ACCESS, false, MMF_NAME);
            bool created = false;

            if (_hMap == IntPtr.Zero)
            {
                _hMap = CreateFileMappingA(IntPtr.Zero, IntPtr.Zero, PAGE_READWRITE,
                                           0, (uint)MMF_SIZE, MMF_NAME);
                created = Marshal.GetLastWin32Error() != ERROR_ALREADY_EXISTS;
            }
            if (_hMap == IntPtr.Zero) return false;

            _pMap = MapViewOfFile(_hMap, FILE_MAP_ALL_ACCESS, 0, 0, (UIntPtr)MMF_SIZE);
            if (_pMap == IntPtr.Zero) { CloseHandle(_hMap); _hMap = IntPtr.Zero; return false; }

            if (created)
            {
                // Chi zero khi MINH tao â€” tranh xoa lenh pending cua Python
                for (int i = 0; i < MMF_SIZE; i += 4) Marshal.WriteInt32(_pMap, i, 0);
                Marshal.WriteInt32(_pMap, OFF_STATUS, ST_DONE);
            }

            Plugin.Log?.LogInfo($"[VMouse] MMF '{MMF_NAME}' mo thanh cong (created={created})");
            _hudLine1 = "MMF da ket noi";
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[VMouse] OpenMMF fail: {ex.Message}");
            return false;
        }
    }

    void CloseMmf()
    {
        try { if (_pMap != IntPtr.Zero) UnmapViewOfFile(_pMap); } catch { }
        try { if (_hMap != IntPtr.Zero) CloseHandle(_hMap); } catch { }
        _pMap = IntPtr.Zero; _hMap = IntPtr.Zero;
    }

    int ReadInt(int off) => Marshal.ReadInt32(_pMap, off);
    void WriteInt(int off, int val) => Marshal.WriteInt32(_pMap, off, val);

    static string CmdName(int c) => c switch
    {
        CMD_CLICK => "CLICK",
        CMD_DBLCLICK => "DBLCLICK",
        CMD_SHUTDOWN => "SHUTDOWN",
        _ => "?" + c,
    };

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // HUD â€” F10. Chi GUI.Box + GUI.Label (IMGUI control bi strip trong game nay)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    void OnGUI()
    {
        if (!_showHud) return;
        var ev = Event.current;
        if (ev != null && ev.type != EventType.Repaint) return;

        try
        {
            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle();
                _hudStyle.fontSize = 12;
                _hudStyle.normal.textColor = new Color(0.75f, 1f, 0.8f);
            }

            var r = new Rect(8, 8, 430, 64);
            GUI.color = new Color(0.05f, 0.05f, 0.1f, 0.85f);
            GUI.Box(r, GUIContent.none);
            GUI.color = Color.white;

            GUI.Label(new Rect(14, 12, 420, 16), $"[VirtualMouse] {_hudLine1}", _hudStyle);
            GUI.Label(new Rect(14, 28, 420, 16), _hudLine2, _hudStyle);
            GUI.Label(new Rect(14, 44, 420, 16), _hudLine3, _hudStyle);
        }
        catch { /* HUD hong khong duoc phep lam chet plugin */ }
    }
}

