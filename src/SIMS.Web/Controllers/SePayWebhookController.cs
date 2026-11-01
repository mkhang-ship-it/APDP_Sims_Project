using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Payments;
using SIMS.Application.Settings;

namespace SIMS.Web.Controllers;

public sealed record SePayWebhookRequest(
    [property: JsonPropertyName("transferType")] string? TransferType,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("transferAmount")] decimal? TransferAmount,
    [property: JsonPropertyName("transactionDate")] string? TransactionDate);

/// <summary>Webhook SePay: xác thực Apikey trong cấu hình rồi ghi nhận thanh toán hoá đơn.</summary>
[ApiController]
[Route("api/payments/sepay")]
[AllowAnonymous]
public class SePayWebhookController : ControllerBase
{
    private readonly IPaymentWebhookService _service;
    private readonly ISettingsService _settings;

    public SePayWebhookController(IPaymentWebhookService service, ISettingsService settings)
    {
        _service = service;
        _settings = settings;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(
        [FromBody] SePayWebhookRequest request,
        [FromHeader(Name = "Authorization")] string? authorization,
        CancellationToken cancellationToken)
    {
        var apiKey = await _settings.GetAsync(null, SettingKeys.PaymentsSePayApiKey, cancellationToken);

        if (!PaymentWebhookService.IsConfigured(apiKey))
        {
            return StatusCode(503, new { success = false, message = "SePay chưa được cấu hình." });
        }

        if (!PaymentWebhookService.IsAuthorized(authorization, apiKey))
        {
            return Unauthorized(new { success = false, message = "Sai khoá xác thực webhook." });
        }

        var result = await _service.HandleSePayAsync(
            new SePayPayload(
                request.TransferType,
                request.Code,
                request.TransferAmount ?? 0m,
                request.TransactionDate),
            cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            var message = result.Errors.FirstOrDefault()?.Message ?? "Không xử lý được giao dịch.";
            var status = message.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase) ? 404 : 422;

            return StatusCode(status, new { success = false, message });
        }

        return Ok(new
        {
            success = true,
            invoice = result.Value.Code,
            status = result.Value.Status.ToString(),
        });
    }
}
