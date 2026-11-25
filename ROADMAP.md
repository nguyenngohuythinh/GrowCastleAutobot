# ROADMAP.md — Lộ trình phát triển GrowCastleAutobot

Cập nhật: 2026-11-26 (Asia/Bangkok)

Lộ trình dưới đây được điều chỉnh theo tình trạng thực tế của repository (branch `feature/true-background-mode`).

| Phase | Tên giai đoạn | Trạng thái thực tế | Ghi chú |
| --- | --- | --- | --- |
| Phase 0 | Repository Audit | **DONE** | Đã kiểm toán mã nguồn, cấu hình máy thực tế, lịch sử commit, và tồn tại `docs/BACKGROUND_MODE_*.md`. |
| Phase 1 | Background Input | **DONE (Win32) / TESTING (ADB)** | `BackgroundInput` (Win32) đã tách biệt, có guard chống foreground fallback, đã test Replay phủ cửa sổ. `AdbInput` đã vượt qua 2 chu kỳ ADB Replay liên tiếp khi LDPlayer ẩn (`hwnd=0`), cần thêm kiểm thử tính năng mở rộng. |
| Phase 2 | Background Capture | **DONE (Win32) / TESTING (ADB)** | `WindowCapture` chuẩn hóa 1520×865, có `FrameHealth`, timeout single-flight 2s, xử lý late bitmap. `AdbCapture` đã có timeout 4s, drain stream an toàn và tái sử dụng buffer raw (0 LOH/frame). |
| Phase 3 | Strict Background Mode | **DONE (Replay scope) / PARTIAL (Full QA)** | Đã vượt qua 2 chu kỳ Replay khi bị VS Code che phủ (Win32) và 2 chu kỳ Replay khi LDPlayer ẩn (ADB). Còn các kịch bản manual QA mở rộng. |
| Phase 4 | Stability & Recovery | **IN_PROGRESS** | Đã hoàn thành harden capture timeout, retry policy, `RestartGuard` chống vòng lặp restart vô hạn, logging thread-safe có xoay file, và Stop/Pause an toàn (98/98 automated assertions). Cần kiểm thử chạy dài hạn (soak test) thực tế. |
| Phase 5 | Performance Optimization | **IN_PROGRESS** | Đã đo baseline BEFORE/AFTER và tối ưu hot-path xử lý ảnh (`BmpHash`, `Normalize`, `IsBlank`, `AdbCapture`, `ScreenshotCache`, sửa rò rỉ `Colormode`), giảm ~15.7 ms/frame và loại bỏ 5.7–17.2 MB LOH/frame (0 Gen2 GC). Còn soak test nhiều giờ đo CPU/RAM thực tế trên WPF UI. |
| Phase 6 | User Experience | **TESTING** | Đã bổ sung dòng trạng thái thời gian thực (chế độ ADB/Win32/Foreground, thiết bị/HWND đích, số trận hoàn thành, độ trễ capture, số retry), tiêu đề cửa sổ & system tray động, gợi ý Pause/Stop rõ ràng (`102/102` assertions). Chờ kiểm thử trực quan trên WPF UI. |
| Phase 7 | Quality Assurance | **IN_PROGRESS** | Đã có 102 automated assertions + kết quả live Win32 (TASK 03–06) + 2 chu kỳ live ADB khi ẩn LDPlayer; cần hoàn thiện ma trận manual QA đầy đủ trên LDPlayer thực tế. |
| Phase 8 | Release Preparation | **IN_PROGRESS** | Đã hoàn thiện hướng dẫn tiếng Việt (`docs/HUONG_DAN_SU_DUNG.md`), cập nhật `README.md`, tổng hợp `TEST_REPORT.md`/`CHANGELOG.md`; sẵn sàng tách commit/PR khi người dùng xác nhận (không tự publish). |
