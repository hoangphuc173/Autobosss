using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace AutoBossGrabber;

/// <summary>
/// Đổi khu vực (zone) theo thứ tự: Khu 0 → Khu 1 → Khu 2 → ...
///
/// Flow mỗi lần NextZone() được gọi:
///   1. Click ZoneObject/Button trên HUD để mở panel chọn khu.
///   2. Ngay sau đó, tìm và click button "Khu {_targetZone}" trong panel.
///   3. Nếu panel chưa render kịp → _panelJustOpened = true, lần gọi tiếp
///      sẽ bỏ qua bước mở panel và click thẳng vào Khu N.
///   4. Khi vào map mới (ZoneScanLoop reset), gọi ResetState() → về Khu 0.
/// </summary>
public static class ZoneSwitcher
{
    private struct ZoneCandidate
    {
        public Button Btn;
        public int Idx;
        public string Text;
        public bool FromPanel;

        public ZoneCandidate(Button btn, int idx, string text, bool fromPanel)
        {
            Btn = btn;
            Idx = idx;
            Text = text;
            FromPanel = fromPanel;
        }
    }

    // Khu muốn chọn tiếp theo (tăng dần: 0, 1, 2, ...)
    private static int _targetZone = 0;

    // Tổng số khu trong map hiện tại (-1 = chưa biết)
    private static int _totalZones = -1;

    // 2-step state: đã click ZoneObject mở panel → lần sau click Khu N bên trong
    private static bool _panelJustOpened = false;
    // Số lần đã thử click Khu sau khi panel mở nhưng button chưa render.
    // Cho retry NHANH (mỗi frame) tối đa MaxPanelOpenRetries lần thay vì phí 3s cooldown mỗi lần.
    private static int _panelOpenRetries = 0;
    private const int MaxPanelOpenRetries = 30;

    // Đã dump button 1 lần khi fail (debug)
    private static bool _dumpedOnFail = false;

    // Boss KHÔNG BAO GIỜ spawn ở khu loạn chiến. Nếu nhân vật đang up ở khu Lc thì
    // panel mở ra sẽ ở tab loạn chiến (chỉ có Lc 0, Lc 1) -> quét ở đó là quét sai chỗ.
    // Cờ này bắt NextZone() click tab "Khu thường" MỘT LẦN ngay sau khi mở panel.
    private static bool _forceNormalTabPending = false;

    // Khu vừa click, chờ HUD xác nhận đã thật sự đổi khu
    private static int _pendingZoneIdx = -1;
    private static float _pendingZoneClickedAt = -999f;
    private static float _lastPendingZoneLogAt = -999f;
    private static float _panelSuppressUntil = -999f;
    private const float ZoneConfirmTimeoutSec = 6f;
    private const float ZoneConfirmMinAgeSec = 0.6f;
    private const float ReopenPanelGuardSec = 0.9f;

    // === ADAPTIVE DWELL: thay vì đợi cố định, điều chỉnh theo số mob thực tế ===
    private static float _lastMobCheckAt = -999f;
    private static int _cachedMobCount = 0;
    private const float MobCheckInterval = 0.8f; // check mob count mỗi 0.8s thay vì 1s

    /// <summary>true khi đã quét hết tất cả khu (không có boss).</summary>
    public static bool IsExhausted => _totalZones > 0 && _targetZone >= _totalZones;

    /// <summary>Tổng số khu phát hiện được (-1 = chưa biết).</summary>
    public static int TotalZones => _totalZones;

    /// <summary>true khi đã đọc được tổng số khu từ panel.</summary>
    public static bool HasKnownTotalZones => _totalZones > 0;

    /// <summary>true khi đang chờ HUD xác nhận khu mới.</summary>
    public static bool IsWaitingForZoneChange => _pendingZoneIdx >= 0;

    /// <summary>true khi panel đã mở nhưng đang chờ button "Khu N" render (nên call lại ngay frame sau).</summary>
    public static bool IsWaitingForPanelRender => _panelJustOpened;

    /// <summary>Khu đang chờ xác nhận (-1 nếu không có).</summary>
    public static int PendingZoneIndex => _pendingZoneIdx;

    /// <summary>true nếu lần gọi NextZone gần nhất đã click/chọn một khu thật sự.</summary>
    public static bool LastActionClickedZone { get; private set; } = false;

    /// <summary>Index khu đã click ở lần gọi NextZone gần nhất.</summary>
    public static int LastClickedZoneIndex { get; private set; } = -1;

    /// <summary>Trả về số mob alive trong zone hiện tại (cached, update mỗi 0.8s).</summary>
    public static int GetCachedMobCount()
    {
        if (Time.time - _lastMobCheckAt >= MobCheckInterval)
        {
            _lastMobCheckAt = Time.time;
            _cachedMobCount = 0;
            try
            {
                var mobs = GameAPI.FindAllMobs();
                if (mobs != null)
                    foreach (var m in mobs)
                        if (GameAPI.IsMobAlive(m)) _cachedMobCount++;
                var npcs = GameAPI.FindAllNPCs();
                if (npcs != null)
                    foreach (var n in npcs)
                        if (GameAPI.IsMobAlive(n)) _cachedMobCount++;
            }
            catch { }
        }
        return _cachedMobCount;
    }

    /// <summary>
    /// Đặt mục tiêu khu cụ thể (lệnh SWITCH_ZONE từ Manager).
    /// Lần NextZone() kế tiếp sẽ mở panel và click đúng "Khu N" này
    /// thay vì tiếp tục quét tuần tự.
    /// </summary>
    public static void SetTargetZone(int zone)
    {
        _targetZone = Math.Max(1, zone);
        _pendingZoneIdx = -1;          // hủy pending cũ nếu có
        _panelJustOpened = false;
        _panelOpenRetries = 0;
        Plugin.Log.LogInfo($"[ZoneSwitcher] Target zone set → Khu {_targetZone} (remote command)");
    }

    /// <summary>Gọi khi vào map mới để reset về Khu 0.</summary>
    public static void ResetState()
    {
        _targetZone = 0;
        _totalZones = -1;
        _panelJustOpened = false;
        _panelOpenRetries = 0;
        _dumpedOnFail = false;
        _pendingZoneIdx = -1;
        _pendingZoneClickedAt = -999f;
        _lastPendingZoneLogAt = -999f;
        _panelSuppressUntil = -999f;
        _forceNormalTabPending = true;   // luôn dò boss ở tab khu thường
        LastActionClickedZone = false;
        LastClickedZoneIndex = -1;
        _lastMobCheckAt = -999f;
        _cachedMobCount = 0;
        Plugin.Log.LogInfo("[ZoneSwitcher] Reset → targetZone=0, totalZones=unknown, forceNormalTab=true");
    }

    // ===================================================================
    // Restore: quay lại đúng tab + đúng Khu đã lưu (dùng cho ReturnToFarmMap flow)
    // Tách riêng khỏi NextZone() vì mục đích khác: chọn 1 khu CỐ ĐỊNH,
    // không phải quét tuần tự Khu 0 → Khu N.
    // ===================================================================

    public enum ZoneTab
    {
        Normal,
        Chaotic
    }

    private enum RestorePhase
    {
        Idle,
        ClickTab,
        ClickZone,
        WaitConfirm,
        Done,
        Failed
    }

    private static RestorePhase _restorePhase = RestorePhase.Idle;
    private static int _restoreZoneTarget = -1;
    private static ZoneTab _restoreTab = ZoneTab.Chaotic;
    private static bool _restoreTriedFallbackTab = false;
    private static bool _restoreTriedFallbackZone = false;
    private static float _restorePhaseStartedAt = -999f;
    private static int _restorePendingZoneIdx = -1;
    private static float _restorePendingClickedAt = -999f;
    private static float _lastRestoreLogAt = -999f;
    private static float _restorePanelOpenedAt = -1f;
    private const float RestorePhaseTimeoutSec = 5f;

    /// <summary>true khi có 1 lượt restore đang chạy (chưa Done/Failed).</summary>
    public static bool IsRestoring => _restorePhase != RestorePhase.Idle && _restorePhase != RestorePhase.Done && _restorePhase != RestorePhase.Failed;

    /// <summary>true khi restore vừa hoàn tất thành công (Khu đã được xác nhận trên HUD).</summary>
    public static bool RestoreSucceeded => _restorePhase == RestorePhase.Done;

    /// <summary>true khi restore đã bỏ cuộc (không click được tab/khu nào, hoặc HUD không xác nhận).</summary>
    public static bool RestoreFailed => _restorePhase == RestorePhase.Failed;

    /// <summary>Bắt đầu quy trình khôi phục: ưu tiên tab preferredTab, click đúng Khu zoneIndex.</summary>
    public static void StartRestore(int zoneIndex, ZoneTab preferredTab)
    {
        _restorePhase = RestorePhase.ClickTab;
        _restoreZoneTarget = Math.Max(0, zoneIndex);
        _restoreTab = preferredTab;
        _restoreTriedFallbackTab = false;
        _restoreTriedFallbackZone = false;
        _restorePhaseStartedAt = Time.time;
        _restorePendingZoneIdx = -1;
        _restorePendingClickedAt = -999f;
        _restorePanelOpenedAt = -1f;
        Plugin.Log.LogInfo($"[ZoneSwitcher] StartRestore(zone={_restoreZoneTarget}, tab={preferredTab})");
    }

    /// <summary>
    /// Gọi mỗi frame trong lúc State == RestoreFarmZone.
    /// Trả về true khi đã xong (kiểm tra RestoreSucceeded/RestoreFailed để biết kết quả).
    /// </summary>
    public static bool TickRestore()
    {
        switch (_restorePhase)
        {
            case RestorePhase.Idle:
            case RestorePhase.Done:
            case RestorePhase.Failed:
                return true;

            case RestorePhase.ClickTab:
            {
                bool opened = TryOpenZonePanel();
                if (!opened)
                {
                    _restorePanelOpenedAt = -1f;
                    return false;
                }

                if (_restorePanelOpenedAt < 0f)
                    _restorePanelOpenedAt = Time.time;

                // Chờ 0.5s sau khi panel mở để UI kịp animate trước khi click tab
                if (Time.time - _restorePanelOpenedAt < 0.5f)
                    return false;

                bool tabClicked = TryClickZoneTab(_restoreTab);

                // Chỉ advance khi ĐÃ click được tab (hoặc panel không có tab).
                // Nếu opened=true nhưng tabClicked=false → panel đang render, chờ frame sau.
                if (tabClicked)
                {
                    // Tab đã click thành công → cho 1 nhịp render rồi click khu.
                    _restorePhase = RestorePhase.ClickZone;
                    _restorePhaseStartedAt = Time.time;
                    return false;
                }

                if (Time.time - _restorePhaseStartedAt > RestorePhaseTimeoutSec)
                {
                    if (!_restoreTriedFallbackTab)
                    {
                        var other = _restoreTab == ZoneTab.Chaotic ? ZoneTab.Normal : ZoneTab.Chaotic;
                        Plugin.Log.LogWarning($"[ZoneSwitcher] Restore: {_restoreTab} tab unavailable -> fallback {other} tab");
                        _restoreTriedFallbackTab = true;
                        _restoreTab = other;
                        _restorePhaseStartedAt = Time.time;
                        return false;
                    }
                    // Không tìm được tab nào - vẫn thử click Khu trực tiếp (panel có thể không có tab).
                    _restorePhase = RestorePhase.ClickZone;
                    _restorePhaseStartedAt = Time.time;
                }
                return false;
            }

            case RestorePhase.ClickZone:
            {
                if (TryClickSpecificKhu(_restoreZoneTarget, _restoreTab))
                {
                    _restorePendingZoneIdx = _restoreZoneTarget;
                    _restorePendingClickedAt = Time.time;
                    _lastRestoreLogAt = -999f;
                    _restorePhase = RestorePhase.WaitConfirm;
                    return false;
                }

                if (Time.time - _restorePhaseStartedAt > RestorePhaseTimeoutSec)
                {
                    if (!_restoreTriedFallbackZone && _restoreZoneTarget != 0)
                    {
                        Plugin.Log.LogWarning($"[ZoneSwitcher] Restore: Khu {_restoreZoneTarget} not found -> fallback Khu 0");
                        _restoreTriedFallbackZone = true;
                        _restoreZoneTarget = 0;
                        _restorePhaseStartedAt = Time.time;
                        return false;
                    }

                    if (!_restoreTriedFallbackTab)
                    {
                        var other = _restoreTab == ZoneTab.Chaotic ? ZoneTab.Normal : ZoneTab.Chaotic;
                        Plugin.Log.LogWarning($"[ZoneSwitcher] Restore: Khu not found on {_restoreTab} tab -> fallback {other} tab");
                        _restoreTriedFallbackTab = true;
                        _restoreTab = other;
                        _restorePhase = RestorePhase.ClickTab;
                        _restorePhaseStartedAt = Time.time;
                        return false;
                    }

                    Plugin.Log.LogWarning("[ZoneSwitcher] Restore: failed to click any zone button");
                    _restorePhase = RestorePhase.Failed;
                    return true;
                }
                return false;
            }

            case RestorePhase.WaitConfirm:
            {
                int hudZone = GameAPI.GetCurrentZoneIndexFromHUD();
                float age = Time.time - _restorePendingClickedAt;

                // "Khu Lc 0" và "Khu 0" đều cho index 0 → phải so cả loại khu,
                // nếu không sẽ xác nhận sai tab và bỏ nhân vật ở khu thường.
                var hudTab = GameAPI.IsCurrentZoneChaotic() ? ZoneTab.Chaotic : ZoneTab.Normal;
                bool tabMatches = hudTab == _restoreTab;

                if (hudZone == _restorePendingZoneIdx && tabMatches && age >= ZoneConfirmMinAgeSec)
                {
                    Plugin.Log.LogInfo($"[ZoneSwitcher] Restore: confirmed Khu {_restorePendingZoneIdx} (tab={_restoreTab})");
                    _restorePhase = RestorePhase.Done;
                    return true;
                }

                if (age >= ZoneConfirmTimeoutSec)
                {
                    Plugin.Log.LogWarning($"[ZoneSwitcher] Restore: Khu {_restorePendingZoneIdx} not confirmed after {age:F1}s");
                    _restorePhase = RestorePhase.Failed;
                    return true;
                }

                if (Time.time - _lastRestoreLogAt >= 1.5f)
                {
                    _lastRestoreLogAt = Time.time;
                    string hudText = hudZone >= 0 ? $"HUD={hudZone}/{hudTab}" : "HUD=unknown";
                    Plugin.Log.LogInfo($"[ZoneSwitcher] Restore: waiting confirm Khu {_restorePendingZoneIdx} tab={_restoreTab} ({hudText}, age={age:F1}s)");
                }
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Mở panel + click tab "Khu thường" một lần duy nhất mỗi vòng quét.
    /// Trả về true khi đã tiêu thụ lượt gọi NextZone() này (caller nên chờ nhịp sau).
    /// </summary>
    private static bool EnsureNormalTab()
    {
        if (!_forceNormalTabPending)
            return false;

        // Đang ở khu thường rồi thì không cần click tab, đỡ mất 1 nhịp.
        if (!GameAPI.IsCurrentZoneChaotic())
        {
            _forceNormalTabPending = false;
            return false;
        }

        bool opened = TryOpenZonePanel();
        if (!opened && !GameAPI.IsZonePanelVisible())
            return false;   // chưa mở được panel -> để NextZone() chạy flow mở panel bình thường

        if (TryClickZoneTab(ZoneTab.Normal))
        {
            _forceNormalTabPending = false;
            _panelJustOpened = true;   // tab vừa đổi, chờ list khu render rồi click Khu 0
            _panelOpenRetries = 0;
            Plugin.Log.LogInfo("[ZoneSwitcher] Was in Chaotic zone → switched panel to Normal tab before boss scan");
            return true;
        }

        // Không thấy nút tab (panel có thể không có tab) → bỏ qua, đừng chặn vòng quét.
        _forceNormalTabPending = false;
        Plugin.Log.LogWarning("[ZoneSwitcher] Normal tab button not found → scanning current tab as-is");
        return false;
    }

    private static bool TryClickZoneTab(ZoneTab tab)
    {
        try
        {
            string[] namesChaotic = { "ChaoticTab", "Khu loạn chiến", "Loạn chiến", "Loan chien" };
            string[] namesNormal = { "NormalTab", "Khu thường", "Khu thuong" };
            string[] names = tab == ZoneTab.Chaotic ? namesChaotic : namesNormal;

            var allBtnObjects = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));
            if (allBtnObjects == null) return false;

            foreach (var obj in allBtnObjects)
            {
                var btn = obj as Button;
                if (btn == null) continue;
                if (!btn.gameObject.activeInHierarchy) continue;

                string goName = btn.gameObject?.name ?? "";
                string text = UIHelper.GetButtonText(btn);

                foreach (var n in names)
                {
                    if (goName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (!string.IsNullOrEmpty(text) && text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        Plugin.Log.LogInfo($"[ZoneSwitcher] Clicking zone tab '{n}' (go='{goName}' text='{text}')");
                        btn.onClick.Invoke();
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[ZoneSwitcher] TryClickZoneTab fail: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Chuyển sang khu tiếp theo theo thứ tự.
    /// Trả về true nếu đã click thành công (dù panel chưa đổi khu ngay).
    /// </summary>
    public static bool NextZone()
    {
        LastActionClickedZone = false;
        LastClickedZoneIndex = -1;

        if (HandlePendingZone())
            return true;

        if (Time.time < _panelSuppressUntil)
            return false;

        // Trước khi quét khu đầu tiên: ép panel về tab "Khu thường".
        // Nếu nhân vật đang up ở khu loạn chiến thì panel mở ra mặc định ở tab Lc,
        // quét ở đó vô nghĩa vì boss không spawn tại khu loạn chiến.
        if (EnsureNormalTab())
            return true;

        // === Kiểm tra đã quét hết khu chưa ===
        if (IsExhausted)
        {
            Plugin.Log.LogWarning($"[ZoneSwitcher] All {_totalZones} zones scanned, no boss found.");
            return false;
        }

        // === Bước A: Panel đã mở ở lần call trước → click Khu N bên trong ===
        if (_panelJustOpened)
        {
            if (TryClickSpecificKhu(_targetZone))
            {
                _panelJustOpened = false;
                _panelOpenRetries = 0;
                Plugin.Log.LogInfo($"[ZoneSwitcher] Panel open from last call → clicked Khu {_targetZone}");
                StartPendingZone(_targetZone, null);
                return true;
            }

            // Button "Khu N" chưa render kịp → RETRY NHANH (mỗi frame) tối đa MaxPanelOpenRetries lần,
            // thay vì rơi xuống fail rồi phí cả 3s cooldown ở caller (nguyên nhân chậm ~3s/khu trước đây).
            if (_panelOpenRetries < MaxPanelOpenRetries)
            {
                _panelOpenRetries++;
                if (_panelOpenRetries == 1)
                    Plugin.Log.LogInfo($"[ZoneSwitcher] Panel open, Khu {_targetZone} not rendered yet → fast-retry (max {MaxPanelOpenRetries})");
                return true; // giữ _panelJustOpened=true, click lại ngay frame sau
            }

            // Hết ngân sách retry → thử reflection/text rồi mới bỏ cuộc.
            _panelJustOpened = false;
            _panelOpenRetries = 0;
            Plugin.Log.LogWarning($"[ZoneSwitcher] Khu {_targetZone} not found after {MaxPanelOpenRetries} render-retries → fallback");
            if (TryCallReflectionNextZone()) { StartPendingZone(_targetZone, "reflection"); return true; }
            if (ClickZoneTextButton()) { StartPendingZone(_targetZone, "text"); return true; }

            if (!_dumpedOnFail) { _dumpedOnFail = true; DumpAllButtonsToLog(); }
            return false;
        }

        // === Bước B: Mở panel và click Khu N ===
        // 1) Mở panel (click ZoneObject/Button trên HUD)
        bool panelOpened = TryOpenZonePanel();

        // 2) Thử click Khu _targetZone ngay (panel có thể mở synchronously)
        if (TryClickSpecificKhu(_targetZone))
        {
            StartPendingZone(_targetZone, null);
            return true;
        }

        if (panelOpened)
        {
            // Panel vừa được mở nhưng Khu buttons chưa render → chờ call tiếp (retry nhanh ở Bước A)
            _panelJustOpened = true;
            _panelOpenRetries = 0;
            Plugin.Log.LogInfo($"[ZoneSwitcher] Panel opened, Khu {_targetZone} not ready yet → will retry next call");
            return true; // Không phải fail, sẽ click khu ở call tiếp theo
        }

        // 3) Không mở được panel → thử fallback
        if (TryCallReflectionNextZone()) { StartPendingZone(_targetZone, "reflection"); return true; }
        if (ClickZoneTextButton()) { StartPendingZone(_targetZone, "text"); return true; }

        if (!_dumpedOnFail) { _dumpedOnFail = true; DumpAllButtonsToLog(); }
        return false;
    }

    private static bool HandlePendingZone()
    {
        if (_pendingZoneIdx < 0)
            return false;

        int hudZone = GameAPI.GetCurrentZoneIndexFromHUD();
        float age = Time.time - _pendingZoneClickedAt;

        // Vòng quét boss chỉ chạy ở khu thường, nên "Khu Lc N" (cùng index) KHÔNG
        // được tính là đã đổi khu thành công.
        bool onNormalTab = !GameAPI.IsCurrentZoneChaotic();

        if (hudZone == _pendingZoneIdx && onNormalTab && age >= ZoneConfirmMinAgeSec)
        {
            CommitZoneConfirmed(_pendingZoneIdx);
            return true;
        }

        if (age >= ZoneConfirmTimeoutSec)
        {
            Plugin.Log.LogWarning($"[ZoneSwitcher] Pending Khu {_pendingZoneIdx} not confirmed after {age:F1}s -> retry same zone");
            _pendingZoneIdx = -1;
            _pendingZoneClickedAt = -999f;
            _lastPendingZoneLogAt = -999f;
            LastActionClickedZone = false;
            LastClickedZoneIndex = -1;
            return true;
        }

        if (Time.time - _lastPendingZoneLogAt >= 1.5f)
        {
            _lastPendingZoneLogAt = Time.time;
            string hudText = hudZone >= 0 ? $"HUD={hudZone}" : "HUD=unknown";
            Plugin.Log.LogInfo($"[ZoneSwitcher] Waiting for Khu {_pendingZoneIdx} confirmation ({hudText}, age={age:F1}s)");
        }

        return true;
    }

    private static void StartPendingZone(int zoneIdx, string source)
    {
        _pendingZoneIdx = zoneIdx;
        _pendingZoneClickedAt = Time.time;
        _lastPendingZoneLogAt = -999f;
        _panelJustOpened = false;
        _panelSuppressUntil = -999f;
        LastActionClickedZone = false;
        LastClickedZoneIndex = zoneIdx;

        if (!string.IsNullOrEmpty(source))
            Plugin.Log.LogInfo($"[ZoneSwitcher] Clicked 'Khu {zoneIdx}' via {source} -> waiting HUD confirmation");
    }

    private static void CommitZoneConfirmed(int zoneIdx)
    {
        _pendingZoneIdx = -1;
        _pendingZoneClickedAt = -999f;
        _lastPendingZoneLogAt = -999f;
        _panelJustOpened = false;
        _panelOpenRetries = 0;
        _panelSuppressUntil = Time.time + ReopenPanelGuardSec;
        _targetZone = zoneIdx + 1;
        LastActionClickedZone = true;
        LastClickedZoneIndex = zoneIdx;
        Plugin.Log.LogInfo($"[ZoneSwitcher] ✓ Confirmed 'Khu {zoneIdx}' on HUD (next target Khu {_targetZone})");
    }

    // ===================================================================
    // Mở panel (click ZoneObject/Button trên HUD)
    // ===================================================================

    private static bool TryOpenZonePanel()
    {
        try
        {
            if (GameAPI.IsZonePanelVisible())
                return true;

            var allBtnObjects = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));
            if (allBtnObjects == null) return false;

            // Từ dump xác nhận: parent='ZoneObject', go='Button'
            string[] parentNames = { "ZoneObject", "ZoneBtn", "KhuObject", "ZoneBar", "ZoneHUD", "ZoneInfo", "MapZone" };

            foreach (var obj in allBtnObjects)
            {
                var btn = obj as Button;
                if (btn == null) continue;

                string parentName = btn.transform?.parent?.gameObject?.name ?? "";

                bool matched = false;
                foreach (var pn in parentNames)
                    if (parentName.Equals(pn, StringComparison.OrdinalIgnoreCase)) { matched = true; break; }

                if (!matched) continue;

                string txt = UIHelper.GetButtonText(btn);
                Plugin.Log.LogInfo($"[ZoneSwitcher] Opening panel via ZoneObject button (parent='{parentName}' text='{txt}')");
                btn.onClick.Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[ZoneSwitcher] TryOpenZonePanel fail: {ex.Message}");
        }

        // Fallback: nếu ZonePanel class tồn tại, gọi Show()
        try
        {
            var panel = GameAPI.FindZonePanel();
            if (panel != null)
            {
                var t = panel.GetType();
                foreach (var name in new[] { "Show", "show", "Open", "open", "showPanel" })
                {
                    var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m == null) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0) { m.Invoke(panel, null); return true; }
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(bool)) { m.Invoke(panel, new object[] { true }); return true; }
                }
            }
        }
        catch { }

        return false;
    }

    private static void MarkZoneClicked(int zoneIdx)
    {
        LastActionClickedZone = true;
        LastClickedZoneIndex = zoneIdx;
    }

    // ===================================================================
    // Click đúng button "Khu {zoneIdx}" trong panel
    // ===================================================================

    private static bool TryClickSpecificKhu(int zoneIdx, ZoneTab? expectedTab = null)
    {
        try
        {
            // Nút khu thường có text "Khu N"; nút tab loạn chiến có text "Lc N".
            // CẢNH BÁO - bug đã từng xảy ra: regex cũ chỉ có "khu" nên khi restore về
            // tab loạn chiến, KHÔNG nút nào match -> restore luôn fail -> nhân vật bị
            // bỏ lại ở khu thường dù trước đó đang up ở khu loạn chiến.
            Regex rx = new Regex(@"(?:khu|lc)[:\s]*(\d+)", RegexOptions.IgnoreCase);
            var khuButtons = CollectZoneCandidates(rx, expectedTab);

            if (khuButtons.Count == 0)
            {
                Plugin.Log.LogWarning($"[ZoneSwitcher] TryClickSpecificKhu({zoneIdx}): no '{expectedTab?.ToString() ?? ""} Khu N' buttons found in scan");
                return false;
            }

            // === Cập nhật tổng số khu ===
            int maxIdx = -1;
            var uniqueIdx = new List<int>();
            var seenIdx = new HashSet<int>();
            foreach (var item in khuButtons)
            {
                if (item.Idx > maxIdx) maxIdx = item.Idx;
                if (seenIdx.Add(item.Idx)) uniqueIdx.Add(item.Idx);
            }
            uniqueIdx.Sort();

            // Ưu tiên đọc "Tổng Khu N" trực tiếp từ nhãn panel — đây là số khu THẬT do game
            // hiển thị, chính xác hơn suy ra từ maxIdx (dễ bị nút rác/ẩn làm sai lệch).
            int panelTotal = TryReadTotalZonesFromPanel();
            if (panelTotal > 0)
            {
                if (_totalZones != panelTotal)
                {
                    _totalZones = panelTotal; // tin tuyệt đối nhãn tổng, kể cả khi phải GIẢM xuống
                    Plugin.Log.LogInfo($"[ZoneSwitcher] Total zones from panel label = {_totalZones} (Khu 0 → Khu {_totalZones - 1})");
                }
            }
            else
            {
                int detectedTotal = maxIdx + 1;
                if (detectedTotal > _totalZones)
                {
                    _totalZones = detectedTotal;
                    Plugin.Log.LogInfo($"[ZoneSwitcher] Detected {_totalZones} zones from buttons (Khu 0 → Khu {maxIdx})");
                }
            }

            // === Kiểm tra đã vượt quá số khu không ===
            if (zoneIdx >= _totalZones)
            {
                Plugin.Log.LogWarning($"[ZoneSwitcher] targetZone={zoneIdx} >= totalZones={_totalZones} → all zones done");
                return false;
            }

            // Log tất cả khu tìm được
            var khuList = string.Join(", ", uniqueIdx.ConvertAll(k => $"Khu {k}"));
            Plugin.Log.LogInfo($"[ZoneSwitcher] Zones available: [{khuList}] ({khuButtons.Count} buttons) → clicking Khu {zoneIdx}");

            // Tìm đúng button Khu zoneIdx
            ZoneCandidate? best = null;
            int bestScore = int.MinValue;
            foreach (var item in khuButtons)
            {
                if (item.Idx != zoneIdx) continue;
                int score = ScoreZoneCandidate(item);
                if (best == null || score > bestScore)
                {
                    best = item;
                    bestScore = score;
                }
            }

            if (best.HasValue)
            {
                var picked = best.Value;
                picked.Btn.onClick.Invoke();
                Plugin.Log.LogInfo($"[ZoneSwitcher] ✓ Clicked 'Khu {zoneIdx}' (text='{picked.Text}', source={(picked.FromPanel ? "panel" : "scene")})");
                return true;
            }

            // Khu zoneIdx không có trong list → log warning và return false
            Plugin.Log.LogWarning($"[ZoneSwitcher] Khu {zoneIdx} not found in panel/scene (max={maxIdx})");
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[ZoneSwitcher] TryClickSpecificKhu({zoneIdx}) fail: {ex.Message}");
        }
        return false;
    }


    // ===================================================================
    // Reflection fallback: gọi nextZone/ChangeZone trên ZonePanel class
    // ===================================================================

    private static bool TryCallReflectionNextZone()
    {
        try
        {
            var panel = GameAPI.FindZonePanel();
            if (panel == null) return false;

            var t = panel.GetType();
            foreach (var name in new[] { "nextZone", "NextZone", "changeZone", "ChangeZone", "next", "Next" })
            {
                var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null && m.GetParameters().Length == 0)
                {
                    m.Invoke(panel, null);
                    Plugin.Log.LogInfo($"[ZoneSwitcher] Reflection: called {t.Name}.{m.Name}()");
                    return true;
                }
            }

            // Gọi method nhận int (zone index cụ thể)
            foreach (var name in new[] { "selectZone", "SelectZone", "setZone", "SetZone", "goToZone", "changeZone", "ChangeZone" })
            {
                var mi = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) continue;
                var ps = mi.GetParameters();
                if (ps.Length == 1 && (ps[0].ParameterType == typeof(int) || ps[0].ParameterType == typeof(long)))
                {
                    mi.Invoke(panel, new object[] { _targetZone });
                    Plugin.Log.LogInfo($"[ZoneSwitcher] Reflection: called {t.Name}.{mi.Name}({_targetZone})");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[ZoneSwitcher] TryCallReflectionNextZone fail: {ex.Message}");
        }
        return false;
    }

    // ===================================================================
    // Text fallback: tìm button "khu tiếp" / "đổi khu"...
    // ===================================================================

    private static bool ClickZoneTextButton()
    {
        string[] names = { "khu tiếp", "khu tiep", "next zone", "next khu", "đổi khu", "doi khu", "chuyển khu", "chuyen khu" };
        try
        {
            var buttons = GetZonePanelButtons();
            if (buttons.Length == 0)
            {
                var allBtnObjects = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));
                if (allBtnObjects == null || allBtnObjects.Length == 0) return false;
                buttons = Array.ConvertAll(allBtnObjects, obj => obj as Button);
            }

            foreach (var btn in buttons)
            {
                if (btn == null) continue;
                string text = UIHelper.GetButtonText(btn);
                if (string.IsNullOrEmpty(text)) continue;
                foreach (var n in names)
                    if (text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Plugin.Log.LogInfo($"[ZoneSwitcher] Text-match fallback: clicking '{text}'");
                        btn.onClick.Invoke();
                        return true;
                    }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[ZoneSwitcher] ClickZoneTextButton fail: {ex.Message}");
        }
        return false;
    }

    // ===================================================================
    // Debug: Dump ALL button khi thất bại hoàn toàn
    // ===================================================================

    private static void DumpAllButtonsToLog()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("[ZoneSwitcher] *** BUTTON DUMP (NextZone failed) ***");
            var allBtnObjects = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));
            int count = 0;
            foreach (var obj in allBtnObjects)
            {
                var btn = obj as Button;
                if (btn == null) continue;
                string goName = btn.gameObject?.name ?? "?";
                string txt = UIHelper.GetButtonText(btn);
                bool active = btn.gameObject?.activeInHierarchy ?? false;
                string parent = btn.transform?.parent?.gameObject?.name ?? "ROOT";
                string grandp = btn.transform?.parent?.parent?.gameObject?.name ?? "";
                sb.AppendLine($"  [{(active ? "ACT" : "HID")}] {grandp}/{parent}/{goName} | '{txt}'");
                if (++count >= 120) { sb.AppendLine("  ...(capped at 120)"); break; }
            }
            sb.AppendLine($"  Total: {count} buttons | targetZone={_targetZone}");
            Plugin.Log.LogWarning(sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[ZoneSwitcher] DumpAllButtonsToLog fail: {ex.Message}");
        }
    }

    // ===================================================================
    // Helper
    // ===================================================================

    private static List<ZoneCandidate> CollectZoneCandidates(Regex rx, ZoneTab? expectedTab = null)
    {
        var results = new List<ZoneCandidate>();
        var seenIds = new HashSet<int>();

        // CHỈ đếm/click button đang HIỂN THỊ thật (activeInHierarchy).
        // FindObjectsOfTypeAll trả về cả prefab cache + button ẩn của tab khác,
        // nếu tính luôn sẽ khiến maxIdx (số khu) bị đội lên -> quét lặp vô tận.
        void ScanButtons(Button[] buttons, bool fromPanel)
        {
            if (buttons == null) return;
            foreach (var btn in buttons)
            {
                if (btn == null) continue;

                bool active = false;
                try { active = btn.gameObject != null && btn.gameObject.activeInHierarchy; } catch { }
                if (!active) continue;

                int id = 0;
                try { id = btn.GetInstanceID(); } catch { }
                if (id != 0 && !seenIds.Add(id))
                    continue;

                string text = UIHelper.GetButtonText(btn);
                if (string.IsNullOrEmpty(text)) continue;

                Match m = rx.Match(text);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int idx))
                {
                    if (expectedTab.HasValue)
                    {
                        bool isChaoticBtn = text.IndexOf("lc", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool wantChaotic = expectedTab.Value == ZoneTab.Chaotic;
                        if (isChaoticBtn != wantChaotic)
                            continue;
                    }
                    results.Add(new ZoneCandidate(btn, idx, text, fromPanel));
                }
            }
        }

        // Ưu tiên tuyệt đối button trong ZonePanel: đây là danh sách khu THỰC của tab hiện tại.
        var panelButtons = GetZonePanelButtons();
        if (panelButtons.Length > 0)
            ScanButtons(panelButtons, true);

        // Chỉ fallback scan toàn scene khi panel KHÔNG cho ra khu nào,
        // để tránh gộp nhầm khu của tab khác / prefab -> sai tổng số khu.
        if (results.Count == 0)
        {
            var allBtnObjects = Il2CppAPI.FindObjectsOfTypeAll(typeof(Button));
            if (allBtnObjects != null && allBtnObjects.Length > 0)
            {
                var allButtons = new Button[allBtnObjects.Length];
                for (int i = 0; i < allBtnObjects.Length; i++)
                    allButtons[i] = allBtnObjects[i] as Button;
                ScanButtons(allButtons, false);
            }
        }

        return results;
    }

    private static int TryReadTotalZonesFromPanel()
    {
        try
        {
            var panelObj = GameAPI.FindZonePanel();
            if (panelObj == null) return -1;

            Text[] texts = Array.Empty<Text>();
            TMPro.TextMeshProUGUI[] tmpTexts = Array.Empty<TMPro.TextMeshProUGUI>();

            if (panelObj is Component panelComp)
            {
                texts = panelComp.GetComponentsInChildren<Text>(true);
                tmpTexts = panelComp.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            }
            else if (panelObj is GameObject panelGo)
            {
                texts = panelGo.GetComponentsInChildren<Text>(true);
                tmpTexts = panelGo.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            }
            else
            {
                return -1;
            }

            foreach (var txt in texts)
            {
                int total = TryParseTotalZonesLabel(txt?.text);
                if (total > 0) return total;
            }

            foreach (var txt in tmpTexts)
            {
                int total = TryParseTotalZonesLabel(txt?.text);
                if (total > 0) return total;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[ZoneSwitcher] TryReadTotalZonesFromPanel fail: {ex.Message}");
        }

        return -1;
    }

    private static int TryParseTotalZonesLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;

        foreach (var pattern in new[]
        {
            @"Tổng\s*Khu\s*(\d+)",
            @"Tong\s*Khu\s*(\d+)",
            @"Tổng\s*Zone\s*(\d+)",
            @"Tong\s*Zone\s*(\d+)"
        })
        {
            Match m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int total) && total > 0)
                return total;
        }

        return -1;
    }

    private static int ScoreZoneCandidate(ZoneCandidate candidate)
    {
        int score = 0;
        if (candidate.FromPanel) score += 1000;

        try
        {
            if (candidate.Btn != null)
            {
                if (candidate.Btn.gameObject != null && candidate.Btn.gameObject.activeInHierarchy)
                    score += 100;
                if (candidate.Btn.gameObject != null && candidate.Btn.gameObject.activeSelf)
                    score += 10;
                if (candidate.Btn.interactable)
                    score += 20;
            }
        }
        catch { }

        return score;
    }

    private static Button[] GetZonePanelButtons()
    {
        try
        {
            var panelObj = GameAPI.FindZonePanel();
            if (panelObj is Component panelComp)
                return panelComp.GetComponentsInChildren<Button>(true);
            if (panelObj is GameObject panelGo)
                return panelGo.GetComponentsInChildren<Button>(true);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[ZoneSwitcher] GetZonePanelButtons fail: {ex.Message}");
        }
        return Array.Empty<Button>();
    }


}
