# Checklist kiểm thử trong game — AutoBossGrabber (Vietnamese)

Thực hiện tuần tự với 1 account test + Manager bản Release. Mỗi mục đánh dấu ✅ khi đạt.

## Chuẩn bị

- [ ] Build Release: `dotnet build -c Release`
- [ ] Copy `src/AutoBossGrabber/bin/Release/net6.0/AutoBossGrabber.dll` + `AutoBossShared.dll` vào `runtime/BepInEx/plugins/` (hoặc `deploy/deploy.ps1 -Configuration Release`)
- [ ] Mở Manager: `src/AutoBossManager/bin/Release/net6.0-windows/AutoBossManager.exe` — kiểm tra IPC log tab có dòng "listening on 127.0.0.1:28081"
- [ ] Mở game `runtime/Vũ Trụ Đại Chiến.exe` — console BepInEx hiện `Auto Boss Grabber v2.x LOADED` và `SocketClient Connected to Manager`

## 1. Cơ bản — Dashboard & điều khiển

- [ ] Manager hiện 1 bot row với map/zone/HP
- [ ] Nhấn `▶ Start All` — StatusMessage đổi `Starting...`, tương tự Stop All / Refresh
- [ ] Nhấn `⏸ Global Pause (Ctrl+P)` — Start All bị chặn với thông báo `GLOBAL PAUSE đang bật`
- [ ] Tắt Global Pause — Start All lại hoạt động
- [ ] Phím tắt: F5 (refresh), Ctrl+N (mở dialog Add Bot), chọn 1 hàng rồi Space (toggle start/stop) — xác nhận StatusMessage đổi

## 2. Profile & Presets (task 19, 25)

- [ ] Nhấn `Add Bot (Ctrl+N)` — dialog mở, điền AccountName + chọn preset `Aggressive` — xác nhận các ô MaxZone/Attack/Combat/Retreat/Loot tự điền
- [ ] Lưu profile — kiểm tra `AppData/AutoBossManager/profiles/{Account}.json` tồn tại và password mã hoá `enc:v1:...` (không phải plaintext)
- [ ] Mở lại dialog với account đã lưu — password tự điền (decrypt) và preset giữ nguyên

## 3. Logs & Analytics (task 16, 17)

- [ ] Tab Logs: sau khi game kết nối xuất hiện dòng `Bot connected`; Level filter All/Info/Warning/Error lọc đúng
- [ ] Ô search gõ `boss` + Enter — hiện preview kết quả tìm kiếm file log đã lưu (MessageBox)
- [ ] Nhấn `Export` — chọn file `logs.txt` — mở file kiểm tra chứa các dòng log
- [ ] Tab Analytics: nhấn `Refresh` — Summary hiện `0 kills · 0.0/h` ban đầu; sau khi săn boss (mục 5) kiểm tra line chart tăng
- [ ] Trong Analytics nhấn `Export CSV` — file CSV có header `timestamp,instanceId,kind,detail`

## 4. Item Filter (task 14)

- [ ] Tab bot `⚙ Config` — chỉnh FilterMode Whitelist, list `vang` — lưu → Manager gửi CONFIG_UPDATE (kiểm tra log tab có `Config updated`)
- [ ] Trong game thả item `vang` và `rac cu` — chỉ `vang` được nhặt (quan sát pickup log hoặc inventory)
- [ ] Đổi sang Blacklist `rac cu` — `rac cu` bị bỏ qua, các item khác được nhặt

## 5. Săn boss thực tế (task 12, 13, 20, 22)

- [ ] Đứng ở map farm, nhấn F1 trong game (hoặc Start All từ Manager) — bot chuyển `FarmTown → TeleportToBossMap`
- [ ] Nếu `EnableBfsNavigation = false` (mặc định) — di chuyển qua portal chain cứng cũ
- [ ] Bật `EnableBfsNavigation = true` trong Config dialog và thử TELEPORT_TO_MAP tới map khác — kiểm tra path BFS và portal traversal
- [ ] Quan sát dwell time giữa các khu — không đều tăm tắp (±10% ngẫu nhiên)
- [ ] Sau khi clear 1 khu → ZonesCleared tăng; mỗi ~60s log `FARM_STATS zones=...`
- [ ] Nếu có popup thưởng — bot tự click claim (kiểm tra log `Claimed reward popup`)

## 6. Safety (task 21)

- [ ] Trong game nhấn `Ctrl+Alt+F12` — bot dừng ngay, StatusMessage Manager hiện `PANIC_STOP`
- [ ] Cho bot combat timeout 5 lần liên tiếp (đặt CombatTimeoutSec rất thấp để test) — Manager hiện `SAFETY_PAUSE: 5 consecutive ...` và bot Idle đòi F1 resume thủ công

## 7. State persistence (task 22)

- [ ] Bật `EnableAutoRestartResume = true` + đang farming — chờ 30s — kiểm tra `BepInEx/config/AutoBossGrabber/bot_state.json` tồn tại
- [ ] Kill game process — mở lại game — bot tự resume farming nếu file <5 phút (log `Fresh persisted state -> se auto-resume`)

## 8. Captcha (task 23)

- [ ] Trigger captcha (hoặc chờ tự nhiên) — Manager tab Captcha xuất hiện 1 entry `Auto-solving (CNN)` + toast + tray balloon nếu high-priority
- [ ] Nhấn `Retry` trong hàng captcha — Manager gửi RESUME, bot thử giải lại

## 9. Multi-launcher & Notifications (task 24, 18)

- [ ] Tạo 2 profile khác account — Manager nhấn `Launch All` — 2 game process mở (kiểm tra Task Manager)
- [ ] Cố nhấn `Launch All` lần nữa — thông báo `đang chạy` (chặn trùng account)
- [ ] Kill 1 game process có `AutoRestartOnCrash = true` — sau 5s tự mở lại, log `Crashed -> auto-restart`
- [ ] Trigger boss found (thông báo hệ thống trong game) — Manager hiện toast góc phải trên + tray balloon nếu high-priority
- [ ] Bottom bar nhấn `🔔` — MessageBox hiện history 50 thông báo gần nhất
- [ ] Gửi nhanh >10 notification/phút cho cùng account — xác nhận rate-limit chặn phần thừa (chỉ 10/phút được hiện toast)

## 10. Hiệu năng (task 27.6 — benchmark thô)

- [ ] Mở 3+ game instance cùng Manager — kiểm tra RAM/mỗi instance <800MB (Task Manager → chi tiết), CPU idle <3%, combat <8%
- [ ] Kết nối 5+ bot — Dashboard refresh mỗi 1s không giật (<100ms)

## Khi tất cả ✅

- Đã sẵn sàng bàn giao production. Cập nhật `docs/CHANGELOG.md` và tag release.
