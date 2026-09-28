using System.Drawing;
using System.Drawing.Printing;

namespace FafoshopPrintAgentNet;

/// <summary>
/// Bọc <c>System.Drawing.Printing</c> (GDI+ chuẩn .NET trên Windows) — thay
/// thế <c>PrinterService.java</c> (bản Java tham chiếu, xem
/// <c>fafoshop-print-agent/</c>) cho nhánh Windows.
///
/// <para><b>Vì sao ĐƠN GIẢN HƠN HẲN bản Java</b> (xem chẩn đoán đầy đủ ở
/// docs/pos-in-nhan-san-pham.md mục 6 "Root cause lệch vị trí..."): test thật
/// xác nhận <c>System.Drawing.Printing</c> đọc ĐÚNG khổ giấy driver báo cáo
/// cho CẢ Stock liên tục (bill, 56mm) LẪN Stock die-cut (nhãn, 50x30mm) —
/// KHÔNG dính bug đọc sai (báo nhầm ≈78.7x101.6mm) như cầu nối
/// <c>javax.print</c>/<c>java.awt.print</c> của JDK trên Windows với driver
/// Seagull. Vì vậy KHÔNG cần 2 đường tách biệt + số đo hardcode riêng cho
/// bill như bản Java (<c>printImage()</c> vs <c>printImageWithWidthMm()</c>)
/// — chỉ 1 method dùng chung, mặc định TIN THẲNG khổ giấy driver báo cáo
/// (full-bleed) khi không có override, và nhận override tường minh khi
/// cần (nhãn — FE truyền xuống khớp đúng canvas đã dựng).</para>
///
/// <para><b>Cách vẽ</b>: dùng <c>Graphics.DrawImage</c> với toạ độ tính bằng
/// 1/100 inch (đơn vị mặc định của <see cref="PrintPageEventArgs.Graphics"/>),
/// gốc (0,0) là mép giấy vật lý thật (KHÔNG phải vùng imageable đã trừ lề) —
/// đã xác nhận qua script PowerShell/.NET dùng để chẩn đoán bug bên bản Java
/// (xem docs/pos-in-nhan-san-pham.md mục 6.2 bước 4), kết quả gần đúng ngay
/// từ lần thử đầu, không cần các thủ thuật <c>markCorners</c>/loại trừ
/// attribute như bản Java.</para>
/// </summary>
internal sealed class PrinterService
{
    private const float HundredthsInchPerInch = 100f;
    private const float MmPerInch = 25.4f;

    /// <summary>
    /// Lề an toàn ĐỀU 2 bên cho đường bill (mm) — số đo thật cho queue
    /// `Bill Print`/khổ giấy hiện tại (xem giải thích trong PrintPage). Dùng
    /// chung cho cả tính vùng vẽ lẫn tính chiều cao trang (FitBillPageHeightToImage).
    /// </summary>
    private const float BillSafetyMarginMm = 2f;

    public sealed class PrinterNotFoundException : Exception
    {
        public PrinterNotFoundException(string printerName)
            : base($"Không tìm thấy máy in tên \"{printerName}\" trên máy này.")
        {
        }
    }

    /// <summary>Liệt kê tên tất cả máy in đã cài trên máy này (theo đúng thứ tự OS trả về).</summary>
    public List<string> ListPrinterNames()
    {
        var names = new List<string>();
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// In 1 ảnh (bill hoặc nhãn — cùng 1 method, khác nhau ở tham số).
    /// </summary>
    /// <param name="widthMmOverride">
    /// Khổ in mong muốn (mm). Truyền giá trị cụ thể cho NHÃN (FE đã đo/cấu
    /// hình, khớp đúng canvas đã dựng — xem <c>printableAreaWidthMm</c> ở
    /// <c>POST /print</c>). Để <c>null</c> cho BILL — khi đó tự đọc khổ giấy
    /// MẶC ĐỊNH của queue từ <see cref="PrinterSettings.DefaultPageSettings"/>
    /// (đã xác nhận đọc đúng, xem tóm tắt ở đầu file) và in tràn full-bleed.
    /// </param>
    /// <param name="startXMm">Lệch trái (mm) — mặc định 0 (full-bleed từ mép giấy thật).</param>
    /// <param name="topMarginMm">Lệch trên (mm) — mặc định 0.</param>
    public void PrintImage(string printerName, Image image, float? widthMmOverride, float startXMm, float topMarginMm)
    {
        var printerSettings = new PrinterSettings { PrinterName = printerName };
        if (!printerSettings.IsValid)
        {
            throw new PrinterNotFoundException(printerName);
        }

        using var document = new PrintDocument { PrinterSettings = printerSettings };

        if (widthMmOverride is null)
        {
            FitBillPageHeightToImage(document, image, topMarginMm);
        }

        document.PrintPage += (_, e) =>
        {
            // Khổ giấy MẶC ĐỊNH driver báo cáo cho queue này, đơn vị 1/100 inch
            // — dùng làm fallback khi không có widthMmOverride (đường bill).
            // Đã xác nhận đọc ĐÚNG cho cả Stock liên tục lẫn die-cut (khác bản
            // Java) nên KHÔNG cần số đo hardcode riêng.
            float defaultWidthMm = e.PageBounds.Width / HundredthsInchPerInch * MmPerInch;

            float widthMm;
            float effectiveStartXMm = startXMm;
            if (widthMmOverride is { } explicitWidthMm)
            {
                widthMm = explicitWidthMm; // đường nhãn — FE đã đo/cấu hình, tin thẳng.
            }
            else
            {
                // Đường bill (full-bleed mặc định) — test thật xác nhận full
                // TUYỆT ĐỐI 0mm 2 bên làm mất chữ số cuối bên phải (đầu in
                // không in được sát tuyệt đối mép vật lý) và bên trái không có
                // khoảng đệm nhìn. Chừa lề an toàn nhỏ ĐỀU 2 bên — số đo RIÊNG
                // cho queue `Bill Print`/khổ giấy hiện tại, cần đo lại nếu đổi
                // máy/Stock khác (xem quy trình in-thử-đo-chỉnh đã dùng xuyên
                // suốt docs/pos-in-nhan-san-pham.md).
                widthMm = defaultWidthMm - BillSafetyMarginMm * 2;
                effectiveStartXMm = startXMm + BillSafetyMarginMm;
            }

            float scaleMmPerPx = widthMm / image.Width;
            float heightMm = image.Height * scaleMmPerPx;

            var destRect = new RectangleF(
                MmToHundredthsInch(effectiveStartXMm),
                MmToHundredthsInch(topMarginMm),
                MmToHundredthsInch(widthMm),
                MmToHundredthsInch(heightMm));

            e.Graphics!.DrawImage(image, destRect);
            e.HasMorePages = false;

            Console.WriteLine($"[print-agent] PrintPage pageBoundsHundredthsInch={e.PageBounds} " +
                $"defaultWidthMm={defaultWidthMm:0.###} widthMm={widthMm:0.###} heightMm={heightMm:0.###} " +
                $"effectiveStartXMm={effectiveStartXMm:0.###} topMarginMm={topMarginMm:0.###}");
        };

        Console.WriteLine($"[print-agent] In:Start printer={printerName} image={image.Width}x{image.Height} " +
            $"widthMmOverride={(widthMmOverride is { } w ? w.ToString("0.###") : "null(full-bleed)")}");
        document.Print();
        Console.WriteLine($"[print-agent] In:Success printer={printerName}");
    }

    /// <summary>
    /// In text thuần qua Win32 <c>WritePrinter</c> (raw mode) — CHỈ dùng để
    /// test nhanh bằng curl, KHÔNG dùng cho bill/nhãn thật (không kiểm soát
    /// được layout/font qua các máy in khác nhau) — tương đương
    /// <c>PrinterService.print()</c> của bản Java tham chiếu.
    /// </summary>
    public void PrintRaw(string printerName, string content)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
        Console.WriteLine($"[print-agent] In:Start(RAW) printer={printerName} bytes={bytes.Length}");
        RawPrinter.SendBytesToPrinter(printerName, bytes);
        Console.WriteLine($"[print-agent] In:Success(RAW) printer={printerName}");
    }

    /// <summary>
    /// Đường BILL: đặt chiều cao trang = đúng chiều cao ảnh bill thực tế,
    /// thay vì giữ nguyên chiều cao Stock driver khai báo.
    ///
    /// <para><b>Vì sao bắt buộc</b>: queue bill dùng Stock "Long label" (giấy
    /// cuộn liên tục) khai cao tới <b>1000mm</b> (đo 2026-09-28 qua
    /// <c>Get-PrintConfiguration</c>: <c>MediaSizeHeight=1000000</c> µm).
    /// Trước đây giữ nguyên trang 1000mm, chỉ vẽ ảnh ~150mm lên đầu trang →
    /// driver Seagull (họ TSC) phải rasterize + gửi bitmap cả trang 1000mm
    /// xuống máy in, máy in chờ nhận đủ rồi mới bắt đầu in — ghi nhận thực tế
    /// tại quầy: hơn 10 giây từ lúc bấm thanh toán tới lúc bắt đầu in. Bản
    /// Java tham chiếu đã tránh đúng lỗi này từ trước bằng cách tự tính chiều
    /// cao theo ảnh (xem docs/pos-in-hoa-don.md mục 10 "Layout/canh chỉnh",
    /// bước 7) — bản .NET bị sót khi port.</para>
    ///
    /// <para>Bề rộng giữ nguyên khổ driver báo cáo (không đổi cách canh ngang
    /// full-bleed + lề an toàn ở PrintPage). Chiều cao làm tròn LÊN theo đơn
    /// vị 1/100 inch để không cắt mất dòng cuối. KHÔNG áp dụng cho đường nhãn
    /// (die-cut, khổ cố định theo Stock, driver định vị theo khe hở).</para>
    /// </summary>
    private static void FitBillPageHeightToImage(PrintDocument document, Image image, float topMarginMm)
    {
        PaperSize stockPaper = document.DefaultPageSettings.PaperSize;
        float stockWidthMm = stockPaper.Width / HundredthsInchPerInch * MmPerInch;
        float drawWidthMm = stockWidthMm - BillSafetyMarginMm * 2;
        float imageHeightMm = image.Height * (drawWidthMm / image.Width);
        int pageHeightHundredths = (int)Math.Ceiling(MmToHundredthsInch(topMarginMm + imageHeightMm));

        document.DefaultPageSettings.PaperSize = new PaperSize("fafoshop-bill", stockPaper.Width, pageHeightHundredths);
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        Console.WriteLine($"[print-agent] Bill:FitPage stock={stockPaper.PaperName} {stockPaper.Width}x{stockPaper.Height} " +
            $"-> {stockPaper.Width}x{pageHeightHundredths} (1/100 inch)");
    }

    private static float MmToHundredthsInch(float mm) => mm / MmPerInch * HundredthsInchPerInch;
}
