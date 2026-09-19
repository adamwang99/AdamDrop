# AdamDrop — hướng dẫn cho trợ lý AI cài giúp trên máy Windows

Bạn (AI agent) đang được nhờ cài công cụ này lên máy Windows của người dùng.
Toàn bộ mã nguồn đã có trong gói, **không cần viết lại hay tìm hiểu thêm**.
Người dùng đã đọc HUONG-DAN.txt; bạn chỉ cần làm phần máy tính và hỗ trợ khi họ tạo Phím tắt iOS.

## PC to iPhone sharing (2026-09)

- Keep six dashboard tabs. Overview / Tổng quan contains collapsed Send to iPhone / Gửi sang iPhone.
- Choose files / Chọn tệp opens a native multi-select dialog. Only its selections enter the in-memory queue.
- Existing QR/key opens mobile page: Receive from PC / Nhận từ PC. Files uses Safari Downloads; Photos opens the media INSIDE the page (`#viewer`) so the user can touch-and-hold and pick Save to Photos — a new tab must not be used, iOS Safari tends to download it to Files instead. Web pages cannot write to the photo library; that last touch is always the user's.
- POST `/api/share/pick`, `/api/share/remove?id=...`, `/api/share/stop` require local trusted Host + `X-AdamDrop-Admin` session token from local `/api/state`; cross-origin requests are denied. Never pass file paths via HTTP.
- GET `/shares?k=...` returns opaque ID, name, size, MIME only; `/download?k=...&id=...` streams attachments. `&view=1` permits inline only for the explicit safe image/video MIME allowlist. **Never send a `Content-Security-Policy` containing `sandbox` on these responses** — WebKit (Safari on iOS) treats it as a reason to download instead of render, which silently breaks "Save to Photos" and makes attachments fail. Attachments carry `default-src 'none'` plus an ASCII-safe `filename=` fallback and `filename*=UTF-8''…`. Range/HEAD supported; If-Range conservatively sends the full file. Removal revokes new and active server transfers, not copies already downloaded.
- Build: stop only after `/api/state` reports no `live` uploads or `activeDownloads`; run `python build.py`. A compiler error is fatal. No dependencies beyond Python for this build helper; installed app remains .NET Framework only.
- Regression: with normal app stopped and port 8765 free, `python tests/run-sharing.py`. It compiles a separate test main, stores every fixture/config/history in a unique `%TEMP%/adamdrop-sharing-*` directory, and stops its own process. Uses 8765 because this machine only reserves that HTTP.sys port. Restore normal `AdamDrop.exe` afterward. Test entry point is never compiled into production.
- `tests/share-test.cs` supplies trusted selected fixture paths in-process, not through a network endpoint. Native picker and real iPhone Photos save need manual verification.

## Công cụ này làm gì
Một chương trình nhỏ (`AdamDrop.exe`, tự biên dịch từ `AdamDrop.cs` bằng `csc.exe`
có sẵn trong .NET Framework 4 của Windows) chạy ở khay hệ thống, giữ sẵn cổng 8765
trong mạng LAN. Hai chiều đều chạy trên cùng mã QR và mã khoá đó:

- **iPhone → PC:** gửi ảnh/video bằng Safari (trang `web/index.html`) hoặc bằng một Phím tắt
  iOS (Get Contents of URL, POST, body = File). Tệp lưu vào thư mục do người dùng chọn
  (mặc định Downloads) và được ghi lại vào lịch sử chia sẻ.
- **PC → iPhone:** người dùng mở Tổng quan ▸ **Gửi sang iPhone** ▸ **Chọn tệp** (hộp thoại
  Windows, chọn nhiều tệp), iPhone quét QR rồi chọn **Nhận từ PC** để tải. Chỉ những tệp
  được chọn mới được chia sẻ; không mở thư mục nào của máy tính.

Bảng điều khiển tại `http://localhost:8765/dashboard` (chỉ mở được từ chính máy đó):
tổng quan, lịch sử chia sẻ, tốc độ cao nhất / thấp nhất / trung bình, quản trị phiên
đang nhận, cài đặt (thư mục lưu, mã khoá, tuỳ chọn) và nhật ký.

## Việc bạn cần làm trên máy tính
1. Chép nguyên thư mục này tới một nơi cố định, khuyến nghị `%USERPROFILE%\Documents\AdamDrop`.
   Giữ nguyên cấu trúc: `AdamDrop.cs`, `install.bat`, `uninstall.bat`, `web\index.html`,
   `web\dashboard.html`, `web\qrcode.js`, `web\guide\*.png`.
2. Chạy `install.bat` **với quyền quản trị** (nó tự xin UAC; nếu bạn không thể bấm UAC,
   nhờ người dùng bấm đúp file và chọn Yes). Script này:
   - biên dịch 1 tệp exe duy nhất, nhúng cả trang web và logo (xem mục "Tệp web" bên dưới
     để biết danh sách `/resource:` đang dùng)
   - `netsh http add urlacl url=http://+:8765/ sddl="D:(A;;GX;;;WD)"`
   - `netsh advfirewall firewall add rule name="AdamDrop" dir=in action=allow protocol=TCP localport=8765 profile=private,domain,public`
   - gỡ các mục cũ tên "iPhoneDrop" (nếu máy từng cài bản trước)
   - ghi khoá khởi động `HKCU\...\Run\AdamDrop`, tạo lối tắt trên Desktop (chạy kèm `--show`)
   - chạy chương trình bằng quyền người dùng thường rồi mở `http://localhost:8765/dashboard`
3. Kiểm tra: bảng điều khiển hiện được, có mã QR và ít nhất một địa chỉ `192.168.x.x`;
   `adamdrop.ini` xuất hiện cạnh exe với `key=`, `savedir=`, `openfolder=`, `keepawake=`.
4. Người dùng muốn đổi thư mục lưu: **không cần sửa tệp** — bảng điều khiển > Cài đặt >
   Chọn thư mục trên máy tính, hoặc chuột phải biểu tượng khay > Đổi thư mục nhận tệp…

## Việc người dùng làm trên iPhone (bạn hướng dẫn, không làm thay được)
Làm theo **Phần C trong HUONG-DAN.txt**. Ba chỗ hay vướng, đã gặp thực tế trên iOS 26:
- "Hiển thị trong Bảng chia sẻ" (Show in Share Sheet) **không phải tác vụ**; nó xuất hiện khi
  bấm vào chữ xanh cuối của khối "Nhận … đầu vào từ …" ở đầu phím tắt (khối này tự sinh sau
  khi chọn "Đầu vào phím tắt" làm nguồn của "Lặp lại với từng mục"), hoặc trong nút ⓘ.
- Khi dán URL vào "Lấy nội dung của URL", iOS tự chèn một nhãn biến ("Lặp lại kết quả")
  vào đầu ô. Phải xóa nhãn đó, nếu không URL sai.
- Mục "Tệp" của Request Body phải là biến **Mục lặp lại / Lặp lại mục (Repeat Item)**,
  không phải "Đầu vào phím tắt".

Cấu trúc phím tắt đúng:
```
Receive [Images, Media, Files] input from Share Sheet
Repeat with Each item in [Shortcut Input]
    Get Contents of URL  http://<tên-máy>.local:8765/upload?k=<key>
        Method: POST · Request Body: File · File: [Repeat Item]
End Repeat
(Show Notification "Đã gửi xong")   ← tùy chọn
```

## Giao thức (nếu cần kiểm tra bằng curl từ máy khác trong LAN)
```
POST /upload?k=<key>[&name=<tên tệp>][&mtime=<ms epoch>][&ifnew=1][&sid=<mã phiên>&off=<vị trí>&total=<cỡ tệp>]
     body = nội dung tệp thô (không multipart). Thiếu name → tự đặt IMG_/VID_ + thời gian,
     đuôi đoán theo magic bytes (JPEG/PNG/HEIC/MOV/MP4...) rồi Content-Type.
     Cũng nhận multipart/form-data (bảng chia sẻ iOS, curl -F, FormData): lấy phần đầu tiên
     có filename, bỏ qua các ô nhập chữ đứng trước. Multipart không gửi tiếp từng đoạn được.
     ifnew=1 → nếu thư mục lưu đã có tệp CÙNG TÊN thì bỏ qua (đọc hết body rồi trả
     {"ok":true,"dup":true,"name":...}), dùng cho Phím tắt tự động chạy nhiều lần để không
     sinh bản (1)(2). Không có ifnew thì hành vi cũ giữ nguyên: vẫn tạo (1), (2).
     Có sid/off/total = gửi theo từng đoạn: trả {"ok":false,"error":"partial","got":N} khi
     còn dở và {"ok":true,"done":true,...} khi xong. Sai key → 403, sai quá nhiều → 429.
GET  /resume?k=<key>&sid=<mã phiên>   → {"ok":true,"got":N}  (đã nhận được bao nhiêu byte)
GET  /?k=<key>                        trang gửi cho iPhone
GET  /AdamDrop.shortcut?k=<key>       tệp Phím tắt để thêm AdamDrop vào bảng chia sẻ iOS
GET  /AdamDrop.auto.shortcut?k=<key>  tệp Phím tắt tự động (gửi ảnh trong ngày, kèm ifnew=1)
GET  /dashboard                       bảng điều khiển (chỉ từ chính máy)
GET  /shares?k=<key>                  danh sách tệp PC đang chia sẻ: id, tên, dung lượng, MIME
                                      (không bao giờ trả đường dẫn thật trên máy tính)
GET  /download?k=<key>&id=<id>[&view=1]
                                      tải tệp đang chia sẻ; hỗ trợ Range/HEAD, tên UTF-8;
                                      view=1 chỉ xem trước với ảnh/video, loại khác luôn tải về;
                                      bỏ chia sẻ thì tệp trả 404, tệp bị di chuyển trả 410
POST /api/share/pick                  mở hộp thoại chọn tệp trên máy tính (nhiều tệp).
                                      Bấm lại khi hộp thoại đang mở: KHÔNG báo lỗi, trả
                                      {"ok":true,"alreadyOpen":true,"focused":...} và đưa hộp thoại
                                      đang mở lên trước (Windows hay mở nó sau cửa sổ trình duyệt).
POST /api/share/remove?id=<id>        bỏ chia sẻ một tệp
POST /api/share/stop                  dừng chia sẻ tất cả, cắt cả lượt tải đang chạy
                                      Ba lệnh này CHỈ chạy từ chính máy tính: IsLocal +
                                      Origin/Host hợp lệ + header X-AdamDrop-Admin lấy từ
                                      /api/state. Không nhận đường dẫn tệp qua HTTP.
GET  /api/state /api/history /api/log /api/parts /api/set /api/open /api/pickfolder /api/cancel
GET  /ping                            "ok" (CORS *) — trang iPhone dùng để dò tên .local
```
Muốn thử nhanh: `curl -F "file=@anh.jpg" "http://localhost:8765/upload?k=<key>"`

## Tệp web (nhúng trong exe — thêm tệp là phải sửa cả 3 nơi)
`build-adamdrop.sh`, `install.bat` và bảng route trong `AdamDrop.cs` đều có danh sách này:
`web/index.html`, `web/dashboard.html`, `web/qrcode.js`, `web/logo.png` (mark chữ A, 512),
`web/logo-small.png` (bản rút gọn 180px, dùng ở đầu trang và ô nhỏ), `web/logo-180.png`,
`web/logo.ico` (8 cỡ, biểu tượng khay), `web/adam-chan-dung.png` (chân dung ở tab Giới thiệu),
`web/AdamDrop.shortcut` (Phím tắt iOS cho bảng chia sẻ, tạo bằng `lam-shortcut.py`),
`web/AdamDrop.auto.shortcut` (Phím tắt tự động cho Tự động hoá Wi-Fi, tạo bằng
`lam-shortcut-auto.py`; cả hai đều là plist NHỊ PHÂN và chứa tên máy + mã khoá của lúc sinh,
nên đổi mã khoá phải sinh lại rồi cài lại trên iPhone).

## Việc người dùng làm trên iPhone (bạn hướng dẫn, không làm thay được)
**Cách chính — cài sẵn nút vào bảng chia sẻ (không phải gõ gì):**
1. Mở app Phím tắt, chạy thử một phím tắt bất kỳ → bật `Cài đặt ▸ Phím tắt ▸ Cho phép phím
   tắt không tin cậy` (công tắc chỉ hiện sau khi đã chạy một phím tắt).
2. Trên máy tính mở bảng điều khiển ▸ tab Hướng dẫn ▸ mục "Gửi ngay trong bảng chia sẻ",
   dùng Camera trên iPhone quét mã QR nhỏ trong đó. Trang gửi mở ra, bấm nút vàng
   "Thêm AdamDrop bằng 1 chạm" → app Phím tắt mở ra với phím tắt sẵn sàng → bấm Thêm phím tắt.
   (Nút này dùng liên kết `shortcuts://import-shortcut/?name=...&url=...`; Apple thỉnh thoảng
   nuốt liên kết, nên luôn để lại đường tải tệp: "Hoặc: tải tệp về" → Tệp ▸ Tải về ▸ bấm tệp.)
3. Phím tắt ▸ AdamDrop ▸ ⓘ ▸ bật "Hiển thị trong Bảng chia sẻ".
4. Trong Ảnh: chọn ảnh/video ▸ Chia sẻ ▸ AdamDrop. Lần đầu: Luôn cho phép + cho phép Mạng cục bộ.
Phím tắt chứa sẵn tên máy và mã khoá; đổi mã khoá thì phải cài lại (quét lại QR).

**Cách tự động (không bấm gì) — Phím tắt tự động + Tự động hoá Wi-Fi:**
Apple cho phép Tự động hoá chạy nền KHÔNG cần xác nhận với kích hoạt Wi-Fi và Giờ trong ngày
(chỉ "Trước khi tôi đi làm" là không chạy tự động được — theo tài liệu Shortcuts bản mới nhất).
1. Cài thêm tệp thứ hai `AdamDrop.auto.shortcut` (QR ở mục `Tự động: về nhà là ảnh tự về máy
   tính (Wi-Fi)` trong tab Hướng dẫn, hoặc nút vàng `Thêm phím tắt tự động bằng 1 chạm` ở mục
   `🔁 Tự động: về nhà là ảnh tự về máy tính (Wi-Fi)` trên trang gửi). Tên phím tắt: `AdamDrop-auto`.
2. Phím tắt ▸ Tự động hoá ▸ + ▸ Wi-Fi ▸ tích mạng nhà (hoặc Mọi mạng) ▸ "Chạy ngay"
   (tắt "Hỏi trước khi chạy") ▸ chọn phím tắt AdamDrop-auto ▸ Xong.
3. Phím tắt tự động = `filter.photos` (lọc "Date Taken is today", sắp xếp mới nhất trước)
   → `repeat.each` → `downloadurl` POST `/upload?k=<key>&ifnew=1` với thân Tệp = Mục lặp lại
   → đóng vòng lặp → `notification`. Nội dung chi tiết: PHẦN C2 trong `HUONG-DAN.txt`.

**Cách tự làm bằng tay (khi tệp không cài được) — ĐƯỜNG CHÍNH trên iOS 26:** vào Phím tắt ▸
phím tắt mới ▸ `Lấy nội dung của URL` (địa chỉ `http://<tên-máy>.local:8765/upload?k=<key>`,
POST, Yêu cầu nội dung: Tệp = biến `Lặp lại mục`), rồi bật `Trong Bảng chia sẻ`. Bốn chỗ hay
vướng, đã gặp thực tế trên iOS 26 tiếng Việt:
- Nhãn tác vụ là **"Lấy nội dung của URL"** (Get Contents of URL) — không phải "Nhận", không phải "Mở URL".
  Máy gọi "hành động" là **"tác vụ"**, ô tìm là **"Tìm kiếm tác vụ"**.
- Dòng **"Yêu cầu nội dung"** (Request Body) mặc định là **JSON**: phải bấm chữ JSON ▸ chọn **Tệp**,
  rồi bấm ô **Tệp** ▸ chọn biến **Lặp lại mục** (Repeat Item). Người dùng hay báo "không thấy Phần
  thân yêu cầu" vì máy ghi "Yêu cầu nội dung".
- Ô **URL** hay dính sẵn biến xanh tên "URL": bấm vào biến ▸ dòng đỏ **Xóa biến** ▸ giữ ô trống ▸ **Dán**.
- iOS 26 ở màn ⓘ **không có** dòng tích Ảnh/Tệp/Phương tiện; chỉ cần bật công tắc **Trong Bảng chia sẻ**
  rồi bấm **dấu tích** ở góc phải trên. Tác vụ lạ "Nhận Ứng dụng và 18 mục khác từ Không nơi nào" thì xoá.

**Ảnh hướng dẫn trong app:** 8 ảnh chụp thật từ iPhone của Adam, có vòng tròn vàng đánh số vị trí,
nằm ở `web\guide\*.png` (`buoc1-cong-tac`, `buoc2-tim-kiem`, `buoc3-vong-lap`, `buoc4-lay-noi-dung`,
`buoc5-post`, `buoc6-tep`, `buoc7-bang-chia-se`, `ket-qua`), phục vụ ở `/guide/<tên>.png`
(`AdamDrop.cs`: cho máy này HOẶC khi có mã khoá), nhúng vào exe trong `build-adamdrop.sh`
(`web.guide.<tên>`). Dùng ở tab Hướng dẫn (EN+VI) và ở khối "Tự dựng phím tắt bằng tay" trên
trang gửi. Sửa chữ hướng dẫn thì phải sửa **cả hai nơi**, và giữ số trên ảnh khớp số trong chú thích.

## Tên mục: một chuẩn cho ba nơi (đừng đổi lệch)

Cùng một mục phải mang **đúng một tên** ở ba nơi, cả EN lẫn VI:

| Mục | Tên chuẩn (EN) | Tên chuẩn (VI) |
|---|---|---|
| Gửi một chạm | `One-tap send from the Share Sheet (like AirDrop)` | `Gửi một chạm từ bảng Chia sẻ (như AirDrop)` |
| Tự động Wi-Fi | `Automatic: photos come home on their own (Wi-Fi)` | `Tự động: về nhà là ảnh tự về máy tính (Wi-Fi)` |

- `web/index.html`: `scSummary`, `autoSummary` là bản chuẩn (có thêm ⚡ / 🔁 ở đầu).
- `web/dashboard.html`: tiêu đề trong `buildGuide()` (cả mảng `en` và `vi`) phải trùng chữ đó.
- `HUONG-DAN.txt`: chép nguyên văn **cả hai thứ tiếng**, vì giao diện mặc định là TIẾNG ANH.
- Nút trên trang gửi: `Thêm AdamDrop bằng 1 chạm` / `Add AdamDrop in 1 tap` (`btnImport`),
  `Thêm phím tắt tự động bằng 1 chạm` / `Add the automatic shortcut in 1 tap` (`btnAuto`),
  nút dự phòng `Hoặc: tải tệp về` / `Or: download the file` (`btnSheet`, `btnAuto2`).
- Kiểm bằng `python $LOCALAPPDATA/Temp/kt-ten-muc.py` — phải in ra `TEN MUC THONG NHAT`.
- Số liệu trong hướng dẫn phải khớp mã: **6 thẻ**, `history.tsv` giữ **2000** đợt,
  `recent.tsv` **30** tệp, nhật ký **200** dòng, tab Hướng dẫn **10 mục**.

## Đừng làm
- Đừng cài Python/Node/LocalSend hay viết server mới; mọi thứ đã có sẵn và đã chạy thật.
- Đừng đổi cổng 8765 trong `install.bat` mà không đổi `port=` trong `adamdrop.ini` (và ngược lại).
- Đừng gõ tay địa chỉ cho người dùng; bảo họ bấm "Sao chép" trên trang Safari vì trang đó
  tự điền đúng tên máy (`Environment.MachineName`, chữ thường + `.local`) và key.
- Đừng xoá `history.tsv` (mất lịch sử chia sẻ) hay `adamdrop.ini.bak` khi đang hỗ trợ.
- Lưu ý đã biết: khi ổ đĩa gần đầy, Windows (HTTP.sys) không gửi được câu giải thích cho
  điện thoại nếu chưa nhận hết tệp; máy tính vẫn báo bong bóng + ghi nhật ký.

## Gỡ
`uninstall.bat` (xóa urlacl, rule tường lửa, khoá khởi động, lối tắt) rồi xóa thư mục.
