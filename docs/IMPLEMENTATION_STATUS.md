# Implementation Status — Task Coverage vs Spec

Generated: 2026-05-11  AutoBossGrabber (tasks.md 168 items)

## Legend
- ✅ Done (code + tests, verified)
- 🟡 Partially done (core exists, UI/polish pending or manually tested only)
- ⏭️ Intentionally skipped per plan (theme/wizard, see Q&A)
- ⏳ Remaining for future (requires extended in-game session)

---

## Phase A — Plugin (in-game) — DONE

| Spec | Status | Commit | Notes |
|------|--------|--------|-------|
| 14 ItemFilter | ✅ | 5cf37ed | ItemFilterManager (Decide pure), GameAPI rarity/name heuristics, AutoPickupLite integration, 12 tests |
| 13 FarmLoop (reward/satellite) | ✅ | 74364c4 | FarmExtras, reward popup scoring, satellite reuse via config IDs, analytics LOG_EVENT every 60s |
| 20 BehaviorRandomizer | ✅ | 55972af | ±10% dwell / ±20% delay / 50-200ms micro-pause / scan 50/50 / click offset, 12 tests |
| 21 Safety | ✅ | 94223c4 | Panic Ctrl+Alt+F12, fail-streak 5 auto-pause, SendBossKilled wiring previously missing |
| 22 State persistence | ✅ | 8623f4c | PersistedBotState, atomic save 30s, fresh<5min resume (opt-in EnableAutoRestartResume), 24h cleanup, 7 tests |
| 12 BFS seam | ✅ | 7681712 | SocketClient.RequestNavigationTo public API, IsNavigationInProgress, Runner opt-in EnableBfsNavigation fallback chain |

## Phase B — Manager

| Spec | Status | Commit | Notes |
|------|--------|--------|-------|
| 16 Analytics tab | ✅ | 8a5bbfa | LiveCharts2 12-bucket line + per-bot column, summary chips, export CSV |
| 17 LogAggregator | ✅ | 68d39dd | Daily JSONL, FileShare.ReadWrite live-read, 50MB rotation, 7-day retention, search/export, 4 tests |
| 18 Notifications | ✅ | cf07788 | Rate-limit 10/min/bot, toast 5s, tray balloon high-priority, history 50, App wiring |
| 19 Presets | ✅ | 2e07866 | Aggressive/Balanced/Safe templates, Apply, dropdown in both dialogs, randomization keys synced |
| 26.1 Shortcuts + 21.4 Global Pause | ✅ | 3b939ea | F5/Ctrl+N/Ctrl+P/Space, GlobalPause toggle blocking Start All |
| 24 Multi-launcher | ✅ | be25189 | ProcessLauncherService, per-account PID, duplicate block, crash auto-restart ≤MaxRestartAttempts |
| 23 Captcha queue | ✅ | b602c8e | SendCaptchaDetected on solver start (2 sites), OnCaptchaDetected → queue (max 50) + Retry→RESUME, tab with action |

## Phase C — Tests & Docs

| Spec | Status | Notes |
|------|--------|-------|
| 27.3 IPC reliability | ✅ | 30-round COMMAND→ACK latency <50ms test with heartbeat (IpcAndPresetTests) |
| 19.4 Preset tests | ✅ | 4 tests |
| 65+ unit tests | ✅ | dotnet test 70 passed (including previous suite) |
| Implementation status doc | ✅ | This file |
| In-game checklist | ✅ | docs/IN_GAME_TEST_CHECKLIST.md (Vietnamese) |

## Intentionally Skipped (per approved plan Q&A)

- 26.2 Light/Dark theme toggle — keep dark-only (personal app)
- 26.4 First-run wizard — unnecessary for existing profiles

## Remaining / Manual Verification Required

- 27.1, 27.2, 27.5, 27.6 End-to-end boss hunt, multi-instance stress, crash recovery — require actual game session with 1+ live accounts; see checklist file for step-by-step.
- LiveCharts rendering is smoke-tested (tab selects without crash) but visual tuning (colors, axis ticks) best verified with real kill data (generate via AnalyticsEngine in test).

## How to verify remaining manually

Run `docs/IN_GAME_TEST_CHECKLIST.md` top-to-bottom with game + 1 test account account and Manager Release build (`src/AutoBossManager/bin/Release/net6.0-windows/AutoBossManager.exe`).
