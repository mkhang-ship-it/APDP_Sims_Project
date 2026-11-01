using SIMS.Domain.Common;

namespace SIMS.Infrastructure.Persistence;

/// <summary>
/// Repository dùng chung (LSP): mọi bảng đều đọc/ghi qua một <see cref="IRowStore"/>
/// (CSV trên đĩa, bộ nhớ, hoặc SQL Server) nên chiến lược lưu trữ thay được mà không đổi
/// logic nghiệp vụ. Hợp đồng: ghi nguyên tử, khoá theo bảng, giữ đúng thứ tự cột.
/// </summary>
public abstract class CsvRepository<T> where T : Entity
{
    private readonly IRowStore _store;

    /// <summary>Dựng repository trên file CSV (tương thích với cài đặt cũ).</summary>
    protected CsvRepository(string filePath, params string[] header)
        : this(new CsvFileStore(filePath, header))
    {
    }

    /// <summary>Dựng repository trên một kho dòng bất kỳ (bộ nhớ, SQL, file khác).</summary>
    protected CsvRepository(IRowStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    protected IRowStore Store => _store;

    public virtual async Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Store.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<T>(rows.Count);

        foreach (var row in rows)
        {
            var entity = Map(row);
            if (entity is not null)
            {
                items.Add(entity);
            }
        }

        return items;
    }

    public virtual async Task<T?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.FirstOrDefault(item => item.Id == id);
    }

    public virtual Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Store.AppendAsync(ToCells(entity), cancellationToken);
    }

    public virtual async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var updated = await Store.MutateAsync(rows =>
        {
            var result = new List<string[]>(rows.Count);
            var changed = false;

            foreach (var row in rows)
            {
                if (TryGetId(row, out var rowId) && rowId == entity.Id)
                {
                    result.Add(ToCells(entity));
                    changed = true;
                }
                else
                {
                    result.Add(row);
                }
            }

            return changed ? result : null;
        }, cancellationToken).ConfigureAwait(false);

        if (!updated)
        {
            throw new DomainException($"Không tìm thấy bản ghi {entity.Id} để cập nhật.");
        }
    }

    public virtual Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Store.MutateAsync(rows =>
        {
            var result = rows
                .Where(row => !(TryGetId(row, out var rowId) && rowId == id))
                .ToList();

            return result.Count == rows.Count ? null : result;
        }, cancellationToken);

    protected abstract T? Map(string[] cells);

    protected abstract string[] ToCells(T entity);

    protected static bool TryGetId(string[] cells, out Guid id)
        => Guid.TryParse(cells.Length > 0 ? cells[0] : null, out id);
}
