# CHANGELOG.md — Lịch sử thay đổi

Tất cả các thay đổi đáng chú ý của dự án được ghi nhận theo thứ tự mới nhất trước.

## [Unreleased] — Branch `feature/true-background-mode` (2026-11-26)

### Added
- Thêm [Reliability.cs](GrowCastleAutobot/gca/Classes/Automation/Reliability.cs) gồm `AutomationErrorKind`, `RetryPolicy`, `RetryTracker`, `RestartGuard`, và `RuntimeStatusSnapshot`.
- Thêm API điều khiển vòng đời an toàn cho `Autobot`: `BotState`, `Pause()`, `Resume()`, `StopAndWait(TimeSpan)`, cùng `OnRuntimeStatusUpdate` và `CompletedBattles`.
- Thêm hiển thị trạng thái thời gian thực `BackgroundModeStatusText` trên `MainWindow.xaml` (chế độ ADB/Win32/Foreground, thiết bị/HWND đích, số trận hoàn thành, độ trễ capture, số retry) và cập nhật động tiêu đề cửa sổ + system tray (`TASK-15`).
- Thêm hướng dẫn sử dụng tiếng Việt đầy đủ tại [docs/HUONG_DAN_SU_DUNG.md](docs/HUONG_DAN_SU_DUNG.md) và liên kết từ [README.md](README.md).
- Thêm chế độ `--benchmark` và mở rộng bộ kiểm thử tự động lên **102 assertions** trong [Program.cs](GrowCastleAutobot/gca.Tests/Program.cs) (bao gồm capture timeout, retry policy, chống vòng lặp restart, logging đa luồng + xoay file, Stop/Pause an toàn, kiểm chứng tương đương Phase 5 và UX status snapshot Phase 6).
- Khởi tạo bộ tài liệu quản lý dự án tiếng Việt: `AGENTS.md`, `ROADMAP.md`, `TASKS.md`, `PROGRESS.md`, `CHANGELOG.md`, `TEST_REPORT.md`, `HANDOFF.md`.
- (Từ thay đổi chưa commit trước phiên này): `AdbTransport.cs`, `LongReplay.cs`, tùy chọn `UseAdb` trên UI & settings.

### Changed
- **Hiệu năng xử lý ảnh & bộ nhớ (`TASK-14` — Phase 5)**:
  - Tối ưu `Utils.BmpHash` dùng `IncrementalHash` (MD5) + `ArrayPool<byte>.Shared` (giảm từ `4.004 ms` & `5.7 MB LOH/op` với 49 Gen2 GC/100 frame xuống `1.941 ms` & `0.2 KB/op`, 0 GC).
  - Tối ưu `GameCoordinates.Normalize` dùng `LockBits` unsafe copy/nearest-neighbor khớp pixel-for-pixel với GDI+ `NearestNeighbor + PixelOffsetMode.Half` (`17.354 ms → 3.727 ms`).
  - Tối ưu `FrameHealth.IsBlank` dùng `LockBits` thay cho `GetPixel` (`0.907 ms → 0.037 ms` trên frame blank).
  - Tối ưu `AdbCapture` tái sử dụng `MemoryStream` raw buffer (bỏ 11.5 MB LOH/frame, chỉ tái sử dụng khi đọc hoàn tất an toàn), giải mã 32-bit `uint*`, triển khai `IDisposable`.
  - Tối ưu `ScreenshotCache.CompressToJpeg` cache `ImageCodecInfo` (`19.036 ms → 12.634 ms`).
- **Capture timeout**: Cập nhật `WindowCapture` (cấu hình timeout, log slow capture, log & dispose late bitmap) và `AdbCapture`/`AdbTarget` (drain stdout/stderr trước khi dispose `MemoryStream`, phân loại `CaptureTimeout`).
- **Retry control**: Cập nhật `CaptureBackground` và thêm `CaptureForegroundWithRetry` dùng `RetryPolicy` (backoff tăng dần, bỏ qua retry với lỗi `WindowInvalid`/`StaleFrame`, ngắt backoff ngay khi Pause/Stop).
- **Restart loop protection**: Tích hợp `RestartGuard` vào `Restart()`, `Reset()`, `EnterGC()`, `OnWindowFreezeDetected()`, `WorkerLoop()` và `InitParameters()`.
- **Logging**: Cập nhật `Log.cs` thành thread-safe, tự xoay file ở ngưỡng 5 MB (tối đa 3 bản sao lưu), thêm Thread ID và tên file gọi; sửa `GetLatestVersionAsync` dùng `CultureInfo.InvariantCulture` để tránh spam lỗi ngày tháng.
- **Safe Stop & Pause**: Đồng bộ trạng thái qua `stateLock` trong `ThreadControl.cs` & `BackgroundMode.cs`; làm `Wait(ms)` thức dậy ngay khi Pause/Stop; dọn tài nguyên trong `finally` của `WorkerLoop`; đổi `OnRunningTimeUIUpdate` sang `Dispatcher.BeginInvoke`; gọi `StopAndWait` khi đóng `MainWindow`; sửa `WaitBetweenBattlesRuntime` hỗ trợ hủy ngay khi đang tạm dừng.

### Fixed
- Sửa rò rỉ tài nguyên GDI+ `EncoderParameters` và `EncoderParameter` trong `ScreenshotCache.CompressToJpeg`.
- Sửa rò rỉ unmanaged `Bitmap` khi `Colormode(...)` ghi đè `currentScreen` trong `Lib.cs` (`CollectMimic`) và `CaptchaSolving.cs` thông qua `ReplaceCurrentScreen`.
- Sửa lỗi đệ quy vô hạn `Restart() → Reset() → EnterGC(true) → Restart()`.
- Sửa lỗi `Stop()` không xóa `restartRequested`, có thể gây tự động restart sau khi người dùng đã dừng.
- Sửa lỗi race khiến `PauseAutomation` có thể nuốt yêu cầu `Stop()` hoặc làm `C()` nhấp nháy UI `Running` khi Stop lúc đang Pause.
- Sửa lỗi `InitParameters` ném `AutomationException` thành `[F] Unhandled exception` khi LDPlayer đang minimize lúc bấm Start.
- Sửa lỗi rò rỉ bitmap khi `CaptureScreen()` tạo lại ảnh theo kích thước màn hình mới.
- Sửa nguy cơ deadlock khi đóng cửa sổ chính trong lúc `TimeRunningUpdater` gọi `Dispatcher.Invoke`.

## [08691da .. 8ab68e3] — 2026-11-22 đến 2026-11-23
- Triển khai Strict Background Mode (Win32 `WindowCapture`, `BackgroundInput`, `FrameHealth`, chuẩn hóa tọa độ 1520×865, tạm dừng an toàn).
- Hoàn thành TASK 01–07, ma trận QA (`docs/BACKGROUND_MODE_QA.md`), và cập nhật PR #1.
