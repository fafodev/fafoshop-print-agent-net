# fafoshop-print-agent-net

Local print agent chạy trên máy POS **Windows** — bản **.NET** (C#,
`System.Drawing.Printing`/GDI+), thay thế
[`fafoshop-print-agent`](https://github.com/fafodev/fafoshop-print-agent)
(Java, `javax.print`) cho nhánh Windows. Nhận lệnh in từ frontend `fafoshop`
(Angular) qua HTTP nội bộ (`http://127.0.0.1:9199`) và in thẳng ra máy in,
KHÔNG qua popup in của trình duyệt, KHÔNG phụ thuộc phần mềm in ấn thứ 3.

**CÙNG HTTP contract 100%** với bản Java — frontend Angular
(`fafoshop/src/app/core/print-agent/`) KHÔNG cần đổi bất kỳ dòng nào, chỉ
đổi BACKEND thực thi (`.jar` → `.exe`) khi deploy cho máy Windows.

## Vì sao có bản .NET riêng (không dùng chung bản Java)?

Xem đầy đủ chẩn đoán tại
[`docs/pos-in-nhan-san-pham.md`](../docs/pos-in-nhan-san-pham.md) mục "Root
cause lệch vị trí khi in nhãn" (ở gốc workspace `fafoshop-workspace`). Tóm
tắt: test thật trên máy Xprinter XP-365B (driver Seagull) xác nhận cầu nối
in ấn của JDK trên Windows (`javax.print` VÀ `java.awt.print`) đọc **SAI**
khổ giấy cho Stock die-cut custom (báo nhầm ≈78.7x101.6mm dù thật 50x30mm),
và bị driver Seagull xử lý không đáng tin (callback gọi lại nhiều lần,
`PageFormat` bị bỏ qua âm thầm). `.NET`/GDI+ (`System.Drawing.Printing`) đọc
**ĐÚNG** khổ giấy cho CẢ Stock liên tục (bill) LẪN Stock die-cut (nhãn) trên
CÙNG driver — đã xác nhận qua nhiều lần in thật.

**Bản Java (`fafoshop-print-agent`) VẪN GIỮ LẠI làm tham chiếu** — dùng cho
nhánh **macOS** (CUPS, kiến trúc in ấn khác hẳn Windows GDI, chưa có bằng
chứng gặp vấn đề tương tự). Không xoá, không ngừng bảo trì.

## Vì sao code ĐƠN GIẢN HƠN HẲN bản Java

Vì `.NET` đọc đúng khổ giấy cho mọi loại Stock, `PrinterService.PrintImage()`
chỉ có **1 method dùng chung** cho cả bill lẫn nhãn (khác hẳn bản Java cần
tách `printImage()`/`printImageWithWidthMm()` + nhiều cờ `useMarkCorners`/
`addMediaAttribute`/`setResolutionAttribute` để né các bug riêng của JDK):

- **Bill** (không có `printableAreaWidthMm` từ FE) → full-bleed: tự đọc khổ
  giấy mặc định driver báo cáo, trừ lề an toàn nhỏ 2mm mỗi bên (số đo RIÊNG
  cho queue `Bill Print` hiện tại — đo lại nếu đổi máy/Stock khác, xem
  comment trong `PrinterService.cs`).
- **Nhãn** (`printProfile:"label"`, kèm `printableAreaWidthMm`) → dùng ĐÚNG
  số FE truyền (khớp canvas đã dựng), `x=0/top=0` mặc định — đã test đúng
  ngay lần đầu, không cần canh chỉnh thêm.

## Build & chạy lúc dev

Cần **.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8` nếu máy chỉ có
runtime).

```powershell
dotnet build
dotnet run --no-build
```

Mặc định lắng nghe `127.0.0.1:9199`, cho phép origin `http://localhost:4200`.
Muốn đổi port/origin mà không build lại: tạo file `agent.properties` CÙNG
thư mục với file `.exe` — **CÙNG format** với bản Java, xem
[`fafoshop-print-agent/README.md`](https://github.com/fafodev/fafoshop-print-agent/blob/main/README.md)
mục cấu hình:

```properties
port=9199
allowedOrigins=http://localhost:4200,https://pos.fafoshop.vn
debugSaveLastPrint=false
```

## Đóng gói chạy thật (single-file .exe, không cần cài .NET trên máy khách)

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false
```

Kết quả: `bin/Release/net8.0-windows/win-x64/publish/fafoshop-print-agent-net.exe`
— copy nguyên file này sang máy POS, kèm `agent.properties` nếu cần đổi cấu
hình mặc định. Tự khởi động cùng lúc đăng nhập máy: thêm shortcut vào thư
mục Startup (`shell:startup`) — CHƯA làm thành script tự động, làm khi có
nhu cầu triển khai thật.

## Kiểm tra nhanh (không cần frontend)

```bash
curl http://127.0.0.1:9199/health
curl http://127.0.0.1:9199/printers
curl -X POST http://127.0.0.1:9199/print \
  -H "Content-Type: application/json" \
  -d '{"printerName":"TEN_MAY_IN","content":"Xin chao\nTest in thu\n"}'
```

## Kiến trúc (tóm tắt)

```
Program.cs      — entry point, HttpListener, routing (/health, /printers, /print),
                  đọc AgentConfig.
CorsSupport.cs  — áp header CORS + xử lý preflight OPTIONS (port 1:1 từ bản Java).
PrinterService  — System.Drawing.Printing (PrintDocument), 1 method PrintImage()
                  dùng chung bill/nhãn + PrintRaw() (test nhanh, qua RawPrinter).
RawPrinter.cs   — P/Invoke Win32 WritePrinter (raw mode) cho PrintRaw().
AgentConfig.cs  — đọc agent.properties (key=value, CÙNG format bản Java).
```

Không dùng NuGet package nào ngoài BCL/shared framework
(`Microsoft.WindowsDesktop.App`, bật qua `<UseWindowsForms>true</UseWindowsForms>`
trong `.csproj` để có `System.Drawing.Printing`) — cùng tinh thần "không
thêm dependency" của bản Java tham chiếu.
