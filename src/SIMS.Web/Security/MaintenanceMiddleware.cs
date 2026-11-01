using SIMS.Application.Settings;

namespace SIMS.Web.Security;

/// <summary>
/// Chế độ bảo trì: khi `system.maintenance` = on, khách nhận 503 (trang thông báo);
/// Admin, trang đăng nhập, health check, static files và webhook thanh toán vẫn hoạt động.
/// </summary>
public sealed class MaintenanceMiddleware
{
    private readonly RequestDelegate _next;

    public MaintenanceMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ISettingsService settings)
    {
        if (IsExempt(context.Request.Path) || context.User.IsInRole("Admin"))
        {
            await _next(context);
            return;
        }

        var flag = await settings.GetAsync(null, SettingKeys.SystemMaintenance, context.RequestAborted);
        if (!string.Equals(flag, "on", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var message = await settings.GetAsync(
            null, SettingKeys.SystemMaintenanceMessage, context.RequestAborted);

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        await context.Response.WriteAsync(RenderPage(message));
    }

    private static bool IsExempt(PathString path)
    {
        var value = path.Value ?? "/";

        if (Path.HasExtension(value))
        {
            return true;
        }

        return value.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("/api/payments", StringComparison.OrdinalIgnoreCase);
    }

    private static string RenderPage(string? message)
    {
        var text = string.IsNullOrWhiteSpace(message)
            ? "Hệ thống đang bảo trì. Vui lòng quay lại sau."
            : message;

        return $$"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Đang bảo trì</title>
                <style>
                    body { font-family: "Segoe UI", Arial, sans-serif; display: flex; min-height: 100vh;
                           align-items: center; justify-content: center; background: #0f172a; color: #e2e8f0; margin: 0; }
                    .card { max-width: 520px; padding: 32px; border: 1px solid #334155; border-radius: 16px;
                            background: #111c33; text-align: center; }
                    h1 { font-size: 24px; margin: 0 0 12px; }
                    p { line-height: 1.6; color: #cbd5e1; }
                    a { color: #7dd3fc; }
                </style>
            </head>
            <body>
                <div class="card">
                    <h1>Hệ thống đang bảo trì</h1>
                    <p>{{Escape(text)}}</p>
                    <p><a href="/Account/Login">Đăng nhập quản trị</a></p>
                </div>
            </body>
            </html>
            """;
    }

    private static string Escape(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
