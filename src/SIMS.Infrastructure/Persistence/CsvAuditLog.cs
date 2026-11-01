using System.Globalization;
using SIMS.Application.Abstractions;

namespace SIMS.Infrastructure.Persistence;

/// <summary>audit.csv — append-only, ghi mọi thao tác ghi dữ liệu và đăng nhập; đọc cho trang nhật ký.</summary>
public sealed class CsvAuditLog : IAuditLog, IAuditLogReader
{
    private readonly IRowStore _store;

    public CsvAuditLog(string filePath)
        : this(new CsvFileStore(
            filePath,
            "timestamp_utc",
            "actor",
            "action",
            "entity",
            "entity_id",
            "details"))
    {
    }

    public CsvAuditLog(IRowStore store)
    {
        _store = store;
    }

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var cells = new[]
        {
            entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            entry.Actor,
            entry.Action,
            entry.Entity,
            entry.EntityId,
            entry.Details,
        };

        return _store.AppendAsync(cells, cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> ReadAsync(
        int maxEntries = 200,
        CancellationToken cancellationToken = default)
    {
        var rows = await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<AuditEntry>(rows.Count);

        foreach (var row in rows)
        {
            if (row.Length != 6 ||
                !DateTimeOffset.TryParse(
                    row[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            {
                continue;
            }

            entries.Add(new AuditEntry(timestamp, row[1], row[2], row[3], row[4], row[5]));
        }

        return maxEntries <= 0 || entries.Count <= maxEntries
            ? entries
            : entries.Skip(entries.Count - maxEntries).ToArray();
    }
}
