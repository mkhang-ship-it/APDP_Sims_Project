using System.Text.RegularExpressions;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Finance;
using SIMS.Application.Settings;
using SIMS.Application.Validation;
using SIMS.Domain.Finance;

namespace SIMS.Application.Payments;

public sealed record SePayPayload(
    string? TransferType,
    string? Code,
    decimal TransferAmount,
    string? TransactionDate);

public interface IPaymentWebhookService
{
    /// <summary>
    /// Xử lý giao dịch chuyển khoản SePay: tìm hoá đơn theo mã, ghi nhận thanh toán (tối đa số còn lại).
    /// </summary>
    Task<Result<Invoice>> HandleSePayAsync(
        SePayPayload payload,
        CancellationToken cancellationToken = default);
}

public sealed class PaymentWebhookService : IPaymentWebhookService
{
    private static readonly Regex CompactCode = new(@"^INV(\d{4})(\d{6})$", RegexOptions.Compiled);

    private readonly IInvoiceRepository _invoices;
    private readonly IInvoiceService _invoiceService;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;

    public PaymentWebhookService(
        IInvoiceRepository invoices,
        IInvoiceService invoiceService,
        ISettingsService settings,
        IClock clock)
    {
        _invoices = invoices;
        _invoiceService = invoiceService;
        _settings = settings;
        _clock = clock;
    }

    public async Task<Result<Invoice>> HandleSePayAsync(
        SePayPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(payload.TransferType, "in", StringComparison.OrdinalIgnoreCase))
        {
            return Result<Invoice>.Fail(new ValidationError(string.Empty, "Bỏ qua giao dịch không phải tiền vào."));
        }

        var code = NormalizeCode(payload.Code);
        if (code.Length == 0)
        {
            return Result<Invoice>.Fail(new ValidationError(string.Empty, "Thiếu mã hoá đơn trong giao dịch."));
        }

        var invoice = await _invoices.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result<Invoice>.Fail(new ValidationError(string.Empty, $"Không tìm thấy hoá đơn {code}."));
        }

        var detail = await _invoiceService.GetAsync(invoice.Id, cancellationToken).ConfigureAwait(false);
        if (detail is not null && detail.Remaining <= 0m)
        {
            return Result<Invoice>.Ok(invoice);
        }

        var remaining = detail?.Remaining ?? invoice.TotalAmount;
        var amount = Math.Min(payload.TransferAmount, remaining);
        if (amount <= 0m)
        {
            return Result<Invoice>.Fail(new ValidationError(string.Empty, "Số tiền giao dịch không hợp lệ."));
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var paymentDate = ParseDate(payload.TransactionDate) ?? today;
        var createdOn = DateOnly.FromDateTime(invoice.CreatedAtUtc.UtcDateTime);

        if (paymentDate < createdOn)
        {
            paymentDate = today;
        }

        var result = await _invoiceService.RecordPaymentAsync(
            invoice.Id,
            amount,
            paymentDate,
            PaymentMethod.Sepay,
            $"SePay: {code}",
            "sepay-webhook",
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return Result<Invoice>.Fail(result.Errors);
        }

        var updated = await _invoices.FindByIdAsync(invoice.Id, cancellationToken).ConfigureAwait(false);

        return updated is null
            ? Result<Invoice>.Fail(new ValidationError(string.Empty, "Không tải lại được hoá đơn."))
            : Result<Invoice>.Ok(updated);
    }

    public static bool IsConfigured(string? apiKey)
        => !string.IsNullOrWhiteSpace(apiKey) && apiKey.Trim().Length >= 8;

    public static bool IsAuthorized(string? authorizationHeader, string? configuredKey)
    {
        if (!IsConfigured(configuredKey))
        {
            return false;
        }

        var provided = (authorizationHeader ?? string.Empty).Trim();
        const string prefix = "Apikey ";
        if (provided.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            provided = provided[prefix.Length..].Trim();
        }

        if (provided.Length == 0)
        {
            return false;
        }

        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(configuredKey!.Trim());
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            providedBytes, expectedBytes);
    }

    private static string NormalizeCode(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        var match = CompactCode.Match(value);
        if (match.Success)
        {
            return $"INV-{match.Groups[1].Value}-{match.Groups[2].Value}";
        }

        return value.ToUpperInvariant();
    }

    private static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            raw,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? DateOnly.FromDateTime(parsed.UtcDateTime)
            : null;
    }
}
