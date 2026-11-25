# TASKS.md — Danh sách nhiệm vụ dự án

Cập nhật: 2026-11-26 (Asia/Bangkok)

> Lịch sử chi tiết các task nền tảng **TASK 01 – TASK 07** (Audit, Build, Win32 Capture/Input, QA ban đầu, PR #1) được lưu tại [docs/BACKGROUND_MODE_TASKS.md](docs/BACKGROUND_MODE_TASKS.md) để tránh trùng lặp.

## Bảng tổng hợp nhiệm vụ hiện tại

| Task ID | Phase | Mục tiêu | Trạng thái |
| --- | --- | --- | --- |
| TASK-00 | Phase 0 | Kiểm toán repository, xác minh cấu hình thực tế & thiết lập hệ thống quản lý tiến độ | **DONE** |
| TASK-08 | Phase 4 | Xử lý capture timeout có kiểm soát trên Win32, ADB và Foreground | **DONE** |
| TASK-09 | Phase 4 | Kiểm soát retry (backoff, phân loại lỗi retryable/non-retryable, tracker) | **DONE** |
| TASK-10 | Phase 4 | Ngăn vòng lặp restart vô hạn (`RestartGuard`: chặn đệ quy, cửa sổ trượt, giới hạn thread restart) | **DONE** |
| TASK-11 | Phase 4 | Cải thiện logging (thread-safe, xoay file theo dung lượng, ngữ cảnh thread/file, bỏ spam lỗi parse version) | **DONE** |
| TASK-12 | Phase 4 | Xử lý Stop và Pause an toàn (máy trạng thái đồng bộ, ngắt `Wait` tức thì, tránh deadlock đóng app & UI flash) | **DONE** |
| TASK-13 | Phase 1–2 | Kiểm thử thực tế chế độ ADB (minimize/hide LDPlayer) và các tính năng farm mở rộng | **TESTING** |
| TASK-14 | Phase 5 | Đo đạc baseline CPU/RAM/capture latency và tối ưu chạy dài hạn | **TESTING** |
| TASK-15 | Phase 6–8 | Hoàn thiện UX hiển thị trạng thái, hướng dẫn tiếng Việt và chuẩn bị release | **TESTING** |

---

## Chi tiết các task

### TASK-00 — Repository Audit & Hệ thống quản lý tiến độ
- **Mục tiêu**: Xác minh cấu hình máy thực tế, trạng thái git, kiến trúc hiện có và hoàn thiện bộ tài liệu quản lý dự án.
- **Phạm vi thay đổi**: Tạo `AGENTS.md`, `ROADMAP.md`, `TASKS.md`, `PROGRESS.md`, `CHANGELOG.md`, `TEST_REPORT.md`, `HANDOFF.md`.
- **File liên quan**: `AGENTS.md`, `ROADMAP.md`, `TASKS.md`, `PROGRESS.md`, `CHANGELOG.md`, `TEST_REPORT.md`, `HANDOFF.md`, `docs/BACKGROUND_MODE_*.md`.
- **Điều kiện nghiệm thu**: Xác nhận đúng phần cứng thực tế (không giả định theo thông tin tham khảo), xác nhận branch/uncommitted changes, không tạo trùng nội dung với `docs/BACKGROUND_MODE_*.md`.
- **Phương pháp kiểm thử**: Kiểm tra `git status`, `git branch -a`, `gc.log`, đọc mã nguồn cốt lõi.
- **Trạng thái**: `DONE`
- **Kết quả thực tế**: Xác minh Windows 10 Home 19045 (64-bit), CPU thực tế Intel i7-7700HQ (khác thông tin tham khảo i5 Gen 13), RAM 15.9 GB, GPU GTX 1050 + Intel HD 630, LDPlayer 9.5.45.0, `.dotnet` SDK 8.0.319, branch `feature/true-background-mode` có sẵn thay đổi ADB chưa commit của phiên trước.
- **Vấn đề còn tồn tại**: Tên instance trong log gần nhất là `LDPlayer` (index 1), trong docs là `LDPlayer-1` — cần người dùng chọn đúng tiêu đề cửa sổ trên giao diện.

### TASK-08 — Xử lý capture timeout
- **Mục tiêu**: Bảo đảm mọi đường capture (Win32 `PrintWindow`, ADB `screencap`, Foreground `CopyFromScreen`) đều có timeout rõ ràng, không tích tụ worker treo, không rò rỉ bitmap hoặc race khi đóng stream.
- **Phạm vi thay đổi**: Phân loại `AutomationErrorKind.CaptureTimeout`, bổ sung single-flight guard có cấu hình timeout & log chậm trong `WindowCapture`, sửa lỗi race `MemoryStream`/`copyTask` khi kill tiến trình ADB, sửa rò rỉ bitmap và bọc retry/error cho Foreground `CaptureScreen`.
- **File liên quan**: `GrowCastleAutobot/gca/Classes/Automation/Reliability.cs`, `GrowCastleAutobot/gca/Classes/Automation/BackgroundTransport.cs`, `GrowCastleAutobot/gca/Classes/Automation/AdbTransport.cs`, `GrowCastleAutobot/gca/Autobot/Functions.cs`.
- **Điều kiện nghiệm thu**: Timeout trả về `AutomationErrorKind.CaptureTimeout`, giữ single-flight guard cho đến khi tác vụ trễ xong, tự dispose bitmap trễ, build Release thành công và vượt qua test timeout.
- **Phương pháp kiểm thử**: Unit/Integration test tự động trong `gca.Tests/Program.cs` (mô phỏng tác vụ `PrintWindow` treo vượt timeout).
- **Trạng thái**: `DONE`
- **Kết quả thực tế**: Build Release 0 lỗi; 5 assertion riêng cho capture timeout & late bitmap disposal đều PASS.
- **Vấn đề còn tồn tại**: `PrintWindow` treo vĩnh viễn ở tầng driver Windows không thể force-kill từ user-mode; bot sẽ từ chối capture chồng lấn và pause an toàn để người dùng xử lý.

### TASK-09 — Kiểm soát retry
- **Mục tiêu**: Tách logic retry thành `RetryPolicy` + `RetryTracker` có exponential backoff có trần, phân biệt lỗi retryable (`CaptureFailed`, `CaptureTimeout`) và non-retryable (`WindowInvalid`, `StaleFrame`, `InputFailed`, `GameStateUnknown`).
- **Phạm vi thay đổi**: Thêm `RetryPolicy`, `RetryTracker`, tích hợp vào `CaptureBackground()` và `CaptureForegroundWithRetry()`, hỗ trợ hủy/pause tức thì trong lúc chờ backoff.
- **File liên quan**: `GrowCastleAutobot/gca/Classes/Automation/Reliability.cs`, `GrowCastleAutobot/gca/Autobot/BackgroundMode.cs`, `GrowCastleAutobot/gca/Autobot/Functions.cs`.
- **Điều kiện nghiệm thu**: Lỗi retryable thực hiện tối đa 2 lần retry (tổng 3 lần thử) với backoff tăng dần; lỗi `WindowInvalid`/`StaleFrame` không retry vô ích (1 lần thử rồi pause ngay); phục hồi thành công sẽ reset bộ đếm lỗi liên tiếp.
- **Phương pháp kiểm thử**: Unit/Integration test tự động trong `gca.Tests/Program.cs`.
- **Trạng thái**: `DONE`
- **Kết quả thực tế**: 9 assertion kiểm tra backoff, phân loại lỗi, bỏ retry cho `WindowInvalid`/`StaleFrame`, và reset bộ đếm khi khôi phục thành công đều PASS.
- **Vấn đề còn tồn tại**: Chưa kiểm thử lỗi renderer thực tế trên GPU khác.

### TASK-10 — Ngăn vòng lặp restart vô hạn
- **Mục tiêu**: Triệt tiêu hoàn toàn vòng lặp đệ quy vô hạn `Restart() → Reset() → EnterGC(true) → Restart()`, giới hạn số lần restart liên tiếp & trong cửa sổ trượt thời gian, và giới hạn tự động khởi động lại worker thread (`WorkerLoop → RestartThread`).
- **Phạm vi thay đổi**: Tạo `RestartGuard`, tích hợp vào `Restart()`, `Reset()`, `EnterGC()`, `OnWindowFreezeDetected()`, `WorkerLoop()`, `InitParameters()`. Xóa `restartRequested` ngay khi người dùng bấm Stop.
- **File liên quan**: `GrowCastleAutobot/gca/Classes/Automation/Reliability.cs`, `GrowCastleAutobot/gca/Autobot/Lib.cs`, `GrowCastleAutobot/gca/Autobot/Main.cs`, `GrowCastleAutobot/gca/Autobot/Functions.cs`, `GrowCastleAutobot/gca/Autobot/Init.cs`, `GrowCastleAutobot/gca/Autobot/ThreadControl.cs`.
- **Điều kiện nghiệm thu**: Chặn đệ quy `Restart → Reset → EnterGC → Restart`; chặn vượt ngưỡng restart liên tiếp & sliding window; chặn vượt ngưỡng thread auto-restart; không restart sau khi người dùng Stop.
- **Phương pháp kiểm thử**: Unit test deterministic với fake clock trong `gca.Tests/Program.cs` + kiểm tra luồng code.
- **Trạng thái**: `DONE`
- **Kết quả thực tế**: 10 assertion kiểm tra re-entrancy, consecutive limit, healthy progress reset, sliding window và thread restart budget đều PASS.
- **Vấn đề còn tồn tại**: Kịch bản reboot LDPlayer thực tế (Foreground mode) chưa được kiểm thử live trong phiên này.

### TASK-11 — Cải thiện logging
- **Mục tiêu**: Làm cho `Log` an toàn đa luồng (thread-safe), tự động xoay file theo dung lượng (mặc định 5 MB, tối đa 3 file backup), bổ sung ngữ cảnh Thread ID + tên file nguồn, và loại bỏ spam lỗi `[E] Failed to get latest version` do parse ngày theo culture.
- **Phạm vi thay đổi**: Viết lại `Log.cs` giữ nguyên 100% tương thích API; sửa `GetLatestVersionAsync` dùng `DateTime.TryParseExact(..., CultureInfo.InvariantCulture)`; bổ sung log ngữ cảnh cho timeout, retry, restart guard và trạng thái Pause/Stop.
- **File liên quan**: `GrowCastleAutobot/gca/Classes/Log.cs`, `GrowCastleAutobot/gca/Autobot/Lib.cs`, `GrowCastleAutobot/gca/Autobot/BackgroundMode.cs`, `GrowCastleAutobot/gca/Autobot/ThreadControl.cs`, `GrowCastleAutobot/gca/Autobot/Main.cs`.
- **Điều kiện nghiệm thu**: Ghi log đồng thời từ nhiều luồng không ném `IOException`; file tự xoay đúng ngưỡng và không vượt quá số backup tối đa; format có Thread ID + file nguồn + level.
- **Phương pháp kiểm thử**: Stress test 8 luồng × 40 dòng log đồng thời với ngưỡng xoay file nhỏ trong `gca.Tests/Program.cs`.
- **Trạng thái**: `DONE`
- **Kết quả thực tế**: 4 assertion kiểm tra concurrency, rotation, backup cap và log format đều PASS.
- **Vấn đề còn tồn tại**: Không có.

### TASK-12 — Xử lý Stop và Pause an toàn
- **Mục tiêu**: Đồng bộ hóa máy trạng thái `BotState` bằng `stateLock`, ngắt `Wait(ms)` tức thì khi Pause hoặc Stop, loại bỏ lỗi race giữa `PauseAutomation` và `Stop`, sửa lỗi nhấp nháy UI Running khi Stop lúc đang Pause, sửa lỗi deadlock đóng app ở `OnRunningTimeUIUpdate`, và dọn tài nguyên chắc chắn trong `finally`.
- **Phạm vi thay đổi**: Refactor `ThreadControl.cs`, `BackgroundMode.cs`, `Main.cs`, `Init.cs`, `MainWindow.xaml.cs`, `WaitBetweenBattlesRuntime.cs`; bổ sung API `Pause()`, `Resume()`, `Stop()`, `StopAndWait(TimeSpan)`.
- **File liên quan**: `GrowCastleAutobot/gca/Autobot/ThreadControl.cs`, `GrowCastleAutobot/gca/Autobot/BackgroundMode.cs`, `GrowCastleAutobot/gca/Autobot/Main.cs`, `GrowCastleAutobot/gca/Autobot/Init.cs`, `GrowCastleAutobot/gca/MainWindow.xaml.cs`, `GrowCastleAutobot/gca/Classes/WaitBetweenBattlesRuntime.cs`.
- **Điều kiện nghiệm thu**: Pause ngắt `Wait(10_000)` < 1s; Stop ngắt cả `Wait` lẫn `Paused` < 1.5s; Stop lúc đang Pause không kích hoạt `OnStarted`; `Stop()` gọi nhiều lần an toàn; `PauseAutomation` không nuốt lệnh `Stop()`.
- **Phương pháp kiểm thử**: Unit/Integration test đa luồng trong `gca.Tests/Program.cs`.
- **Trạng thái**: `DONE`
- **Kết quả thực tế**: 8 assertion kiểm tra Pause/Resume/Stop/StopAndWait/race condition đều PASS.
- **Vấn đề còn tồn tại**: Cần kiểm thử thủ công nút bấm & phím tắt trên giao diện WPF khi đang chạy farm thực tế.

### TASK-13 — Kiểm thử thực tế chế độ ADB & tính năng farm mở rộng
- **Mục tiêu**: Xác minh chế độ ADB khi ẩn/minimize LDPlayer (`hwnd=0`) và các tính năng farm mở rộng (Auto Battle, Dungeon/Dragon, Upgrades, Ads, CAPTCHA, WPF UI Pause/Resume/Stop).
- **Phạm vi thay đổi**: Bổ sung harness `--live-adb-cycles` có kiểm tra thời lượng trận (`battleFrames >= 3`, `battle >= 5s`) trong `GrowCastleAutobot/gca.Tests/Program.cs`.
- **File liên quan**: `GrowCastleAutobot/gca.Tests/Program.cs`, `GrowCastleAutobot/gca/Classes/Automation/AdbTransport.cs`.
- **Điều kiện nghiệm thu**: Vượt qua 2 chu kỳ Replay liên tiếp bằng ADB khi LDPlayer ẩn với 0 đổi foreground/cursor; hoàn thành manual QA các tính năng farm mở rộng trên WPF UI.
- **Phương pháp kiểm thử**: Live integration test `--live-adb-capture` & `--live-adb-cycles LDPlayer 2` trên LDPlayer thật (`emulator-5556`).
- **Trạng thái**: `TESTING`
- **Kết quả thực tế**: PASS 2 chu kỳ ADB Replay liên tiếp trên `emulator-5556` khi LDPlayer ẩn (`hwnd=0`) trong `83.3s` (cycle 1: `38.4s` / `50` battleFrames; cycle 2: `37.1s` / `53` battleFrames; `118` capture attempts, `0` retries, foreground & cursor không đổi).
- **Vấn đề còn tồn tại**: Chưa kiểm thử thủ công Auto Battle, Dungeon/Dragon, Upgrades, Ads, CAPTCHA và nút/phím tắt Pause/Resume/Stop trên WPF UI thực tế.

### TASK-14 — Đo baseline & tối ưu hiệu năng chạy dài hạn (Phase 5)
- **Mục tiêu**: Đo baseline tốc độ/cấp phát bộ nhớ trên hot-path xử lý ảnh & capture, loại bỏ LOH churn và rò rỉ tài nguyên GDI+ để chạy ổn định nhiều giờ.
- **Phạm vi thay đổi**:
  - Thêm chế độ `--benchmark` tái lập được trong `GrowCastleAutobot/gca.Tests/Program.cs`.
  - Tối ưu `Utils.BmpHash` sang `IncrementalHash` (MD5) + `ArrayPool<byte>.Shared` (loại bỏ cấp phát 5.7 MB LOH & 49 lần Gen2 GC / 100 frame).
  - Tối ưu `GameCoordinates.Normalize` sang `LockBits` unsafe copy/nearest-neighbor khớp từng pixel với GDI+ `NearestNeighbor + PixelOffsetMode.Half` (nhanh hơn 4.66×).
  - Tối ưu `FrameHealth.IsBlank` sang `LockBits` unsafe (nhanh hơn 24.5× trên frame blank).
  - Tối ưu `AdbCapture` tái sử dụng `MemoryStream` raw buffer (an toàn khi timeout/hủy, bỏ 11.5 MB LOH/frame) + giải mã 32-bit `uint*` + `IDisposable`.
  - Tối ưu `ScreenshotCache.CompressToJpeg` cache `ImageCodecInfo` và `using` dispose `EncoderParameters`/`EncoderParameter` (hết rò rỉ GDI+).
  - Sửa rò rỉ `Bitmap` khi `Colormode(...)` ghi đè `currentScreen` trong `Lib.cs` (`CollectMimic`) và `CaptchaSolving.cs` (`ReplaceCurrentScreen`).
- **File liên quan**: `GrowCastleAutobot/gca/Classes/Utils.cs`, `GrowCastleAutobot/gca/Classes/Automation/BackgroundTransport.cs`, `GrowCastleAutobot/gca/Classes/Automation/AdbTransport.cs`, `GrowCastleAutobot/gca/Classes/ScreenshotCache.cs`, `GrowCastleAutobot/gca/Autobot/Functions.cs`, `GrowCastleAutobot/gca/Autobot/Lib.cs`, `GrowCastleAutobot/gca/Autobot/CaptchaSolving.cs`, `GrowCastleAutobot/gca.Tests/Program.cs`.
- **Điều kiện nghiệm thu**: Kết quả đầu ra khớp 100% bit-for-bit (`BmpHash`) và pixel-for-pixel (`Normalize`), giảm đáng kể thời gian & LOH allocation theo benchmark BEFORE/AFTER, PASS toàn bộ automated tests & live ADB cycles; sau đó xác minh soak test dài hạn.
- **Phương pháp kiểm thử**: Automated unit/equivalence tests (`98/98`), `--benchmark 100` Release BEFORE vs AFTER, và live test `--live-adb-capture` / `--live-adb-cycles LDPlayer 2`.
- **Trạng thái**: `TESTING`
- **Kết quả thực tế**: Build Release (`artifacts\phase5-build\gca.exe`) 0 lỗi; PASS **98/98 assertions**; `BmpHash` `4.004 ms → 1.941 ms` (`5706.4 KB/op, 49 Gen2 GC → 0.2 KB/op, 0 GC`), `Normalize` `17.354 ms → 3.727 ms`, `IsBlank(blank)` `0.907 ms → 0.037 ms`, `CompressJpeg` `19.036 ms → 12.634 ms`; PASS live ADB 2 chu kỳ liên tiếp.
- **Vấn đề còn tồn tại**: Chưa thực hiện soak test chạy liên tục nhiều giờ trên WPF UI thực tế để xác nhận ổn định RAM/CPU dài hạn.

### TASK-15 — Cải thiện UX hiển thị trạng thái, hướng dẫn tiếng Việt & chuẩn bị Release (Phase 6–8)
- **Mục tiêu**: Cải thiện khả năng quan sát trạng thái bot ngay trên cửa sổ chính/Taskbar/System Tray ở chiều rộng tối thiểu `390px`, hoàn thiện hướng dẫn sử dụng tiếng Việt đầy đủ và liên kết tài liệu chuẩn bị release.
- **Phạm vi thay đổi**:
  - Thêm `RuntimeStatusSnapshot` (`FormatSummary`, `FormatShortBadge`) trong `Reliability.cs` và event `OnRuntimeStatusUpdate` + bộ đếm `CompletedBattles` / `lastCaptureElapsedMs` (throttled ≥ 500ms) trong `Autobot.cs`, `BackgroundMode.cs`, `Main.cs`, `Functions.cs`.
  - Thêm dòng trạng thái `BackgroundModeStatusText` trên `MainWindow.xaml` (tự cập nhật khi đổi checkbox `Background mode`/`Capture inner window`/`Use ADB` và khi bot đang chạy/tạm dừng).
  - Cập nhật tiêu đề cửa sổ `Title` và `trayIcon.Text` (an toàn ≤ 63 ký tự) theo trạng thái (`Running [ADB]`, `Paused [Win32]`, ...), bổ sung gợi ý `Resume or Stop (phím tắt)` khi `Paused`.
  - Tạo hướng dẫn tiếng Việt chi tiết tại `docs/HUONG_DAN_SU_DUNG.md` và cập nhật liên kết trong `README.md`.
- **File liên quan**: `GrowCastleAutobot/gca/Classes/Automation/Reliability.cs`, `GrowCastleAutobot/gca/Autobot/Autobot.cs`, `GrowCastleAutobot/gca/Autobot/BackgroundMode.cs`, `GrowCastleAutobot/gca/Autobot/Main.cs`, `GrowCastleAutobot/gca/Autobot/Functions.cs`, `GrowCastleAutobot/gca/MainWindow.xaml`, `GrowCastleAutobot/gca/MainWindow.xaml.cs`, `GrowCastleAutobot/gca/MainWindow/Buttons.cs`, `GrowCastleAutobot/gca.Tests/Program.cs`, `docs/HUONG_DAN_SU_DUNG.md`, `README.md`.
- **Điều kiện nghiệm thu**: Build Release 0 lỗi, PASS toàn bộ automated tests (`102/102`), 100% file UTF-8 hợp lệ & bảo toàn BOM gốc, giao diện WPF hiển thị rõ ở `MinWidth=390`.
- **Phương pháp kiểm thử**: Build Release tách biệt (`artifacts\phase6-build`), unit test tự động trong `gca.Tests/Program.cs`, kiểm tra encoding toàn repo.
- **Trạng thái**: `TESTING`
- **Kết quả thực tế**: Đã triển khai hoàn chỉnh UX trạng thái + hướng dẫn tiếng Việt (`docs/HUONG_DAN_SU_DUNG.md`) + cập nhật `README.md`, build `artifacts\phase6-build\gca.exe` & chạy kiểm thử hồi quy `102/102`.
- **Vấn đề còn tồn tại**: Chờ kiểm thử trực quan WPF UI `artifacts\phase6-build\gca.exe` và xác nhận commit/PR từ người dùng.
