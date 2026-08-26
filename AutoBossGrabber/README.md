# Auto Boss Grabber - Hướng dẫn sử dụng

Plugin BepInEx tự động **ôm boss** trong Võ Trụ Đại Chiến:
- Auto scan phát hiện boss (3s/lần)
- Mở menu Dịch chuyển nhanh → sang map boss
- Đổi zone cho tới khi tìm thấy boss
- Di chuyển + tấn công boss
- Nhặt đồ rơi
- "Go back" - mở menu → click "Thị Trấn Cổ" / "Quay lại"
- Quay về farm tại Ngoại Ô Thị Trấn

## Cài đặt

1. Plugin `AutoBossGrabber.dll` đã được copy vào:
   ```
   C:\Users\phuct\Downloads\VuTruDaiChien_123\BepInEx\plugins\AutoBossGrabber.dll
   ```

2. Chạy file `Vu Trụ Đại Chiến.exe`

## Hotkeys

| Phím | Chức năng |
|---|---|
| **F1** | Bật/tắt Auto Boss (mặc định TẮT) |
| **F2** | Dump toàn bộ UI panels ra file `BepInEx\ui_panel_dump.txt` |
| **F3** | Test mở menu Teleport |
| **F4** | Test click button "Đổi khu" |
| **F5** | Test "Go back" - về town |

## Bước 1: Dump UI panels

Trong game:
1. Đăng nhập nhân vật
2. Đứng ở map **Ngoại Ô Thị Trấn** (map farm bình thường)
3. Nhấn **F2** → kiểm tra file `BepInEx\ui_panel_dump.txt` xuất hiện chưa
4. Mở **menu Dịch chuyển nhanh** (phím `T` hoặc click icon)
5. Nhấn **F2** lần nữa → dump sẽ có thêm class teleport panel
6. Đóng menu, **mở menu Đổi khu vực** → nhấn **F2** lần 3

**Sau đó gửi file `BepInEx\ui_panel_dump.txt` cho tôi** để tôi viết code MapTransporter/ZoneSwitcher chính xác.

## Bước 2: Test từng chức năng

1. Nhấn **F3** - nếu menu teleport tự mở được → OK
2. Nhấn **F4** - nếu zone tự đổi → OK
3. Nhấn **F5** - nếu về town được → OK

## Bước 3: Bật Auto Boss

Nhấn **F1** → log hiện "ENABLED". Bot sẽ tự động:
- Mỗi 3s scan trong town
- Nếu thấy boss → mở menu teleport → sang map boss
- Đổi zone cho tới khi thấy boss
- Đánh + nhặt đồ + về farm

## Logs

Plugin log ra:
- `<game_folder>\BepInEx\Log\BepInEx.log` (BepInEx standard)
- `<game_folder>\BepInEx\ui_panel_dump.txt` (khi nhấn F2)
- `<game_folder>\BepInEx\runtime_type_dump.txt` (nếu có)

## Build lại

Nếu muốn build lại từ source:
```cmd
cd C:\Users\phuct\Downloads\tool\AutoBossGrabber
build.cmd
```

## Cấu trúc file

```
AutoBossGrabber/
├── BepInEx/                          ← copy từ game folder
├── source/
│   ├── AutoBossGrabber.csproj
│   ├── Plugin.cs                     ← entry point
│   ├── GameAPI.cs                    ← wrapper game API
│   ├── AutoBoss/
│   │   ├── AutoBossState.cs          ← enum trạng thái
│   │   ├── AutoBossConfig.cs         ← cấu hình
│   │   ├── AutoBoss.cs               ← state machine
│   │   ├── BossDetector.cs           ← scan mob
│   │   ├── MapTransporter.cs         ← "go back" tool
│   │   ├── ZoneSwitcher.cs           ← đổi khu
│   │   ├── UiPanelDumper.cs          ← dump UI
│   │   └── AutoPickupLite.cs         ← helper nhặt đồ
│   └── bin/Release/net6.0/AutoBossGrabber.dll
└── build.cmd
```

## Config

Sửa `AutoBossConfig.cs` để đổi:
- `BossNames` - danh sách tên boss (substring match)
- `BossMapName` - tên map boss
- `HomeMapName` - tên map town
- `AttackRange` - khoảng cách bắt đầu đánh
- `MaxZoneAttempts` - số lần đổi zone tối đa

## Lưu ý

- File `runtime_type_dump.txt` và `ui_panel_dump.txt` được ghi ở folder **CHA** của `plugins`, tức `<game_folder>\BepInEx\`
- Nếu plugin không hoạt động, mở `BepInEx\Log\BepInEx.log` xem lỗi
- Nếu vẫn lỗi, paste log vào đây để tôi fix