using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// In-game overlay UI. F9 = mở/đóng cửa sổ.
///
/// THIẾT KẾ QUAN TRỌNG — đọc trước khi sửa:
/// Game này bị IL2CPP strip toàn bộ GUILayout + phần lớn control machinery của IMGUI.
/// Hậu quả: GUI.Button / GUI.TextField / GUI.Toggle / GUI.BeginScrollView VẼ được nhưng
/// KHÔNG BAO GIỜ nhận click (control ID + hotControl bookkeeping bị lỗi).
///
/// Nên file này tự làm immediate-mode UI:
///   - Vẽ: CHỈ dùng GUI.Label + GUI.DrawTexture (2 hàm đã xác nhận chạy được trong game này).
///   - Input: đọc trực tiếp từ lớp Input (Input.GetMouseButton*, Input.mousePosition,
///     Input.inputString, Input.mouseScrollDelta) trong Update() — không phụ thuộc IMGUI event.
///   - Hit-test button/toggle/textfield tự viết tay.
/// KHÔNG thêm GUI.Button/GUI.TextField/GUILayout.* vào file này.
/// </summary>
public class AutoBossUI : MonoBehaviour
{
    public static AutoBossUI Instance { get; private set; }

    private bool _visible = false;
    private Rect _winR = new Rect(20, 20, 560, 620);

    private int _tab = 0;
    private static readonly string[] TabNames = { "Tổng Quan", "Skill Boss", "Quản Lý Item", "Cấu Hình", "Log" };

    // ── Input state (cập nhật trong Update, tiêu thụ trong OnGUI/Repaint) ─────
    private Vector2 _mouse;         // toạ độ GUI (y đảo so với Input.mousePosition)
    private Vector2 _pressPos;      // vị trí lúc nhấn xuống
    private Vector2 _clickPos;      // vị trí lúc nhả ra
    private bool _mouseHeld;
    private bool _pendingClick;     // đã nhả chuột, chưa widget nào nhận
    private float _wheel;
    private bool _dragging;
    private Vector2 _dragOff;

    // text field focus
    private string _focus;          // id của field đang focus (null = không)
    private bool _focusClaimed;     // trong pass vẽ hiện tại đã có field nhận click chưa
    private string _typed = "";     // ký tự gõ trong frame
    private int _backspaces;

    // scroll offsets
    private float _skillScroll;
    private float _logScroll;

    // skill trigger edit buffers
    private readonly Dictionary<int, string[]> _buf = new Dictionary<int, string[]>();
    private string _newHp = "100000";
    private string _newSk = "1";

    // item edit buffers
    private string _newItemUseId = "";
    private string _newItemDropId = "";
    private float _itemUseScroll;
    private float _itemDropScroll;
    private string _bItemDelay = "";

    // config edit buffers
    private string _bAtkRange  = "";
    private string _bCombatTo  = "";
    private string _bRetreatHp = "";
    private string _bLootR     = "";
    private string _bScanEvery = "";

    // log ring
    private readonly Queue<string> _logs = new Queue<string>();
    private const int MaxLogs = 120;

    // state snapshot
    private float _bossHp = -1f;

    // styles / textures
    private bool _built = false;
    private bool _drawErrLogged = false;
    private Texture2D _px;
    private GUIStyle _stTitle, _stLbl, _stLblR, _stBtn, _stLog, _stFill;

    // layout constants
    private const float LH  = 22f;
    private const float SPC = 4f;
    private const float PAD = 10f;

    // palette
    private static readonly Color CBg     = new Color(0.06f, 0.06f, 0.11f, 0.97f);
    private static readonly Color CPanel  = new Color(0.10f, 0.10f, 0.18f, 1.00f);
    private static readonly Color CBorder = new Color(0.35f, 0.38f, 0.65f, 1.00f);
    private static readonly Color CGreen  = new Color(0.13f, 0.52f, 0.22f);
    private static readonly Color CRed    = new Color(0.58f, 0.13f, 0.18f);
    private static readonly Color CBlue   = new Color(0.10f, 0.36f, 0.72f);
    private static readonly Color CGray   = new Color(0.20f, 0.20f, 0.30f);
    private static readonly Color CTabOn  = new Color(0.22f, 0.22f, 0.45f);
    private static readonly Color CTabOff = new Color(0.13f, 0.13f, 0.21f);
    private static readonly Color CField  = new Color(0.14f, 0.14f, 0.24f);
    private static readonly Color CAccent = new Color(0.65f, 0.70f, 1.00f);
    private static readonly Color CDim    = new Color(0.70f, 0.70f, 0.88f);

    void Awake()
    {
        Instance = this;
        // Tắt GUILayout pass — GUILayout bị strip, để Unity chạy nó sẽ ném lỗi mỗi frame.
        // Bọc try/catch: nếu setter này cũng bị strip thì Awake không được phép chết,
        // vì chết ở đây là mất luôn cả UI.
        try { useGUILayout = false; }
        catch (Exception ex) { Plugin.Log?.LogWarning($"[UI] useGUILayout=false fail: {ex.Message}"); }
    }

    public static void AddLog(string msg)
    {
        if (Instance == null) return;
        Instance._logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {msg}");
        while (Instance._logs.Count > MaxLogs) Instance._logs.Dequeue();
    }

    public void UpdateState(string _, float bossHp) { _bossHp = bossHp; }

    // ── Input: tất cả xử lý ở đây, không dùng IMGUI event ────────────────────
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9))
        {
            _visible = !_visible;
            if (_visible) SyncBufs();
            else { _focus = null; _dragging = false; _mouseHeld = false; }
        }

        if (!_visible) return;

        _mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        _wheel += Input.mouseScrollDelta.y;

        if (Input.GetMouseButtonDown(0))
        {
            _mouseHeld = true;
            _pressPos  = _mouse;

            Rect dragR = new Rect(_winR.x, _winR.y, _winR.width - 36, 28);
            if (dragR.Contains(_mouse))
            {
                _dragging = true;
                _dragOff  = _mouse - new Vector2(_winR.x, _winR.y);
            }
        }

        if (_dragging && _mouseHeld)
        {
            Vector2 p = _mouse - _dragOff;
            _winR.x = Mathf.Clamp(p.x, 0, Mathf.Max(0, Screen.width  - _winR.width));
            _winR.y = Mathf.Clamp(p.y, 0, Mathf.Max(0, Screen.height - _winR.height));
        }

        if (Input.GetMouseButtonUp(0))
        {
            _mouseHeld = false;
            _dragging  = false;
            _pendingClick = true;
            _clickPos = _mouse;
        }

        // Gõ chữ cho field đang focus
        if (_focus != null)
        {
            string s = Input.inputString;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\b') _backspaces++;
                else if (c == '\n' || c == '\r') _focus = null;
                else if (!char.IsControl(c)) _typed += c;
            }
        }
    }

    // ── Vẽ: chỉ chạy trên Repaint ────────────────────────────────────────────
    void OnGUI()
    {
        if (!_visible) return;

        Event ev = Event.current;
        EventType et = ev.type;

        // Nuốt event chuột của IMGUI khi con trỏ nằm trong cửa sổ (giảm click lọt xuống game).
        // Use() chỉ hợp lệ cho event chuột — Repaint/Layout sẽ spam warning.
        if (et == EventType.MouseDown || et == EventType.MouseUp
         || et == EventType.MouseDrag || et == EventType.ScrollWheel)
        {
            if (_winR.Contains(ev.mousePosition)) ev.Use();
            return;
        }

        if (et != EventType.Repaint) return;

        if (!_built)
        {
            // Đặt _built=true TRƯỚC khi build: nếu Build() ném lỗi thì cũng không
            // retry vô hạn mỗi frame với exception không bắt được.
            _built = true;
            try { Build(); }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[UI] Build style fail: {ex.Message}");
                return;
            }
        }
        if (_stLbl == null) return;   // style chưa dựng được → không vẽ

        bool hadClick = _pendingClick;
        _focusClaimed = false;

        try { Draw(); }
        catch (Exception ex)
        {
            // Log 1 lần rồi thôi — OnGUI chạy mỗi frame, log liên tục sẽ ngập console.
            if (!_drawErrLogged)
            {
                _drawErrLogged = true;
                Plugin.Log?.LogError($"[UI] Draw fail: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Click vào vùng trống → bỏ focus text field
        if (hadClick && !_focusClaimed) _focus = null;

        // Reset input tích luỹ trong frame
        _pendingClick = false;
        _typed = "";
        _backspaces = 0;
        _wheel = 0f;
    }

    void Draw()
    {
        float W = _winR.width, H = _winR.height;
        float x = _winR.x, y = _winR.y;

        // Nền + viền
        Fill(_winR, CBg);
        Frame(_winR, CBorder);

        // Thanh tiêu đề
        Fill(new Rect(x + 1, y + 1, W - 2, 27), new Color(0.13f, 0.14f, 0.26f, 1f));
        Text(new Rect(x + 10, y + 5, W - 60, 20), "⚔  AutoBossGrabber v2.0   (kéo để di chuyển)", CAccent, _stTitle);

        if (Button(new Rect(x + W - 30, y + 4, 24, 20), "✕", CRed)) { _visible = false; _focus = null; }
        y += 30;

        // Tab bar
        float tw = (W - 10f) / TabNames.Length;
        for (int i = 0; i < TabNames.Length; i++)
        {
            Rect tr = new Rect(x + 5 + i * tw, y, tw - 2, 24);
            if (Button(tr, TabNames[i], i == _tab ? CTabOn : CTabOff, i == _tab ? Color.white : CDim))
                _tab = i;
            if (i == _tab) Fill(new Rect(tr.x, tr.yMax - 2, tr.width, 2), CAccent);
        }
        y += 27;

        // Khung nội dung
        Rect body = new Rect(x + 4, y, W - 8, H - (y - _winR.y) - 5);
        Fill(body, CPanel);
        Frame(body, new Color(0.25f, 0.26f, 0.42f));

        Rect c = new Rect(body.x + PAD, body.y + PAD, body.width - PAD * 2, body.height - PAD * 2);
        switch (_tab)
        {
            case 0: DrawOverview(c); break;
            case 1: DrawSkill(c);    break;
            case 2: DrawItemManager(c); break;
            case 3: DrawConfig(c);   break;
            case 4: DrawLogTab(c);   break;
        }
    }

    // ── Tab 0: Tổng Quan ─────────────────────────────────────────────────────
    void DrawOverview(Rect c)
    {
        var run = Plugin.Instance?.Runner;
        var cfg = run?.Config;
        bool on  = cfg?.Enabled ?? false;
        float cy = c.y;

        if (Button(new Rect(c.x, cy, c.width, 34),
                   on ? "● AUTO BOSS: ĐANG BẬT   (nhấn để tắt)" : "● AUTO BOSS: ĐANG TẮT   (nhấn để bật)",
                   on ? CGreen : CRed))
        {
            if (cfg != null)
            {
                cfg.Enabled = !cfg.Enabled;
                AddLog($"[UI] Auto Boss → {(cfg.Enabled ? "BẬT" : "TẮT")}");
            }
        }
        cy += 38;

        cy = Head("Trạng Thái", c, cy);
        string state = run != null ? run.State.ToString() : "N/A";
        cy = Row("State",   state, c, cy, SCol(state));
        cy = Row("Boss HP", _bossHp >= 0 ? _bossHp.ToString("N0") : "—", c, cy,
                 _bossHp >= 0 ? new Color(1f, 0.55f, 0.2f) : Color.gray);
        string map = GameAPI.GetCurrentMapName();
        cy = Row("Map",     string.IsNullOrEmpty(map) ? "—" : map, c, cy, Color.white);
        cy += SPC;

        cy = Head("Hotkeys", c, cy);
        Text(new Rect(c.x, cy, c.width, LH), "F1 Bật/Tắt   |   F2 Dump UI   |   F3 Teleport test", CDim, _stLbl); cy += LH;
        Text(new Rect(c.x, cy, c.width, LH), "F4 Zone test   |   F5 Go Back   |   F9 Mở/Đóng UI",  CDim, _stLbl); cy += LH;

        if (cfg != null)
        {
            cy += SPC;
            cy = Head("Tuỳ Chọn Nhanh", c, cy);
            cfg.AutoDetectBossNotification = Toggle(new Rect(c.x, cy, c.width, LH),
                cfg.AutoDetectBossNotification, "Auto detect boss qua thông báo chat");
            cy += LH + 2;
            cfg.AutoFusion = Toggle(new Rect(c.x, cy, c.width, LH),
                cfg.AutoFusion, "Tự động hợp thể (nút P) khi tool chạy");
            cy += LH + 2;
            cfg.Enabled = Toggle(new Rect(c.x, cy, c.width, LH), cfg.Enabled, "Auto Boss đang hoạt động");
        }
    }

    // ── Tab 1: Skill Boss ────────────────────────────────────────────────────
    void DrawSkill(Rect c)
    {
        var cfg = Plugin.Instance?.Runner?.Config;
        float cy = c.y;

        cy = Head("Trigger Skill theo HP Boss   (HP thật, không phải %)", c, cy);
        Text(new Rect(c.x, cy, c.width, LH), "HP boss <= ngưỡng → bắn skill. Kiểm từ trên xuống.",
             new Color(0.65f, 0.9f, 0.7f), _stLbl);
        cy += LH + SPC;

        Text(new Rect(c.x,       cy, 130, LH), "HP Ngưỡng",  CAccent, _stLbl);
        Text(new Rect(c.x + 136, cy,  90, LH), "Skill (1-4)", CAccent, _stLbl);
        cy += LH;

        // Danh sách trigger — tự cuộn, tự cull (không dùng BeginScrollView)
        float viewH = 176f;
        Rect view = new Rect(c.x, cy, c.width, viewH);
        Fill(view, new Color(0.07f, 0.07f, 0.13f));
        Frame(view, new Color(0.22f, 0.23f, 0.38f));

        var list = cfg?.BossSkillTriggers;
        int count = list?.Count ?? 0;
        float rowH = LH + 3;
        float contentH = count * rowH + 4;

        if (view.Contains(_mouse) && Mathf.Abs(_wheel) > 0.01f)
            _skillScroll -= _wheel * 34f;
        _skillScroll = Mathf.Clamp(_skillScroll, 0, Mathf.Max(0, contentH - viewH));

        if (count == 0)
        {
            Text(new Rect(view.x + 8, view.y + 6, view.width - 16, LH),
                 "Chưa có trigger nào. Dùng preset bên dưới hoặc tự thêm.", Color.gray, _stLbl);
        }
        else
        {
            int removeAt = -1;
            for (int i = 0; i < count; i++)
            {
                float ry = view.y + 3 + i * rowH - _skillScroll;
                if (ry + rowH < view.y || ry > view.yMax) continue;   // cull ngoài khung

                var t = list[i];
                if (!_buf.ContainsKey(i))
                    _buf[i] = new[] { t.HpThreshold.ToString("F0"), t.SkillKey.ToString() };
                string[] b = _buf[i];

                string nh = Field($"hp{i}", new Rect(view.x + 6,   ry, 126, LH), b[0]);
                string ns = Field($"sk{i}", new Rect(view.x + 138, ry,  76, LH), b[1]);
                if (nh != b[0]) { b[0] = nh; if (float.TryParse(nh, out float fv)) t.HpThreshold = fv; }
                if (ns != b[1]) { b[1] = ns; if (int.TryParse(ns, out int iv) && iv >= 1 && iv <= 4) t.SkillKey = iv; }

                if (Button(new Rect(view.x + 222, ry, 54, LH), "Xoá", CRed)) removeAt = i;
            }
            if (removeAt >= 0)
            {
                list.RemoveAt(removeAt);
                RebuildBuf(cfg);
                _focus = null;
                AddLog("[UI] Đã xoá 1 trigger.");
            }
        }
        cy += viewH + SPC + 2;

        // Thêm trigger
        cy = Head("Thêm trigger mới", c, cy);
        Text(new Rect(c.x, cy, 26, LH), "HP:", CDim, _stLbl);
        _newHp = Field("newHp", new Rect(c.x + 28,  cy, 108, LH), _newHp);
        Text(new Rect(c.x + 142, cy, 38, LH), "Skill:", CDim, _stLbl);
        _newSk = Field("newSk", new Rect(c.x + 182, cy, 44, LH), _newSk);
        if (Button(new Rect(c.x + 232, cy, 72, LH), "+ Thêm", CGreen))
        {
            if (cfg != null && float.TryParse(_newHp, out float hp)
                && int.TryParse(_newSk, out int sk) && sk >= 1 && sk <= 4)
            {
                cfg.BossSkillTriggers.Add(new SkillTrigger { HpThreshold = hp, SkillKey = sk });
                _buf.Clear(); _newHp = "100000"; _newSk = "1"; _focus = null;
                AddLog($"[UI] Thêm trigger HP<={hp:N0} → skill {sk}");
            }
            else AddLog("[UI] Giá trị không hợp lệ (skill phải 1-4).");
        }
        cy += LH + SPC + 2;

        cy = Head("Preset nhanh", c, cy);
        float bw = (c.width - 4) / 2f;
        if (Button(new Rect(c.x,          cy, bw - 2, 24), "4 mốc chuẩn",    CBlue)) Preset4(cfg);
        if (Button(new Rect(c.x + bw + 2, cy, bw - 2, 24), "1 mốc đơn giản", CGray)) Preset1(cfg);
        cy += 28 + SPC;

        Text(new Rect(c.x, cy, c.width, LH),
             _bossHp > 0 ? $"► Boss HP hiện tại: {_bossHp:N0}   ← dùng số này để chỉnh ngưỡng"
                         : "► Boss HP: N/A (chỉ hiện khi đang combat boss)",
             _bossHp > 0 ? new Color(1f, 0.7f, 0.3f) : Color.gray, _stLbl);
    }

    void Preset4(AutoBossConfig c)
    {
        if (c == null) return;
        c.BossSkillTriggers.Clear(); _buf.Clear(); _focus = null;
        c.BossSkillTriggers.Add(new SkillTrigger { HpThreshold = 500000f, SkillKey = 1 });
        c.BossSkillTriggers.Add(new SkillTrigger { HpThreshold = 200000f, SkillKey = 2 });
        c.BossSkillTriggers.Add(new SkillTrigger { HpThreshold =  50000f, SkillKey = 3 });
        c.BossSkillTriggers.Add(new SkillTrigger { HpThreshold =  10000f, SkillKey = 4 });
        AddLog("[UI] Áp preset 4 mốc.");
    }

    void Preset1(AutoBossConfig c)
    {
        if (c == null) return;
        c.BossSkillTriggers.Clear(); _buf.Clear(); _focus = null;
        c.BossSkillTriggers.Add(new SkillTrigger { HpThreshold = 100000f, SkillKey = 1 });
        AddLog("[UI] Áp preset 1 mốc.");
    }

    void RebuildBuf(AutoBossConfig c)
    {
        _buf.Clear();
        var l = c?.BossSkillTriggers;
        if (l == null) return;
        for (int i = 0; i < l.Count; i++)
            _buf[i] = new[] { l[i].HpThreshold.ToString("F0"), l[i].SkillKey.ToString() };
    }

    // ── Tab 2: Quản Lý Item ──────────────────────────────────────────────────
    void DrawItemManager(Rect c)
    {
        var cfg = Plugin.Instance?.Runner?.Config;
        float cy = c.y;

        if (cfg == null)
        {
            Text(new Rect(c.x, cy, c.width, LH), "Config chưa sẵn sàng.", Color.gray, _stLbl);
            return;
        }

        // --- SECTION: Tự Động Sử Dụng ---
        cy = Head("Danh Sách Tự Động Sử Dụng (Mở Rương/Hộp)", c, cy);
        cy = FField("Delay (giây):", ref _bItemDelay, ref cfg.AutoItemDelaySec, c, cy, "fItemDelay");
        
        Text(new Rect(c.x, cy, 100, LH), "Thêm Item ID:", CDim, _stLbl);
        _newItemUseId = Field("newUId", new Rect(c.x + 100, cy, 80, LH), _newItemUseId);
        if (Button(new Rect(c.x + 190, cy, 70, LH), "+ Thêm", CGreen))
        {
            if (int.TryParse(_newItemUseId, out int uid) && !cfg.AutoUseItemIds.Contains(uid))
            {
                cfg.AutoUseItemIds.Add(uid);
                _newItemUseId = ""; _focus = null;
                AddLog($"[UI] Thêm ID {uid} vào danh sách Tự Dùng.");
            }
        }
        if (Button(new Rect(c.x + 270, cy, 90, LH), "🔍 Quét ID", CBlue))
        {
            GameAPI.DumpBagItemsToLog();
            _focus = null;
        }
        cy += LH + SPC;

        float viewH = 100f;
        Rect viewUse = new Rect(c.x, cy, c.width, viewH);
        Fill(viewUse, new Color(0.07f, 0.07f, 0.13f));
        Frame(viewUse, new Color(0.22f, 0.23f, 0.38f));
        
        var uList = cfg.AutoUseItemIds;
        int uCount = uList.Count;
        float rowH = LH + 2;
        float uContentH = uCount * rowH + 4;
        
        if (viewUse.Contains(_mouse) && Mathf.Abs(_wheel) > 0.01f) _itemUseScroll -= _wheel * 34f;
        _itemUseScroll = Mathf.Clamp(_itemUseScroll, 0, Mathf.Max(0, uContentH - viewH));

        if (uCount == 0) Text(new Rect(viewUse.x + 8, viewUse.y + 6, viewUse.width, LH), "Trống.", Color.gray, _stLbl);
        else
        {
            int rmvIdx = -1;
            for (int i = 0; i < uCount; i++)
            {
                float ry = viewUse.y + 3 + i * rowH - _itemUseScroll;
                if (ry + rowH < viewUse.y || ry > viewUse.yMax) continue;
                Text(new Rect(viewUse.x + 8, ry, 120, LH), $"[ID: {uList[i]}]", CAccent, _stLbl);
                if (Button(new Rect(viewUse.x + 140, ry, 50, LH), "Xoá", CRed)) rmvIdx = i;
            }
            if (rmvIdx >= 0) { uList.RemoveAt(rmvIdx); _focus = null; AddLog("[UI] Đã xoá ID khỏi danh sách Dùng."); }
        }
        cy += viewH + SPC * 2;

        // --- SECTION: Tự Động Vứt ---
        cy = Head("Danh Sách Tự Động Vứt (Có Hạn Sử Dụng)", c, cy);
        Text(new Rect(c.x, cy, 100, LH), "Thêm Item ID:", CDim, _stLbl);
        _newItemDropId = Field("newDId", new Rect(c.x + 100, cy, 80, LH), _newItemDropId);
        if (Button(new Rect(c.x + 190, cy, 70, LH), "+ Thêm", CRed))
        {
            if (int.TryParse(_newItemDropId, out int did) && !cfg.AutoDropItemIds.Contains(did))
            {
                cfg.AutoDropItemIds.Add(did);
                _newItemDropId = ""; _focus = null;
                AddLog($"[UI] Thêm ID {did} vào danh sách Tự Vứt.");
            }
        }
        cy += LH + SPC;

        Rect viewDrop = new Rect(c.x, cy, c.width, viewH);
        Fill(viewDrop, new Color(0.07f, 0.07f, 0.13f));
        Frame(viewDrop, new Color(0.22f, 0.23f, 0.38f));

        var dList = cfg.AutoDropItemIds;
        int dCount = dList.Count;
        float dContentH = dCount * rowH + 4;

        if (viewDrop.Contains(_mouse) && Mathf.Abs(_wheel) > 0.01f) _itemDropScroll -= _wheel * 34f;
        _itemDropScroll = Mathf.Clamp(_itemDropScroll, 0, Mathf.Max(0, dContentH - viewH));

        if (dCount == 0) Text(new Rect(viewDrop.x + 8, viewDrop.y + 6, viewDrop.width, LH), "Trống.", Color.gray, _stLbl);
        else
        {
            int rmvIdx = -1;
            for (int i = 0; i < dCount; i++)
            {
                float ry = viewDrop.y + 3 + i * rowH - _itemDropScroll;
                if (ry + rowH < viewDrop.y || ry > viewDrop.yMax) continue;
                Text(new Rect(viewDrop.x + 8, ry, 120, LH), $"[ID: {dList[i]}]", CAccent, _stLbl);
                if (Button(new Rect(viewDrop.x + 140, ry, 50, LH), "Xoá", CRed)) rmvIdx = i;
            }
            if (rmvIdx >= 0) { dList.RemoveAt(rmvIdx); _focus = null; AddLog("[UI] Đã xoá ID khỏi danh sách Vứt."); }
        }
    }

    // ── Tab 3: Cấu Hình ──────────────────────────────────────────────────────
    void DrawConfig(Rect c)
    {
        var cfg = Plugin.Instance?.Runner?.Config;
        float cy = c.y;

        if (cfg == null)
        {
            Text(new Rect(c.x, cy, c.width, LH), "Config chưa sẵn sàng.", Color.gray, _stLbl);
            return;
        }

        cy = Head("Chiến Đấu", c, cy);
        cy = FField("Attack range:",       ref _bAtkRange,  ref cfg.AttackRange,      c, cy, "f1");
        cy = FField("Combat timeout (s):", ref _bCombatTo,  ref cfg.CombatTimeoutSec, c, cy, "f2");
        cy = FField("Retreat HP %:",       ref _bRetreatHp, ref cfg.RetreatHpPct,     c, cy, "f3");
        cy += SPC;

        cy = Head("Nhặt Đồ & Scan", c, cy);
        cy = FField("Loot radius:",        ref _bLootR,     ref cfg.LootRadius,       c, cy, "f4");
        cy = FField("Scan boss mỗi (s):",  ref _bScanEvery, ref cfg.ScanBossEverySec, c, cy, "f5");
        cy += SPC;

        cy = Head("Thông Báo", c, cy);
        cfg.AutoDetectBossNotification = Toggle(new Rect(c.x, cy, c.width, LH),
            cfg.AutoDetectBossNotification, "Auto detect boss qua thông báo chat");
        cy += LH + 2;
        cfg.AutoFusion = Toggle(new Rect(c.x, cy, c.width, LH),
            cfg.AutoFusion, "Tự động hợp thể (nút P) khi tool chạy");
        cy += LH + SPC * 2;

        if (Button(new Rect(c.x, cy, c.width, 26), "Lưu config ra file JSON",  CBlue)) SaveCfg(cfg);
        cy += 30;
        if (Button(new Rect(c.x, cy, c.width, 26), "Load config từ file JSON", CGray)) LoadCfg(cfg);
        cy += 30 + SPC;

        Text(new Rect(c.x, cy, c.width, LH * 2), CfgPath, Color.gray, _stLog);
    }

    // ── Tab 3: Log ───────────────────────────────────────────────────────────
    void DrawLogTab(Rect c)
    {
        float cy = c.y;

        Text(new Rect(c.x, cy, c.width - 60, LH), $"Log gần đây ({_logs.Count})", CAccent, _stTitle);
        if (Button(new Rect(c.x + c.width - 52, cy, 52, 20), "Xoá", CGray)) _logs.Clear();
        cy += LH + 2;

        Rect view = new Rect(c.x, cy, c.width, c.yMax - cy);
        Fill(view, new Color(0.05f, 0.06f, 0.10f));
        Frame(view, new Color(0.20f, 0.22f, 0.36f));

        string[] arr = _logs.ToArray();
        float lineH = 15f;
        float contentH = arr.Length * lineH + 6;

        if (view.Contains(_mouse) && Mathf.Abs(_wheel) > 0.01f)
            _logScroll -= _wheel * 45f;
        _logScroll = Mathf.Clamp(_logScroll, 0, Mathf.Max(0, contentH - view.height));

        // Mới nhất trên cùng
        int n = arr.Length;
        for (int k = 0; k < n; k++)
        {
            float ly = view.y + 3 + k * lineH - _logScroll;
            if (ly + lineH < view.y || ly > view.yMax) continue;      // cull
            Text(new Rect(view.x + 5, ly, view.width - 10, lineH), arr[n - 1 - k],
                 new Color(0.76f, 0.92f, 0.78f), _stLog);
        }
    }

    // ── Widget tự viết (không dùng control IMGUI) ─────────────────────────────

    /// <summary>Click = nhấn xuống VÀ nhả ra đều trong rect.</summary>
    bool TakeClick(Rect r)
    {
        if (!_pendingClick) return false;
        if (!r.Contains(_clickPos) || !r.Contains(_pressPos)) return false;
        _pendingClick = false;
        return true;
    }

    bool Button(Rect r, string label, Color bg) => Button(r, label, bg, Color.white);

    bool Button(Rect r, string label, Color bg, Color textCol)
    {
        bool hover = r.Contains(_mouse);
        bool held  = hover && _mouseHeld && r.Contains(_pressPos);

        Fill(r, held ? Shade(bg, 1.45f) : hover ? Shade(bg, 1.20f) : bg);
        Frame(r, hover ? CAccent : new Color(0f, 0f, 0f, 0.55f));
        Text(new Rect(r.x, r.y + (r.height - 18f) / 2f, r.width, 18f), label, textCol, _stBtn);

        return TakeClick(r);
    }

    bool Toggle(Rect r, bool val, string label)
    {
        Rect box = new Rect(r.x, r.y + 3, 16, 16);
        bool hover = r.Contains(_mouse);

        Fill(box, val ? CGreen : CField);
        Frame(box, hover ? CAccent : new Color(0.4f, 0.42f, 0.6f));
        if (val) Text(new Rect(box.x, box.y - 1, box.width, box.height), "✓", Color.white, _stBtn);

        Text(new Rect(r.x + 22, r.y + 2, r.width - 22, LH), label, hover ? Color.white : CDim, _stLbl);

        return TakeClick(r) ? !val : val;
    }

    /// <summary>Text field tự quản focus + gõ chữ qua Input.inputString.</summary>
    string Field(string id, Rect r, string val)
    {
        val ??= "";
        bool focused = _focus == id;
        bool hover   = r.Contains(_mouse);

        if (TakeClick(r)) { _focus = id; _focusClaimed = true; focused = true; }
        else if (focused) _focusClaimed = true;

        Fill(r, focused ? new Color(0.18f, 0.19f, 0.32f) : CField);
        Frame(r, focused ? CAccent : hover ? new Color(0.45f, 0.48f, 0.7f) : new Color(0.3f, 0.32f, 0.48f));

        if (focused)
        {
            if (_backspaces > 0 && val.Length > 0)
                val = val.Substring(0, Mathf.Max(0, val.Length - _backspaces));
            if (_typed.Length > 0) val += _typed;
            if (val.Length > 18) val = val.Substring(0, 18);
        }

        string shown = focused && (Time.unscaledTime % 1f) < 0.5f ? val + "|" : val;
        Text(new Rect(r.x + 5, r.y + 2, r.width - 10, r.height - 4), shown, Color.white, _stLbl);

        return val;
    }

    // ── Vẽ nguyên thuỷ: CHỈ GUI.Box + GUI.Label ──────────────────────────────
    // GUI.DrawTexture bị IL2CPP strip trong game này ("Method unstripping failed").
    // GUI.Box(rect, GUIContent.none, style) thì chạy được → dùng làm hàm tô màu chính.
    // _fillMode tự dò 1 lần rồi nhớ: 0=GUI.Box, 1=GUI.DrawTexture, 2=bỏ tô (chỉ chữ).
    private int _fillMode = 0;

    void Fill(Rect r, Color c)
    {
        if (_fillMode == 2) return;

        Color old = GUI.color;
        GUI.color = c;
        try
        {
            if (_fillMode == 0) GUI.Box(r, GUIContent.none, _stFill);
            else                GUI.DrawTexture(r, _px);
        }
        catch (Exception ex)
        {
            _fillMode++;
            Plugin.Log?.LogWarning($"[UI] Fill mode {_fillMode - 1} fail ({ex.Message}) → thử mode {_fillMode}");
        }
        GUI.color = old;
    }

    void Frame(Rect r, Color c, float t = 1f)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    void Text(Rect r, string s, Color c, GUIStyle st)
    {
        if (string.IsNullOrEmpty(s)) return;
        Color old = GUI.color;
        GUI.color = c;
        // GUI.Label đã xác nhận chạy được trong game này, nhưng vẫn bọc để 1 chuỗi lỗi
        // (font thiếu glyph, style null) không làm chết cả pass vẽ.
        try { GUI.Label(r, s, st ?? _stLbl); } catch { }
        GUI.color = old;
    }

    static Color Shade(Color c, float f) => new Color(
        Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);

    // ── Helper layout ────────────────────────────────────────────────────────
    float Head(string text, Rect c, float cy)
    {
        Text(new Rect(c.x, cy, c.width, LH), text, CAccent, _stTitle);
        Fill(new Rect(c.x, cy + LH - 2, c.width, 1), new Color(0.3f, 0.32f, 0.5f));
        return cy + LH;
    }

    float Row(string label, string val, Rect c, float cy, Color col)
    {
        Text(new Rect(c.x, cy, 100, LH), label + ":", CDim, _stLbl);
        Text(new Rect(c.x + 104, cy, c.width - 104, LH), val, col, _stLbl);
        return cy + LH;
    }

    float FField(string label, ref string buf, ref float val, Rect c, float cy, string id)
    {
        Text(new Rect(c.x, cy, 158, LH), label, CDim, _stLbl);
        string n = Field(id, new Rect(c.x + 162, cy, 96, LH), buf);
        if (n != buf) { buf = n; if (float.TryParse(n, out float v)) val = v; }
        return cy + LH + 3;
    }

    Color SCol(string s) => s switch
    {
        "CombatBoss"                       => new Color(1f, 0.4f, 0.3f),
        "ZoneScanLoop"                     => new Color(1f, 0.85f, 0.3f),
        "MoveToBoss"                       => new Color(0.3f, 0.9f, 1f),
        "LootDrops"                        => new Color(0.4f, 1f, 0.6f),
        "TeleportToBossMap" or
        "TeleportHome"                     => new Color(0.8f, 0.5f, 1f),
        "Idle"                             => Color.gray,
        _                                  => Color.white,
    };

    void SyncBufs()
    {
        var cfg = Plugin.Instance?.Runner?.Config;
        if (cfg == null) return;
        _bAtkRange  = cfg.AttackRange.ToString("F1");
        _bCombatTo  = cfg.CombatTimeoutSec.ToString("F0");
        _bRetreatHp = cfg.RetreatHpPct.ToString("F0");
        _bLootR     = cfg.LootRadius.ToString("F0");
        _bScanEvery = cfg.ScanBossEverySec.ToString("F1");
        _bItemDelay = cfg.AutoItemDelaySec.ToString("F1");
        _buf.Clear();
        _focus = null;
    }

    static string CfgPath => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "AutoBossConfig.json");

    // JsonSerializerOptions dùng chung — bắt buộc có IncludeFields=true vì
    // AutoBossConfig/SkillTrigger dùng public fields, không phải properties.
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts =
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IncludeFields = true };

    void SaveCfg(AutoBossConfig cfg)
    {
        try
        {
            System.IO.File.WriteAllText(CfgPath,
                System.Text.Json.JsonSerializer.Serialize(cfg, _jsonOpts));
            AddLog($"[UI] Config đã lưu → {CfgPath}");
        }
        catch (Exception ex) { AddLog($"[UI] Lưu lỗi: {ex.Message}"); }
    }

    void LoadCfg(AutoBossConfig cfg)
    {
        try
        {
            if (!System.IO.File.Exists(CfgPath)) { AddLog("[UI] File config chưa tồn tại."); return; }
            var ld = System.Text.Json.JsonSerializer.Deserialize<AutoBossConfig>(
                System.IO.File.ReadAllText(CfgPath), _jsonOpts);
            if (ld == null) return;
            cfg.AttackRange                = ld.AttackRange;
            cfg.CombatTimeoutSec           = ld.CombatTimeoutSec;
            cfg.RetreatHpPct               = ld.RetreatHpPct;
            cfg.LootRadius                 = ld.LootRadius;
            cfg.ScanBossEverySec           = ld.ScanBossEverySec;
            cfg.AutoDetectBossNotification = ld.AutoDetectBossNotification;
            cfg.AutoFusion                 = ld.AutoFusion;
            if (ld.BossSkillTriggers != null) cfg.BossSkillTriggers = ld.BossSkillTriggers;
            SyncBufs();
            AddLog("[UI] Config đã load.");
        }
        catch (Exception ex) { AddLog($"[UI] Load lỗi: {ex.Message}"); }
    }

    /// <summary>Auto-load config từ file JSON nếu tồn tại. Gọi từ Plugin.Load() sau khi Runner sẵn sàng.</summary>
    public static void TryAutoLoad()
    {
        try
        {
            string path = CfgPath;
            if (!System.IO.File.Exists(path)) return;
            var opts = _jsonOpts;
            var ld = System.Text.Json.JsonSerializer.Deserialize<AutoBossConfig>(
                System.IO.File.ReadAllText(path), opts);
            if (ld == null) return;

            var cfg = Plugin.Instance?.Runner?.Config;
            if (cfg == null) return;

            cfg.AttackRange                = ld.AttackRange;
            cfg.CombatTimeoutSec           = ld.CombatTimeoutSec;
            cfg.RetreatHpPct               = ld.RetreatHpPct;
            cfg.LootRadius                 = ld.LootRadius;
            cfg.ScanBossEverySec           = ld.ScanBossEverySec;
            cfg.AutoDetectBossNotification = ld.AutoDetectBossNotification;
            cfg.AutoFusion                 = ld.AutoFusion;
            cfg.AutoItemDelaySec           = ld.AutoItemDelaySec;
            if (ld.AutoUseItemIds != null) cfg.AutoUseItemIds = ld.AutoUseItemIds;
            if (ld.AutoDropItemIds != null) cfg.AutoDropItemIds = ld.AutoDropItemIds;
            if (ld.BossSkillTriggers != null) cfg.BossSkillTriggers = ld.BossSkillTriggers;

            Plugin.Log.LogInfo($"[UI] Auto-loaded config từ {path}");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[UI] TryAutoLoad fail: {ex.Message}");
        }
    }

    // ── Style: chỉ dùng cho GUI.Label nên rất tối giản ───────────────────────
    void Build()
    {
        _px = new Texture2D(1, 1);
        _px.SetPixel(0, 0, Color.white);
        _px.Apply();

        // Style tô màu: texture trắng + border/padding = 0 để GUI.Box phủ kín đúng rect.
        // GUI.color sẽ nhân vào texture trắng → ra màu mong muốn.
        // new GUIStyle() mặc định đã có border/padding/margin = 0 nên không cần set
        // (RectOffset trong Il2CppInterop không có ctor 4 tham số).
        _stFill = new GUIStyle();
        _stFill.normal.background = _px;

        _stTitle = new GUIStyle();
        _stTitle.fontSize  = 13;
        _stTitle.fontStyle = FontStyle.Bold;
        _stTitle.alignment = TextAnchor.MiddleLeft;
        _stTitle.normal.textColor = Color.white;

        _stLbl = new GUIStyle();
        _stLbl.fontSize  = 12;
        _stLbl.alignment = TextAnchor.MiddleLeft;
        _stLbl.normal.textColor = Color.white;

        _stLblR = new GUIStyle();
        _stLblR.fontSize  = 12;
        _stLblR.alignment = TextAnchor.MiddleRight;
        _stLblR.normal.textColor = Color.white;

        _stBtn = new GUIStyle();
        _stBtn.fontSize  = 12;
        _stBtn.alignment = TextAnchor.MiddleCenter;
        _stBtn.normal.textColor = Color.white;

        _stLog = new GUIStyle();
        _stLog.fontSize  = 11;
        _stLog.alignment = TextAnchor.MiddleLeft;
        _stLog.normal.textColor = new Color(0.76f, 0.92f, 0.78f);
    }
}
