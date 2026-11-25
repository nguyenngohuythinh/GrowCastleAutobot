# Hướng dẫn sử dụng GrowCastleAutobot (Strict Background Mode)

Tài liệu này hướng dẫn cách thiết lập, vận hành và xử lý sự cố cho **GrowCastleAutobot (GCA)** với chế độ **Strict Background Mode** (Win32 và ADB) trên Windows + LDPlayer 9.

---

## 1. Yêu cầu hệ thống & cấu hình LDPlayer

- **Hệ điều hành**: Windows 10/11 64-bit.
- **Giả lập**: LDPlayer 9 (đã kiểm thử trên phiên bản `9.5.45.0`).
- **Độ phân giải Android bắt buộc trong LDPlayer**:
  - **1600 × 900**, **DPI 240**, chế độ màn hình ngang (Landscape 16:9).
- **Khuyến nghị chung**:
  - Đặt tên cửa sổ LDPlayer rõ ràng (ví dụ: `LDPlayer` hoặc `LDPlayer-1`) và nhập **đúng tên đó** vào ô `Window name` trên giao diện GCA.
  - Không thay đổi tỷ lệ khung hình sang dọc hoặc tỉ lệ khác 16:9.

---

## 2. So sánh 3 chế độ hoạt động

| Chế độ | Tùy chọn trên GCA | Hỗ trợ che cửa sổ (Chrome/VS Code) | Hỗ trợ Minimize / Boss Key (`Ctrl+Q`) | Chiếm chuột / focus |
| --- | --- | --- | --- | --- |
| **Background ADB** (khuyến nghị khi muốn ẩn hoàn toàn) | Bật `Background mode` + `Capture inner window` + `Use ADB (allow minimize/hide LD)` | Có | **Có** | **Không** |
| **Background Win32** | Bật `Background mode` + `Capture inner window` (tắt `Use ADB`) | **Có** | Không (nếu minimize bot sẽ tự Pause an toàn, không tự restore) | **Không** |
| **Foreground** (chế độ gốc) | Tắt `Background mode` | Không | Không | Có |

> **Nguyên tắc Strict Background Mode**: Khi bật `Background mode`, bot **không bao giờ** di chuyển con trỏ chuột vật lý, **không bao giờ** tự đưa LDPlayer/GCA lên foreground, **không** tự mở lại cửa sổ đang minimize, và **không** tự chuyển lùi sang Foreground Mode khi gặp lỗi.

---

## 3. Thiết lập từng bước

### Bước 1: Chuẩn bị LDPlayer
1. Mở **LDPlayer Settings → Display**: chọn độ phân giải **1600 × 900**, **DPI 240**.
2. Nếu dùng **Background ADB** (để minimize hoặc ẩn bằng Boss Key `Ctrl+Q`):
   - Mở **LDPlayer Settings → Other settings → ADB debugging** → chọn **Open local connection** (Kết nối cục bộ).
   - Lưu cài đặt và **khởi động lại LDPlayer** một lần để ADB nhận thiết bị (`emulator-5554`, `emulator-5556`, ...).
3. Mở game **Grow Castle** và vào màn hình chính (menu/trận).

### Bước 2: Cấu hình GCA
1. Mở `gca.exe` trong thư mục build (không tách riêng file `gca.exe` khỏi thư mục chứa các file `.dll` đi kèm).
2. Nhập đúng tên cửa sổ giả lập vào ô **`Window name`** (không kèm phiên bản LDPlayer, ví dụ `LDPlayer`).
3. Chọn **Build** và chế độ farm (`Replays`, `AB mode`, `Farm dungeon`, ...).
4. Trong mục **Background**:
   - Đánh dấu **`Background mode`** và **`Capture inner window`**.
   - Nếu muốn ẩn/minimize LDPlayer: đánh dấu thêm **`Use ADB (allow minimize/hide LD)`**.
   - Quan sát dòng trạng thái màu xanh phía dưới checkbox để xác nhận chế độ đang chọn (`Mode: Background ADB ...` / `Mode: Background Win32 ...`).

### Bước 3: Chạy, Tạm dừng và Dừng an toàn
- **Start / Pause / Resume**: Bấm nút **Start/Pause** trên giao diện hoặc dùng phím tắt (mặc định `Alt+F1`).
- **Stop**: Bấm nút **Stop** hoặc dùng phím tắt (mặc định `Alt+F2`). Khi đang ở trạng thái `Paused`, bạn vẫn có thể bấm **Stop** ngay lập tức mà không bị treo.
- **Theo dõi trạng thái nhanh**:
  - Tiêu đề cửa sổ & icon khay hệ thống (System Tray) hiển thị trực tiếp trạng thái và chế độ (ví dụ `GCA — Running [ADB]`, `GCA — Paused [Win32]`).
  - Dòng trạng thái trong mục Background hiển thị: chế độ, thiết bị/HWND đích, số trận đã hoàn thành (`Battles`), độ trễ capture gần nhất (`Capture: ... ms`), và số lần retry (`Retries`).

---

## 4. Xử lý các thông báo lỗi thường gặp

Khi gặp sự cố trong Background Mode, GCA hiển thị thông báo lỗi màu đỏ trực tiếp trong cửa sổ GCA và chuyển sang trạng thái **Paused** (không bật popup giành focus):

| Tiền tố lỗi | Nguyên nhân phổ biến | Cách khắc phục |
| --- | --- | --- |
| `Window invalid:` | LDPlayer đang bị minimize (chế độ Win32), cửa sổ đã đóng, sai `Window name`, hoặc chưa bật ADB debugging (chế độ ADB). | Kiểm tra lại tên cửa sổ; khôi phục cửa sổ LDPlayer (Win32) hoặc bật `ADB debugging → Local connection` rồi khởi động lại LDPlayer (ADB), sau đó bấm **Resume**. |
| `Capture failed:` | Lỗi chụp ảnh / timeout (`PrintWindow` quá 2s hoặc `adb screencap` quá 4s) hoặc khung hình đen. | Bot tự retry có backoff tối đa 2 lần; nếu vẫn lỗi sẽ Pause. Kiểm tra LDPlayer có bị treo hay không, sau đó bấm **Resume** (hoặc **Stop**). |
| `Stale frame:` | Màn hình không đổi liên tiếp 15 giây sau khi đã gửi thao tác. | Kiểm tra xem game Grow Castle có bị đứng hình / mất kết nối hay bị che bởi popup hệ thống Android không. |
| `Input failed:` | Không gửi được thao tác click/phím (cửa sổ đóng hoặc ADB shell ngắt kết nối). | Kiểm tra trạng thái LDPlayer / kết nối ADB, sau đó bấm **Resume** hoặc **Stop → Start**. |
| `Game state unknown:` | Không nhận diện được màn hình game (bầu trời/menu) sau thời gian chờ. | Mở lại đúng màn hình Grow Castle (đóng popup quảng cáo/hệ thống), sau đó bấm **Resume**. |
| `Restart limit reached:` / `Restart loop prevented:` | Đạt giới hạn chống vòng lặp restart vô hạn (`RestartGuard`). | Kiểm tra log `gc.log` để tìm nguyên nhân gốc khiến game/giả lập không vào được trước khi bấm **Start** lại. |

---

## 5. Build & chạy kiểm thử từ mã nguồn

Từ thư mục gốc repository (sử dụng .NET SDK `8.0.319` tại `.dotnet/dotnet.exe`):

```powershell
# Build bản Release tách biệt
.\.dotnet\dotnet.exe build GrowCastleAutobot\gca\gca.csproj -c Release -o artifacts\release-build

# Chạy toàn bộ bộ kiểm thử tự động (Unit & Deterministic Integration)
.\.dotnet\dotnet.exe run -c Release --project GrowCastleAutobot\gca.Tests

# Chạy benchmark hiệu năng xử lý ảnh
.\.dotnet\dotnet.exe run -c Release --project GrowCastleAutobot\gca.Tests --no-build -- --benchmark 100

# Kiểm thử live trên LDPlayer thật (thay LDPlayer bằng tên instance của bạn)
.\.dotnet\dotnet.exe run -c Release --project GrowCastleAutobot\gca.Tests --no-build -- --live-adb-capture LDPlayer
.\.dotnet\dotnet.exe run -c Release --project GrowCastleAutobot\gca.Tests --no-build -- --live-adb-cycles LDPlayer 2
```

---

## 6. Trạng thái kiểm thử & giới hạn đã biết

- **Đã xác minh trên LDPlayer 9.5.45.0 thực tế**:
  - Background Win32: chụp dưới Chrome/VS Code và chạy 2 chu kỳ Replay khi bị VS Code che phủ, không đổi foreground/cursor.
  - Background ADB: chụp màn hình và chạy 2 chu kỳ Replay liên tiếp khi LDPlayer bị ẩn hoàn toàn (`hwnd=0`), 0 retry, không đổi foreground/cursor.
  - Bộ kiểm thử tự động (`gca.Tests`) xác minh tọa độ, chuẩn hóa ảnh, capture timeout, retry backoff, `RestartGuard`, logging đa luồng + xoay file, Stop/Pause an toàn, và tối ưu hiệu năng Phase 5.
- **Chưa hoàn tất kiểm thử thủ công toàn diện**:
  - Chạy dài hạn nhiều giờ (soak test) trên giao diện WPF đầy đủ.
  - Smoke test thủ công từng tính năng nâng cao (Auto Battle dài hạn, Dungeon/Dragon, Upgrades, Ads, CAPTCHA, chạy song song nhiều instance).
- Chi tiết kết quả mới nhất xem tại [TEST_REPORT.md](../TEST_REPORT.md) và [BACKGROUND_MODE_QA.md](BACKGROUND_MODE_QA.md).
