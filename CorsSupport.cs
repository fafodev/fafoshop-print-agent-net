using System.Net;

namespace FafoshopPrintAgentNet;

/// <summary>
/// Xử lý CORS cho HTTP server nội bộ — CHỈ allow đúng origin frontend đã cấu
/// hình (không phải "*"), để trang web lạ không tự gọi được vào agent (in
/// bậy/spam máy in). Port 1:1 hành vi từ <c>CorsSupport.java</c> (bản Java
/// tham chiếu, <c>fafoshop-print-agent</c>) — xem docs/pos-in-hoa-don.md để
/// biết đầy đủ lý do (preflight OPTIONS chặn request thật trước khi tới
/// handler, header Private Network Access cho Chrome khi gọi vào
/// 127.0.0.1).
/// </summary>
internal sealed class CorsSupport
{
    private readonly List<string> _allowedOrigins;

    public CorsSupport(List<string> allowedOrigins)
    {
        _allowedOrigins = allowedOrigins;
    }

    /// <summary>
    /// Áp header CORS phù hợp nếu origin của request nằm trong allow-list.
    /// Trả về true nếu origin hợp lệ (hoặc request không có Origin — vd gọi
    /// trực tiếp bằng curl/Postman lúc test).
    /// </summary>
    public bool ApplyHeaders(HttpListenerContext context)
    {
        string? origin = context.Request.Headers["Origin"];

        context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        context.Response.Headers.Add("Access-Control-Allow-Private-Network", "true");

        if (origin == null)
        {
            return true;
        }

        if (_allowedOrigins.Contains(origin))
        {
            context.Response.Headers.Add("Access-Control-Allow-Origin", origin);
            return true;
        }

        return false;
    }

    /// <summary>true nếu là request OPTIONS (preflight) — gọi trước khi xử lý handler thật.</summary>
    public static bool IsPreflight(HttpListenerContext context) =>
        string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase);
}
