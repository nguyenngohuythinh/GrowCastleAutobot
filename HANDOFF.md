# HANDOFF.md — Thông tin bàn giao phiên làm việc

Cập nhật: 2026-11-26 03:20 (Asia/Bangkok)

## 1. Branch & trạng thái Git hiện tại

- **Branch hiện tại**: `feature/true-background-mode` (tracking `origin/feature/true-background-mode`).
- **Trạng thái commit**: Đã commit gọn trên branch `feature/true-background-mode` (chưa push / chưa merge vào `main`).
- **Lịch sử trước đó**: Xem `docs/BACKGROUND_MODE_HANDOFF.md` cho TASK 01–07.

## 2. Task đã hoàn thành & đang thực hiện

- **Đã hoàn thành (`DONE`)**: `TASK-00`, `TASK-08`, `TASK-09`, `TASK-10`, `TASK-11`, `TASK-12`.
- **Đang ở trạng thái `TESTING` (đã triển khai & vượt qua kiểm thử tự động + live ADB 2 chu kỳ + smoke-test khởi chạy WPF, còn manual QA/soak test dài hạn)**:
  - `TASK-13`: Đã PASS kiểm thử live **2 chu kỳ ADB Replay liên tiếp** khi LDPlayer ẩn hoàn toàn (`hwnd=0`, `emulator-5556`) trong `83.3s` (`118` capture attempts, `0` retries). Còn manual test Auto Battle, Dungeon, Upgrades, Ads, CAPTCHA, và WPF UI Pause/Resume/Stop.
  - `TASK-14`: Đã đo baseline BEFORE/AFTER và tối ưu hiệu năng hot-path (`BmpHash`, `Normalize`, `IsBlank`, `AdbCapture`, `ScreenshotCache`, sửa rò rỉ `Colormode`). Bản build `artifacts\phase5-build\gca.exe` khởi chạy ổn định (`Responding=True`, `142.6 MB` RAM). Còn soak test chạy liên tục nhiều giờ trên WPF UI thực tế.
  - `TASK-15`: Đã hoàn thiện UX hiển thị trạng thái thời gian thực (`BackgroundModeStatusText`, tiêu đề cửa sổ & system tray động, gợi ý Pause/Stop), tạo hướng dẫn tiếng Việt `docs/HUONG_DAN_SU_DUNG.md`, cập nhật `README.md`, build tách biệt `artifacts\phase6-build\gca.exe`, PASS **102 / 102 assertions**.
- **Kiểm tra Encoding**: Toàn bộ file thay đổi/mới tạo đều là UTF-8 hợp lệ (`TOTAL_BAD_FILES=0`), 100% file source code hiện có giữ nguyên BOM gốc so với `HEAD`. Lỗi hiển thị trên console Windows (nếu có) đến từ terminal đang ở `chcp 850`.
- **Task tiếp theo**: Người dùng chạy thử bản build mới nhất `artifacts\phase6-build\gca.exe` để kiểm thử trực quan UX & soak test dài hạn, sau đó xác nhận tách commit/PR trên `feature/true-background-mode`.

## 3. File đã thay đổi / thêm mới (chưa commit)

**Thay đổi Phase 4, Phase 5 & Quản lý tiến độ:**
- `GrowCastleAutobot/gca/Classes/Automation/Reliability.cs` (mới)
- `GrowCastleAutobot/gca/Classes/Automation/BackgroundTransport.cs`
- `GrowCastleAutobot/gca/Classes/Automation/AdbTransport.cs` (mở rộng trên file ADB sẵn có từ trước)
- `GrowCastleAutobot/gca/Autobot/BackgroundMode.cs`
- `GrowCastleAutobot/gca/Autobot/CaptchaSolving.cs`
- `GrowCastleAutobot/gca/Autobot/Functions.cs`
- `GrowCastleAutobot/gca/Autobot/Init.cs`
- `GrowCastleAutobot/gca/Autobot/Lib.cs`
- `GrowCastleAutobot/gca/Autobot/Main.cs`
- `GrowCastleAutobot/gca/Autobot/ThreadControl.cs`
- `GrowCastleAutobot/gca/Classes/Log.cs`
- `GrowCastleAutobot/gca/Classes/ScreenshotCache.cs`
- `GrowCastleAutobot/gca/Classes/Utils.cs`
- `GrowCastleAutobot/gca/Classes/WaitBetweenBattlesRuntime.cs`
- `GrowCastleAutobot/gca/MainWindow.xaml.cs`
- `GrowCastleAutobot/gca/gca.csproj` (`InternalsVisibleTo` cho `gca.Tests`)
- `GrowCastleAutobot/gca.Tests/Program.cs` (98 assertions + `--benchmark` + `--live-adb-cycles`)
- `AGENTS.md`, `ROADMAP.md`, `TASKS.md`, `PROGRESS.md`, `CHANGELOG.md`, `TEST_REPORT.md`, `HANDOFF.md` (mới)

**Thay đổi chưa commit có sẵn từ trước (được bảo toàn nguyên vẹn):**
- `GrowCastleAutobot/BACKGROUND_MODE.md`
- `GrowCastleAutobot/gca/Autobot/ActionsSimulation.cs`
- `GrowCastleAutobot/gca/Classes/SettingsScripts/ClickerSettings.cs`
- `GrowCastleAutobot/gca/MainWindow.xaml`
- `GrowCastleAutobot/gca/MainWindow/Buttons.cs`
- `GrowCastleAutobot/gca.Tests/LongReplay.cs`

## 4. Quyết định kỹ thuật quan trọng

- Giữ nguyên tiền tố thông báo lỗi (`Capture failed:`, `Stale frame:`, `Input failed:`, `Window invalid:`, `Game state unknown:`) để 100% tương thích ngược, đồng thời bổ sung `AutomationErrorKind` và `IsRetryable`.
- `Wait(int milliseconds)` và `WaitRetryDelay` chờ trên `[ioCancellation.Token.WaitHandle, pauseRequestedEvent]` — phản hồi < 1 ms cho cả Pause lẫn Stop.
- `RestartGuard` reset khi người dùng Start mới nhưng giữ nguyên cửa sổ trượt khi `RestartThread()` tự động chạy lại sau freeze.
- `Utils.BmpHash`, `GameCoordinates.Normalize`, và `FrameHealth.IsBlank` được tối ưu với yêu cầu bắt buộc **khớp 100% bit-for-bit / pixel-for-pixel** so với thuật toán gốc (đã khóa bằng unit test tự động).
- `AdbCapture` chỉ tái sử dụng `MemoryStream` sau khi `copyTask` & `stderrTask` đã kết thúc sạch; nếu timeout/hủy thì hủy bỏ buffer cũ để tránh tuyệt đối race condition.

## 5. Lệnh đã chạy & kết quả

```powershell
.\.dotnet\dotnet.exe build GrowCastleAutobot\gca\gca.csproj -c Release -o D:\GrowCastleAutobot\artifacts\phase5-build
.\.dotnet\dotnet.exe run -c Release --project GrowCastleAutobot\gca.Tests
.\.dotnet\dotnet.exe run -c Release --project GrowCastleAutobot\gca.Tests --no-build -- --benchmark 100
.\.dotnet\dotnet.exe run -c Release --project GrowCastleAutobot\gca.Tests --no-build -- --live-adb-capture LDPlayer
.\.dotnet\dotnet.exe run -c Release --project GrowCastleAutobot\gca.Tests --no-build -- --live-adb-cycles LDPlayer 2
```

- **Build Release (`artifacts\phase5-build\gca.exe`)**: PASS (0 errors, 7 warnings sẵn có ở `SystemMonitor.cs`).
- **Automated Tests**: PASS — **98 / 98 assertions**.
- **Phase 5 Benchmark (`--benchmark 100`)**:
  - `Utils.BmpHash`: `4.004 ms → 1.941 ms`, `5,706.4 KB/op (49 Gen2 GC) → 0.2 KB/op (0 GC)`.
  - `GameCoordinates.Normalize`: `17.354 ms → 3.727 ms` (4.66×).
  - `FrameHealth.IsBlank(blank)`: `0.907 ms → 0.037 ms` (24.5×).
  - `ScreenshotCache.CompressJpeg`: `19.036 ms → 12.634 ms` (0 GDI+ leak).
- **Live ADB Capture (`emulator-5556`, LDPlayer ẩn `hwnd=0`)**: PASS — 10/10 frames, 9 thay đổi, `sky=True`, `menu=True`.
- **Live ADB 2 chu kỳ Replay liên tiếp (`emulator-5556`, LDPlayer ẩn `hwnd=0`)**: **PASS** — cycle 1 `38.4s` (`50` battleFrames), cycle 2 `37.1s` (`53` battleFrames), tổng `83.3s`, `118` capture attempts, `0` retries, foreground & cursor không đổi.

## 6. Lỗi chưa xử lý / giới hạn còn tồn tại

- `PrintWindow` bị treo vĩnh viễn ở tầng driver Windows không thể force-kill; hiện đã được cô lập bằng single-flight guard + timeout + pause an toàn.
- Chưa thực hiện manual QA đầy đủ trên WPF UI (`artifacts\phase5-build\gca.exe`) và soak test nhiều giờ đo CPU/RAM thực tế.
- Còn 7 compiler warning sẵn có từ trước ở `SystemMonitor.cs` (ngoài phạm vi Phase 4–5).

## 7. Nội dung cần người dùng xác nhận

1. Chạy thử bản build mới `D:\GrowCastleAutobot\artifacts\phase5-build\gca.exe` trên WPF UI thực tế (kiểm thử Pause/Resume/Stop, các tính năng farm mở rộng & soak test dài hạn).
2. Xác nhận có muốn tiếp tục sang `TASK-15` (Phase 6 UX & Phase 8 chuẩn bị tài liệu/release) và/hoặc tách commit gọn gàng trên `feature/true-background-mode` hay không.
