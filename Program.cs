using System.Drawing;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FafoshopPrintAgentNet;

/// <summary>
/// Entry point local print agent — BẢN .NET, thay thế
/// <c>fafoshop-print-agent</c> (Java) cho NHÁNH WINDOWS. Xem
/// docs/pos-in-nhan-san-pham.md mục "Root cause lệch vị trí khi in nhãn" để
/// biết đầy đủ lý do chuyển sang .NET (System.Drawing.Printing đọc ĐÚNG khổ
/// giấy Stock die-cut trên driver Seagull, javax.print/java.awt.print đọc
/// SAI) — bản Java VẪN GIỮ LẠI làm tham chiếu (KHÔNG xoá), dùng cho nhánh
/// macOS (CUPS, kiến trúc in ấn khác hẳn, chưa có bằng chứng gặp vấn đề
/// tương tự).
///
/// <para><b>CÙNG HTTP contract 100%</b> với bản Java — frontend Angular
/// (<c>fafoshop/src/app/core/print-agent/</c>) KHÔNG cần đổi bất kỳ dòng nào,
/// chỉ đổi BACKEND thực thi <c>fafoshop-print-agent.jar</c> (Java) sang
/// <c>fafoshop-print-agent-net.exe</c> (.NET) khi deploy cho máy Windows.</para>
///
/// Endpoint: <c>GET /health</c>, <c>GET /printers</c>, <c>POST /print</c> —
/// xem <c>PrintAgentMain.java</c> (fafoshop-print-agent) để đối chiếu đầy đủ
/// hành vi gốc. Bind CHỈ <c>127.0.0.1</c> (không mở ra mạng ngoài).
/// </summary>
internal static class Program
{
    private const string Version = "0.1.0-net";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task Main()
    {
        AgentConfig config = AgentConfig.Load();
        var printerService = new PrinterService();
        var cors = new CorsSupport(config.AllowedOrigins);

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{config.Port}/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException e)
        {
            Console.WriteLine($"[print-agent] Start:Failed Không bind được cổng {config.Port} — {e.Message} " +
                "(có thể đã có tiến trình khác đang lắng nghe cổng này, hoặc thiếu quyền).");
            return;
        }

        if (config.DebugSaveLastPrint)
        {
            Console.WriteLine("[print-agent] Config:DebugSaveLastPrint bật — mỗi ảnh in sẽ được lưu đè vào " +
                "agent-last-print.png để kiểm tra, KHÔNG dùng cờ này ở môi trường thật (lưu lại nội dung hoá đơn khách).");
        }

        Console.WriteLine($"[print-agent] Start:Listening http://127.0.0.1:{config.Port} version={Version} " +
            $"allowedOrigins={string.Join(",", config.AllowedOrigins)}");

        while (true)
        {
            HttpListenerContext context = await listener.GetContextAsync();
            _ = HandleRequestAsync(context, cors, printerService, config);
        }
    }

    /// <summary>Bọc mọi request: xử lý preflight OPTIONS + áp header CORS trước khi vào logic thật, bắt lỗi chung.</summary>
    private static async Task HandleRequestAsync(HttpListenerContext context, CorsSupport cors, PrinterService printerService, AgentConfig config)
    {
        try
        {
            bool allowed = cors.ApplyHeaders(context);

            if (CorsSupport.IsPreflight(context))
            {
                context.Response.StatusCode = allowed ? 204 : 403;
                context.Response.Close();
                return;
            }

            if (!allowed)
            {
                await WriteJsonAsync(context, 403, new { ok = false, error = "Origin không được phép gọi print agent." });
                return;
            }

            string path = context.Request.Url?.AbsolutePath ?? "";
            switch (path)
            {
                case "/health":
                    await HandleHealthAsync(context);
                    break;
                case "/printers":
                    await HandlePrintersAsync(context, printerService);
                    break;
                case "/print":
                    await HandlePrintAsync(context, printerService, config);
                    break;
                default:
                    await WriteJsonAsync(context, 404, new { ok = false, error = "Không tìm thấy endpoint." });
                    break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[print-agent] Handler:Error {e}");
            TryWriteJson(context, 500, new { ok = false, error = $"Lỗi nội bộ agent: {e.Message}" });
        }
    }

    private static async Task HandleHealthAsync(HttpListenerContext context)
    {
        await WriteJsonAsync(context, 200, new { ok = true, version = Version });
    }

    private static async Task HandlePrintersAsync(HttpListenerContext context, PrinterService printerService)
    {
        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, 405, new { ok = false, error = "Chỉ hỗ trợ GET." });
            return;
        }

        List<string> names = printerService.ListPrinterNames();
        await WriteJsonAsync(context, 200, new { ok = true, printers = names });
    }

    /// <summary>
    /// Nhận 1 trong 2 hình dạng body — GIỐNG HỆT bản Java tham chiếu
    /// (xem <c>PrintAgentMain.handlePrint()</c>):
    /// <list type="bullet">
    /// <item><c>{"printerName":"...","imageBase64":"..."}</c> — ảnh PNG bill/nhãn thật.</item>
    /// <item><c>{"printerName":"...","content":"..."}</c> — text thuần, test nhanh.</item>
    /// </list>
    /// Kèm <c>imageBase64</c> có thể có thêm <c>"printProfile":"label"</c> +
    /// BẮT BUỘC <c>printableAreaWidthMm</c> (tuỳ chọn thêm
    /// <c>printableAreaXMm</c>/<c>printableAreaTopMm</c>, mặc định 0) — chọn
    /// đường nhãn thay vì bill. Thiếu/khác <c>"label"</c> = đường bill (full-bleed
    /// theo khổ giấy driver báo cáo, xem <see cref="PrinterService.PrintImage"/>).
    /// </summary>
    private static async Task HandlePrintAsync(HttpListenerContext context, PrinterService printerService, AgentConfig config)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, 405, new { ok = false, error = "Chỉ hỗ trợ POST." });
            return;
        }

        string body = await ReadBodyAsync(context);
        PrintRequestBody? request;
        try
        {
            request = JsonSerializer.Deserialize<PrintRequestBody>(body, JsonOptions);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(context, 400, new { ok = false, error = "Body không phải JSON hợp lệ." });
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.PrinterName))
        {
            await WriteJsonAsync(context, 400, new { ok = false, error = "Thiếu printerName." });
            return;
        }
        if (string.IsNullOrWhiteSpace(request.ImageBase64) && string.IsNullOrEmpty(request.Content))
        {
            await WriteJsonAsync(context, 400, new { ok = false, error = "Thiếu imageBase64 hoặc content." });
            return;
        }

        bool labelProfile = request.PrintProfile == "label";
        float? widthMm = null;
        float xMm = 0f;
        float topMm = 0f;

        if (labelProfile)
        {
            widthMm = request.PrintableAreaWidthMm;
            if (widthMm is null || widthMm <= 0f)
            {
                await WriteJsonAsync(context, 400, new
                {
                    ok = false,
                    error = "printProfile=\"label\" bắt buộc kèm printableAreaWidthMm (mm, khổ nhãn thật) hợp lệ."
                });
                return;
            }
            xMm = request.PrintableAreaXMm ?? 0f;
            topMm = request.PrintableAreaTopMm ?? 0f;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(request.ImageBase64))
            {
                using Image image = DecodeImage(request.ImageBase64!);
                if (config.DebugSaveLastPrint)
                {
                    SaveDebugImage(image);
                }
                printerService.PrintImage(request.PrinterName!, image, widthMm, xMm, topMm);
            }
            else
            {
                printerService.PrintRaw(request.PrinterName!, request.Content!);
            }
            await WriteJsonAsync(context, 200, new { ok = true });
        }
        catch (PrinterService.PrinterNotFoundException e)
        {
            await WriteJsonAsync(context, 404, new { ok = false, error = e.Message });
        }
        catch (ArgumentException e)
        {
            await WriteJsonAsync(context, 400, new { ok = false, error = e.Message });
        }
        catch (Exception e)
        {
            await WriteJsonAsync(context, 502, new { ok = false, error = $"Máy in từ chối lệnh in: {e.Message}" });
        }
    }

    /// <summary>Chấp nhận cả chuỗi base64 thô lẫn dạng data URL <c>data:image/png;base64,...</c>.</summary>
    private static Image DecodeImage(string imageBase64)
    {
        string base64 = imageBase64;
        int commaIndex = base64.IndexOf(',');
        if (base64.StartsWith("data:") && commaIndex >= 0)
        {
            base64 = base64[(commaIndex + 1)..];
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException e)
        {
            throw new ArgumentException($"imageBase64 không hợp lệ: {e.Message}");
        }

        var stream = new MemoryStream(bytes);
        try
        {
            return Image.FromStream(stream);
        }
        catch (Exception e)
        {
            throw new ArgumentException($"imageBase64 không hợp lệ: không đọc được dữ liệu ảnh (không phải PNG/JPEG hợp lệ) — {e.Message}");
        }
    }

    /// <summary>
    /// Lưu đè ảnh vừa nhận vào <c>agent-last-print.png</c> khi
    /// <c>debugSaveLastPrint=true</c> trong <c>agent.properties</c> — best-effort,
    /// lỗi ghi file không được làm hỏng luồng in chính. Y hệt hành vi bản Java.
    /// </summary>
    private static void SaveDebugImage(Image image)
    {
        try
        {
            string path = Path.Combine(Environment.CurrentDirectory, "agent-last-print.png");
            image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"[print-agent] Debug:SavedLastPrint {path}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[print-agent] Debug:SaveFailed {e}");
        }
    }

    private static async Task<string> ReadBodyAsync(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object payload)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    /// <summary>Dùng trong catch-all cuối cùng — response có thể đã hỏng/đóng, KHÔNG để lỗi ghi log làm crash cả agent.</summary>
    private static void TryWriteJson(HttpListenerContext context, int statusCode, object payload)
    {
        try
        {
            WriteJsonAsync(context, statusCode, payload).GetAwaiter().GetResult();
        }
        catch
        {
            // bỏ qua — đã cố hết sức, response phía client coi như mất kết nối.
        }
    }

    private sealed class PrintRequestBody
    {
        [JsonPropertyName("printerName")] public string? PrinterName { get; set; }
        [JsonPropertyName("imageBase64")] public string? ImageBase64 { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("printProfile")] public string? PrintProfile { get; set; }
        [JsonPropertyName("printableAreaWidthMm")] public float? PrintableAreaWidthMm { get; set; }
        [JsonPropertyName("printableAreaXMm")] public float? PrintableAreaXMm { get; set; }
        [JsonPropertyName("printableAreaTopMm")] public float? PrintableAreaTopMm { get; set; }
    }
}
