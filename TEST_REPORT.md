# TEST_REPORT.md — Báo cáo kiểm thử

Cập nhật: 2026-11-26 03:20 (Asia/Bangkok)

> Ma trận kiểm thử thực tế trên LDPlayer của các phiên TASK 03–06 được lưu đầy đủ tại [docs/BACKGROUND_MODE_QA.md](docs/BACKGROUND_MODE_QA.md).

## 1. Môi trường kiểm thử thực tế đã xác minh

- **OS**: Windows 10 Home, Version 10.0.19045, Build 19045 (64-bit)
- **CPU**: Intel(R) Core(TM) i7-7700HQ @ 2.80GHz (4 cores / 8 logical)
- **RAM**: 15.9 GB
- **GPU**: NVIDIA GeForce GTX 1050 + Intel(R) HD Graphics 630
- **LDPlayer**: 9.5.45.0 (cấu hình đã quan sát trong `ldconsole list2`: `LDPlayer`, index 1, `1600×900`, DPI 240, ADB `emulator-5556`)
- **SDK dùng build/test**: `.dotnet/dotnet.exe` — .NET SDK `8.0.319` (`global.json`)

## 2. Kết quả Build & Kiểm toán Encoding

| Lệnh / Kiểm tra | Kết quả | Chi tiết |
| --- | --- | --- |
| `.\.dotnet\dotnet.exe build GrowCastleAutobot\gca\gca.csproj -c Release -o D:\GrowCastleAutobot\artifacts\phase6-build` | **PASS** | 0 errors, 7 warnings sẵn có (`SystemMonitor.cs`), 0 warning mới. |
| Khởi chạy smoke-test WPF `artifacts\phase5-build\gca.exe` | **PASS** | Tiến trình khởi chạy ổn định (`Responding=True`, `RAM=142.6 MB`), nạp đúng `clickerSettings.json`, đăng ký phím tắt `Alt+F1`/`Alt+F2`, 0 lỗi trong `gc.log`. |
| Kiểm tra encoding toàn bộ file đã tạo/chỉnh sửa | **PASS** | 100% file UTF-8 hợp lệ (`TOTAL_BAD_FILES=0`, `U+FFFD=0`, `SuspQ=0`, `Mojibake=0`); 100% file source code hiện có giữ nguyên BOM gốc so với `HEAD`. |

## 3. Phân loại kết quả kiểm thử

### A. Unit Test & Deterministic Integration Test (`gca.Tests`)

- **Lệnh**: `.\.dotnet\dotnet.exe run -c Release --project GrowCastleAutobot\gca.Tests`
- **Kết quả**: **PASS — 102 / 102 assertions**

| Nhóm kiểm thử | Số lượng | Kết quả | Nội dung xác minh |
| --- | --- | --- | --- |
| Tọa độ, DPI, chuẩn hóa ảnh & `FrameHealth` | 17 | PASS | Mapping viewport 1520×865, scaling, tọa độ âm đa màn hình, từ chối frame đen/nhỏ, stale frame 15s sau input. |
| Tương thích JSON & ADB decoding/key mapping | 14 | PASS | Round-trip `BackgroundMode`/`CaptureInnerWindow`/`UseAdb`, giải mã raw screencap 12/16-byte RGBA/BGRA, ánh xạ phím Android. |
| Worker fault pause & Win32 input receiver ẩn | 22 | PASS | 2 retry rồi pause, Stop đánh thức worker, chặn global input/ESC/Restart/Reset trong Background Mode, gửi đúng message tới HWND đích không đổi focus/chuột. |
| Capture timeout & single-flight (`TASK-08`) | 5 | PASS | Phân loại `CaptureTimeout`, chặn chồng lấn worker khi treo, tự nhả khóa & dispose late bitmap. |
| Kiểm soát retry (`TASK-09`) | 9 | PASS | Exponential backoff có trần, bỏ retry với `WindowInvalid`/`StaleFrame` (1 attempt), khôi phục ở lần retry 2 reset bộ đếm lỗi. |
| Chống vòng lặp restart vô hạn (`TASK-10`) | 10 | PASS | Chặn đệ quy `Restart → Reset → EnterGC → Restart`, giới hạn liên tiếp, reset sau healthy progress, cửa sổ trượt, giới hạn thread restart. |
| Logging đa luồng & xoay file (`TASK-11`) | 4 | PASS | 8 luồng × 40 dòng không `IOException`, xoay file tự động, giữ tối đa số backup, có Thread ID & tên file. |
| Stop & Pause an toàn (`TASK-12`) | 8 | PASS | Pause ngắt `Wait(10_000)` tức thì, `StopAndWait` ngắt worker đang Pause, không nhấp nháy UI `Running`, `Stop()` idempotent & xóa `restartRequested`, `PauseAutomation` không nuốt Stop. |
| Tương đương tối ưu hiệu năng (`TASK-14`) | 9 | PASS | `Utils.BmpHash` khớp MD5 tham chiếu bit-for-bit (bỏ qua vùng 75×75), `GameCoordinates.Normalize` khớp GDI+ `NearestNeighbor` pixel-for-pixel (1:1 & 1600×900), `LockBits IsBlank`, `DecodeRawScreencap(buffer, length)`, `CompressToJpeg`. |
| UX Runtime Status Snapshot (`TASK-15`) | 4 | PASS | Badge `ADB`/`Win32`, định dạng `FormatSummary` (mode, target, battles, latency, retries), `RecordCompletedBattle` tăng bộ đếm & phát event `OnRuntimeStatusUpdate`. |

### B. Performance Benchmark (Release, `--benchmark 100`, Intel i7-7700HQ)

| Hạng mục | BEFORE (avg) | AFTER (avg) | Tốc độ | BEFORE alloc / GC | AFTER alloc / GC |
| --- | --- | --- | --- | --- | --- |
| `Utils.BmpHash(1520×865)` | `4.004 ms` | **`1.941 ms`** | **2.06×** | `5,706.4 KB/op` (`49/49/49` GC) | **`0.2 KB/op` (`0/0/0` GC)** |
| `FrameHealth.IsBlank(blank)` | `0.907 ms` | **`0.037 ms`** | **24.5×** | `0.0 KB/op` | `0.1 KB/op` (`0/0/0`) |
| `FrameHealth.IsBlank(game)` | `0.002 ms` | **`0.001 ms`** | **2.0×** | `0.0 KB/op` | `0.1 KB/op` (`0/0/0`) |
| `GameCoordinates.Normalize` | `17.354 ms` | **`3.727 ms`** | **4.66×** | `0.1 KB/op` | `0.1 KB/op` (`0/0/0`) |
| `ScreenshotCache.CompressJpeg` | `19.036 ms` | **`12.634 ms`** | **1.51×** | `53.8 KB/op` (+ native GDI+ leak) | `89.2 KB/op` (`1/0/0`, **0 native leak**) |
| `AdbCapture.DecodeRawScreencap` | `5.530 ms` | **`4.739 ms`** | **1.17×** | `0.1 KB/op` (+ 11.5 MB LOH/frame ở `CaptureRawBytes`) | `0.1 KB/op` (**0 MB LOH/frame** steady-state) |

### C. Live Integration Test trên LDPlayer thực tế

| Kịch bản | Trạng thái | Ghi chú |
| --- | --- | --- |
| Win32 capture dưới Chrome & VS Code | **PASS (TASK 03/06)** | Xem `docs/BACKGROUND_MODE_QA.md` (chưa chạy lại sau bản sửa Phase 4/5). |
| 2 chu kỳ Replay khi bị VS Code che phủ (Win32) | **PASS (TASK 06)** | 42.9s và 85.9s, không phát hiện đổi foreground (`docs/BACKGROUND_MODE_QA.md`). |
| LDPlayer bị minimize (Win32) | **PASS (quan sát sớm)** | Báo `Window invalid` và dừng/pause không tự restore. |
| ADB capture khi LDPlayer ẩn (`hwnd=0`) | **PASS (2026-11-26)** | `--live-adb-capture LDPlayer` trên `emulator-5556` (bản Phase 5): 10/10 frames, 9 thay đổi, mean `545.9 ms`, max `688.3 ms` (trước đó `352–424 ms`), `sky=True`, `menu=True`. |
| ADB input Replay đơn lẻ (`emulator-5556`) | **PASS (2026-11-26)** | `--live-adb-replay LDPlayer`: thoát menu vào trận, foreground và chuột không đổi. |
| ADB 2 chu kỳ Replay liên tiếp khi LDPlayer ẩn (`hwnd=0`) | **PASS (2026-11-26)** | `--live-adb-cycles LDPlayer 2` trên bản Phase 5: cycle 1 `38.4s` (`50` battleFrames), cycle 2 `37.1s` (`53` battleFrames), tổng `83.3s`, `118` capture attempts, `0` retries, foreground & cursor không đổi. |
| Kiểm thử live WPF UI bản Phase 5 (`artifacts\phase5-build\gca.exe`) | **CHƯA KIỂM THỬ** | Đã build tách biệt vào `artifacts\phase5-build\gca.exe`; chờ người dùng chạy thử trực tiếp trên WPF UI. |

### D. Manual Test (WPF UI & Full Farm Features)

- Nút Start/Pause/Resume/Stop và phím tắt trong khi chạy farm thực tế: **CHƯA KIỂM THỬ** (sau bản sửa `TASK-12`).
- Auto Battle, Dungeon/Dragon, Upgrades, Ads, CAPTCHA, chạy đa instance, Foreground mode end-to-end: **CHƯA KIỂM THỬ**.
- Soak test chạy liên tục nhiều giờ + đo CPU/RAM thực tế trên WPF UI: **CHƯA KIỂM THỬ** (thuộc Phase 5 & Phase 7).

> Lưu ý: Kết quả 98/98 assertions và `--benchmark` ở mục A/B là Unit/Deterministic Integration Test và **không** thay thế cho kiểm thử chạy dài hạn thực tế trên LDPlayer + WPF UI.
