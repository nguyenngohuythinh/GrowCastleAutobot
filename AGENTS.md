# AGENTS.md — Quy tắc làm việc cho Coding Agent

Tài liệu này quy định các nguyên tắc bắt buộc khi làm việc trên repository **GrowCastleAutobot**.

## 1. Quy trình bắt đầu mỗi phiên làm việc

1. Đọc theo thứ tự: `HANDOFF.md` → `PROGRESS.md` → `TASKS.md` → `TEST_REPORT.md`.
2. Kiểm tra trạng thái Git (`git branch -a`, `git status --short`) trước khi sửa bất kỳ file nào.
3. Không mặc định cấu hình máy hoặc trạng thái LDPlayer; luôn xác minh thực tế từ hệ thống / log.

## 2. Nguyên tắc bắt buộc về Strict Background Mode

- Không di chuyển con trỏ chuột vật lý hoặc gọi `SetCursorPos`/`mouse_event` trong Background Mode.
- Không chiếm bàn phím vật lý hoặc gọi `SetForegroundWindow` để đưa LDPlayer / GCA lên foreground.
- Không tự động fallback sang Foreground Mode khi Background capture/input thất bại.
- Không tự động restore/maximize cửa sổ LDPlayer đang minimize (Win32 mode phải pause an toàn).
- Không sử dụng ứng dụng bên thứ ba bổ sung hoặc máy ảo phụ.
- Bảo toàn các chức năng cũ (Foreground Mode, Auto Replay, Auto Battle, Dungeon, Upgrades, Ads, CAPTCHA).

## 3. Quy chuẩn kỹ thuật & Git

- Làm việc trên feature branch (hiện tại: `feature/true-background-mode`), không sửa trực tiếp `main`.
- Không tự ý `git push`, `git merge`, tạo release hoặc deploy nếu chưa được người dùng yêu cầu.
- Không ghi đè công việc chưa commit của người dùng; kiểm tra diff trước và sau khi sửa.
- Giữ nguyên quy ước đặt tên source code C#/WPF hiện có; viết tài liệu & báo cáo bằng tiếng Việt.
- Sử dụng `.dotnet/dotnet.exe` (SDK `8.0.319` theo `GrowCastleAutobot/global.json`) để build và chạy `gca.Tests`.
- Phân biệt rõ **Unit Test**, **Integration Test**, **Manual Test** và **Chưa kiểm thử**. Không dùng kết quả Unit Test để khẳng định LDPlayer thực tế đã hoạt động.

## 4. Quy chuẩn cập nhật tiến độ

- Chỉ đánh dấu task `DONE` khi đã đạt đủ điều kiện nghiệm thu và vượt qua kiểm thử phù hợp.
- Nếu thiếu điều kiện kiểm thử thực tế với LDPlayer, giữ trạng thái `TESTING` hoặc `BLOCKED`.
- Sau mỗi task, cập nhật ngay `TASKS.md`, `PROGRESS.md`, `TEST_REPORT.md`, `CHANGELOG.md` và `HANDOFF.md`.
