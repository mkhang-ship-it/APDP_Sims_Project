using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Validation;
using SIMS.Domain.Common;
using SIMS.Domain.Settings;

namespace SIMS.Application.Settings;

public static class SettingKeys
{
    public const string SiteTitle = "site.title";
    public const string SiteDescription = "site.description";
    public const string SiteTheme = "site.theme";
    public const string SitePrimaryColor = "site.primary_color";
    public const string SiteSupportEmail = "site.support_email";

    public const string PaymentsSePayApiKey = "payments.sepay_api_key";
    public const string SystemMaintenance = "system.maintenance";
    public const string SystemMaintenanceMessage = "system.maintenance_message";

    public const string ClubContactEmail = "club.contact_email";
    public const string ClubContactPhone = "club.contact_phone";
    public const string ClubAddress = "club.address";
    public const string ClubInvoiceBank = "club.invoice_bank";
    public const string ClubInvoiceBankAccount = "club.invoice_bank_account";

    public static IReadOnlyList<string> Global { get; } = new[]
    {
        SiteTitle, SiteDescription, SiteTheme, SitePrimaryColor, SiteSupportEmail,
        PaymentsSePayApiKey, SystemMaintenance, SystemMaintenanceMessage,
    };

    public static IReadOnlyList<string> Club { get; } = new[]
    {
        ClubContactEmail, ClubContactPhone, ClubAddress, ClubInvoiceBank, ClubInvoiceBankAccount,
    };
}

public interface ISettingsService
{
    Task<string?> GetAsync(Guid? clubId, string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetManyAsync(
        Guid? clubId,
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default);

    Task<Result<Setting>> SetAsync(
        Guid? clubId,
        string key,
        string? value,
        string actor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Setting>> ListAsync(Guid? clubId, CancellationToken cancellationToken = default);
}

public sealed class SettingsService : ISettingsService
{
    private readonly ISettingRepository _settings;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public SettingsService(ISettingRepository settings, IAuditLog audit, IClock clock)
    {
        _settings = settings;
        _audit = audit;
        _clock = clock;
    }

    public async Task<string?> GetAsync(
        Guid? clubId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var setting = await _settings.FindAsync(clubId, key, cancellationToken).ConfigureAwait(false);

        return setting?.Value;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetManyAsync(
        Guid? clubId,
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var value = await GetAsync(clubId, key, cancellationToken).ConfigureAwait(false);

            if (value is not null)
            {
                result[key] = value;
            }
        }

        return result;
    }

    public async Task<Result<Setting>> SetAsync(
        Guid? clubId,
        string key,
        string? value,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var existing = await _settings.FindAsync(clubId, key, cancellationToken).ConfigureAwait(false);

        Setting setting;
        if (existing is null)
        {
            try
            {
                setting = Setting.Create(clubId, key, value, _clock.UtcNow);
            }
            catch (DomainException ex)
            {
                return Result<Setting>.Fail(new ValidationError(nameof(key), ex.Message));
            }

            await _settings.AddAsync(setting, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing.SetValue(value, _clock.UtcNow);
            await _settings.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
            setting = existing;
        }

        await _audit.WriteAsync(
            new AuditEntry(
                _clock.UtcNow,
                actor,
                "setting.update",
                "setting",
                setting.Id.ToString("D"),
                key),
            cancellationToken).ConfigureAwait(false);

        return Result<Setting>.Ok(setting);
    }

    public async Task<IReadOnlyList<Setting>> ListAsync(
        Guid? clubId,
        CancellationToken cancellationToken = default)
        => (await _settings.ListByScopeAsync(clubId, cancellationToken).ConfigureAwait(false))
            .OrderBy(setting => setting.Key, StringComparer.Ordinal)
            .ToArray();
}
