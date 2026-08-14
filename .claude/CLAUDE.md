# Chỉ Dẫn Cho Claude Code

Local print agent chạy trên máy POS **Windows** (.NET 8, C#,
`System.Drawing.Printing`/GDI+) — nhận lệnh in từ frontend `fafoshop` qua
HTTP nội bộ (`127.0.0.1:9199`), in thẳng ra máy in, không qua popup trình
duyệt, không phụ thuộc phần mềm in ấn thứ 3 (kiểu QZ Tray).

Đây là project ĐỘC LẬP thứ 4 trong workspace `fafoshop-workspace` (ngang
hàng `fafoshop`/`fafoshop-api`/`fafoshop-print-agent`, xem `../CLAUDE.md`) —
repo git riêng, thay thế `fafoshop-print-agent` (Java, `javax.print`) CHỈ
cho nhánh Windows.

**Đọc trước khi sửa gì**: `README.md` (lý do có bản .NET riêng, lệnh
build/publish/test cụ thể) + `../docs/pos-in-nhan-san-pham.md` mục "Root
cause lệch vị trí khi in nhãn" (chẩn đoán đầy đủ vì sao `javax.print`/
`java.awt.print` đọc SAI khổ giấy trên driver Windows còn GDI+ đọc ĐÚNG —
lý do project này tồn tại) + `../docs/pos-in-hoa-don.md` (thiết kế đầy đủ
tính năng in hóa đơn POS nói chung, không chỉ riêng agent này).

## Luật Không Được Phá Vỡ

- **[ƯU TIÊN CAO NHẤT] Bảo mật.** `HttpListener` bind CHỈ
  `http://127.0.0.1:{port}/` (tuyệt đối không đổi thành `0.0.0.0`/`+`/địa
  chỉ mạng ngoài — sẽ cho phép máy khác trong mạng LAN gửi lệnh in vào máy
  POS). CORS (`CorsSupport.cs`) chỉ allow-list origin đã cấu hình cụ thể
  trong `agent.properties`/mặc định trong code — KHÔNG bao giờ dùng
  `Access-Control-Allow-Origin: *`. Phát hiện lỗ hổng bảo mật ở bất kỳ đâu
  (kể cả code cũ ngoài phạm vi đang sửa) phải báo ngay cho người dùng.
- **CÙNG HTTP contract 100% với bản Java (`fafoshop-print-agent`)** —
  endpoint (`/health`, `/printers`, `/print`), hình dạng JSON request/
  response, mã lỗi HTTP, format `agent.properties` PHẢI khớp y hệt. Frontend
  Angular (`fafoshop/src/app/core/print-agent/`) không phân biệt đang gọi
  bản nào. Đổi hành vi ở đây mà không đổi bản Java tương ứng (hoặc ngược
  lại) sẽ làm 2 bản lệch nhau âm thầm — nếu bắt buộc phải đổi contract, báo
  người dùng để cân nhắc sửa cả 2 bên.
- **KHÔNG thêm NuGet package** trừ khi người dùng yêu cầu rõ ràng — xem lý
  do trong `fafoshop-print-agent-net.csproj` (chỉ dùng BCL + shared
  framework `Microsoft.WindowsDesktop.App` qua `UseWindowsForms=true` để có
  `System.Drawing.Printing`, không cần NuGet). Đây là quyết định có chủ đích
  để agent nhỏ gọn, dễ đóng gói single-file `.exe` (`dotnet publish
  --self-contained -p:PublishSingleFile=true`), cùng tinh thần với bản Java
  tham chiếu.
- **KHÔNG thêm phần mềm in ấn thứ 3** (QZ Tray hay tương đương) — đây chính
  là lý do cả 2 bản agent (Java lẫn .NET) tồn tại, người dùng đã từ chối
  hướng đó.
- **KHÔNG "sửa lại" bản Java (`fafoshop-print-agent/`) khi đang làm việc ở
  đây** — đó là repo riêng, dùng cho nhánh macOS, vẫn được bảo trì song
  song. Muốn đổi gì bên đó, đọc `fafoshop-print-agent/.claude/CLAUDE.md`
  trước.
- Toàn bộ nội dung mới (comment, XML doc `<summary>`, message lỗi, log
  `Console.WriteLine`, tài liệu) dùng tiếng Việt — tên class/namespace/
  method vẫn theo quy ước C# bình thường (tiếng Anh, PascalCase). Khác với
  `fafoshop-api` (log runtime tiếng Anh) — ở đây KHÔNG có ngoại lệ tiếng
  Anh nào, kể cả log.
- Không phát minh thêm nghiệp vụ ngoài phạm vi 2 tài liệu docs nêu trên (vd
  hàng đợi in, retry tự động, quản lý nhiều máy in đồng thời...) khi chưa có
  yêu cầu cụ thể.
- Số đo lề/margin trong `PrinterService.cs` (vd lề an toàn 2mm cho đường
  bill full-bleed) là số đo THỰC TẾ cho máy/Stock/queue in cụ thể hiện tại —
  không sửa theo cảm tính, đo lại bằng in thật nếu đổi máy in/khổ giấy.

## Kiến Trúc (tóm tắt — chi tiết xem `README.md`)

```
Program.cs      — entry point, HttpListener, routing (/health, /printers,
                  /print), đọc AgentConfig, bọc mọi request bằng CORS +
                  catch-all lỗi (HandleRequestAsync).
CorsSupport.cs  — áp header CORS + xử lý preflight OPTIONS (port 1:1 từ
                  CorsSupport.java bản Java).
PrinterService  — System.Drawing.Printing (PrintDocument). PrintImage() —
                  1 method DUY NHẤT dùng chung cho cả bill lẫn nhãn (đơn
                  giản hơn bản Java vì GDI+ đọc đúng khổ giấy mọi Stock,
                  không cần tách 2 method + nhiều cờ né bug JDK). PrintRaw()
                  — text thuần qua RawPrinter, chỉ dùng test nhanh.
RawPrinter.cs   — P/Invoke Win32 WritePrinter (raw mode) cho PrintRaw().
AgentConfig.cs  — đọc agent.properties (key=value, CÙNG format bản Java).
```

## Verify

```powershell
dotnet build            # compile nhanh
dotnet run --no-build    # chạy thử, mặc định http://127.0.0.1:9199
```

Kiểm tra nhanh bằng curl (xem thêm ví dụ đầy đủ trong `README.md`):

```bash
curl http://127.0.0.1:9199/health
curl http://127.0.0.1:9199/printers
```

Đóng gói single-file thật (dùng khi test hành vi publish/deploy):

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false
```

Cần **.NET 8 SDK** trên máy dev (`winget install Microsoft.DotNet.SDK.8`
nếu máy chỉ có runtime). Chạy được là do đây là project Windows-only
(`net8.0-windows`, phụ thuộc GDI+) — không cố chạy/test trên máy không phải
Windows.
