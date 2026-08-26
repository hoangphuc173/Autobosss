# AutoBossSystem

Hệ thống auto boss hunt cho game Unity IL2CPP **"Vũ Trụ Đại Chiến"** (BepInEx 6), gồm bot plugin chạy trong game + ứng dụng WPF quản lý nhiều bot qua TCP IPC.

## Kiến trúc

```
AutoBossManager (WPF, net6.0-windows)          AutoBossGrabber (BepInEx plugin, net6.0)
┌─────────────────────────────┐   TCP 28081   ┌──────────────────────────────────┐
│ SocketServer  ←→ Dashboard  │◄─────────────►│ SocketClient (heartbeat, command)│
│ ProfileManager (DPAPI pass) │  line-JSON    │ AutoBossRunner (state machine)   │
│ AnalyticsEngine (24h window)│               │ MessageHook (Harmony packet)     │
│ MainViewModel + BotInstance │               │ BFS Pathfinder + ZoneSwitcher    │
└─────────────────────────────┘               └──────────────────────────────────┘
                    ▲                    ▲
                    └── AutoBossShared ──┘  (IpcMessage, BotProfile, enums - single source of truth)
AutoBossLauncher (WinForms): chọn account → launch game → nhận log qua named pipe.
```

**Luồng chính:** Manager mở TCP server port `28081` → mỗi game instance chạy plugin tự kết nối → heartbeat 3s mang stats, STATUS_UPDATE 5s cập nhật dashboard → lệnh điều khiển (`START_FARMING`, `TELEPORT_TO_MAP`, `SWITCH_ZONE`...) đi xuống và được thực thi trên main thread của Unity.

## Cấu trúc thư mục

| Thư mục | Nội dung |
|---|---|
| `src/AutoBossGrabber/` | BepInEx plugin: Core (Plugin, SocketClient, GameAPI), Features (Runner, Navigation...), Hooks, UI, Utils |
| `src/AutoBossManager/` | WPF app: Services, ViewModels, Helpers |
| `src/AutoBossShared/` | Model IPC chung cho cả 2 phía |
| `src/AutoBossLauncher/` | WinForms launcher đa account |
| `tests/AutoBoss.Tests/` | xUnit tests — chạy được bằng `dotnet test` (không cần game) |
| `libs/` | DLL tham chiếu biên dịch (BepInEx core + interop) |
| `runtime/` | Bản cài game đầy đủ (gitignored — không track) |
| `deploy/` | `deploy.ps1` build + copy plugin vào BepInEx/plugins |
| `docs/` | Tài liệu kỹ thuật + archive lịch sử |
| `tools/` | Codemod/legacy scripts (tham khảo) |

## Build & Test

```powershell
dotnet build AutoBossSystem.sln -c Release     # build tất cả
dotnet test  tests\AutoBoss.Tests\AutoBoss.Tests.csproj   # chạy unit test
```

Yêu cầu: .NET 6 SDK, Windows.

## Deploy

```powershell
.\deploy\deploy.ps1                              # tự tìm game trong Steam library
.\deploy\deploy.ps1 -GamePath "D:\Games\VuTu"    # chỉ định game
.\deploy\deploy.ps1 -Configuration Debug         # deploy bản Debug
```

Script tự build nếu thiếu DLL, backup bản cũ trước khi ghi đè, dọn cache BFS.

## Chạy

1. `runtime\Vũ Trụ Đại Chiến.exe` (hoặc game đã cài BepInEx) — plugin tự nạp, nhấn **F1** bật/tắt bot
2. Mở `src\AutoBossManager\bin\Release\net6.0-windows\AutoBossManager.exe` — server khởi động cùng app
3. Hotkeys trong game: `F2` dump UI · `F3` test teleport · `F4` test chuyển khu · `F5` về nhà · `F6` dump network class

## Tài liệu chi tiết

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — thiết kế hệ thống
- [docs/API_REFERENCE.md](docs/API_REFERENCE.md) — giao thức IPC & lệnh điều khiển
- [docs/TUNING_GUIDE.md](docs/TUNING_GUIDE.md) — tinh chỉnh thông số bot
- [docs/OPTIMIZATION_SUMMARY.md](docs/OPTIMIZATION_SUMMARY.md), [docs/PERFORMANCE_COMPARISON.md](docs/PERFORMANCE_COMPARISON.md) — tối ưu hiệu năng
