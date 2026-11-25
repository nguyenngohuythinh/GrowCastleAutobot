# PROGRESS.md — Tiến độ và trạng thái hiện tại

Cập nhật: 2026-11-26 03:20 (Asia/Bangkok)

## Trạng thái tổng quan

- **Branch hiện tại**: `feature/true-background-mode` (chưa merge vào `main`).
- **Build Release (`artifacts\phase6-build\gca.exe`)**: **PASS** (`0 errors`, 7 warning sẵn có ở `SystemMonitor.cs`, không phát sinh warning mới).
- **Automated Regression (`gca.Tests`)**: **PASS — 102 / 102 assertions** (tăng +4 assertions cho UX runtime status snapshot ở Phase 6).
- **Kiểm tra Encoding toàn bộ file đã tạo/chỉnh sửa**: **PASS** — 100% file tiếng Việt là UTF-8 hợp lệ (`TOTAL_BAD_FILES=0`), 100% file source code hiện có giữ nguyên BOM gốc so với `HEAD`.
- **Trạng thái nghiệm thu thực tế (Live LDPlayer)**:
  - Win32 Strict Background Mode (Replay khi bị VS Code che phủ, Capture dưới Chrome): **ĐÃ XÁC MINH** ở TASK 03–06 (`docs/BACKGROUND_MODE_QA.md`).
  - Chế độ ADB khi ẩn LDPlayer (`hwnd=0`, `emulator-5556`) trên bản Phase 4+5: **ĐÃ PASS** 10/10 frame capture và **2 chu kỳ ADB Replay liên tiếp** (`83.3s`, `118` capture attempts, `0` retries, foreground & cursor không đổi).
  - Bản build `artifacts\phase5-build\gca.exe`: đã khởi chạy thành công trên Windows (`Responding=True`, nạp đúng `clickerSettings.json` ADB, đăng ký phím tắt `Alt+F1`/`Alt+F2`, 0 lỗi log).
  - Các tính năng farm mở rộng, kiểm thử trực quan WPF UI `phase6-build` & soak test nhiều giờ: **CHƯA KIỂM THỬ** (`TASK-13`, `TASK-14`, `TASK-15` giữ trạng thái `TESTING`).

## Các hạng mục vừa hoàn thành

1. **Cải thiện UX hiển thị trạng thái & Hướng dẫn tiếng Việt** (`TASK-15` — Phase 6 & 8):
   - Bổ sung `RuntimeStatusSnapshot`, event `OnRuntimeStatusUpdate` và dòng `BackgroundModeStatusText` hiển thị rõ chế độ (`Background ADB` / `Background Win32` / `Foreground`), thiết bị/HWND đích, số trận hoàn thành (`Battles`), độ trễ capture (`Capture: ... ms`) và số retry (`Retries`).
   - Cập nhật động `Window.Title` & `trayIcon.Text` theo trạng thái/chế độ (`GCA — Running [ADB]`, `GCA — Paused [ADB]`), bổ sung gợi ý `Resume or Stop (phím tắt)` khi `Paused`.
   - Tạo tài liệu tiếng Việt [docs/HUONG_DAN_SU_DUNG.md](docs/HUONG_DAN_SU_DUNG.md) và cập nhật [README.md](README.md).
2. **Đo baseline & tối ưu hiệu năng Phase 5** (`TASK-14`):
   - `Utils.BmpHash`: `4.004 ms → 1.941 ms`, `5,706.4 KB/op (49 Gen2 GC)` → `0.2 KB/op (0 GC)`.
   - `GameCoordinates.Normalize`: `17.354 ms → 3.727 ms` (4.66×, khớp pixel-for-pixel với GDI+).
   - `FrameHealth.IsBlank(blank)`: `0.907 ms → 0.037 ms` (24.5×).
   - `AdbCapture` tái sử dụng buffer raw (bỏ 11.5 MB LOH/frame), sửa rò rỉ GDI+ trong `ScreenshotCache` & `Colormode`.
3. **Xác minh Live ADB 2 chu kỳ liên tiếp** (`TASK-13`) & **Kiểm toán encoding toàn repo**.
4. **Hoàn thành Phase 4 trước đó** (`TASK-08`..`TASK-12`).

## Việc tiếp theo đề xuất

1. Chạy thử bản build mới nhất `D:\GrowCastleAutobot\artifacts\phase6-build\gca.exe` để kiểm thử trực quan dòng trạng thái UX mới, Pause/Resume/Stop, phím tắt và soak test dài hạn (`TASK-13`/`TASK-14`/`TASK-15`).
2. Rà soát diff và tách commit gọn gàng trên `feature/true-background-mode` khi người dùng xác nhận (không tự push/merge).
