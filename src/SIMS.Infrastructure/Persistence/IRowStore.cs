using System.Collections.Concurrent;

namespace SIMS.Infrastructure.Persistence;

/// <summary>
/// Kho dòng dùng chung cho mọi repository (LSP): CSV là một cài đặt, bộ nhớ là cài đặt khác.
/// Bất biến bảo toàn: mỗi bảng giữ đúng thứ tự cột, ghi nguyên tử, khoá theo bảng.
/// </summary>
public interface IRowStore
{
    /// <summary>Tên bảng (tên file CSV / tên bảng SQL, đều là duy nhất).</summary>
    string Name { get; }

    /// <summary>Danh sách cột theo đúng thứ tự — chỉ dùng khi import/khởi tạo.</summary>
    IReadOnlyList<string> Columns { get; }

    Task<IReadOnlyList<string[]>> ReadAllAsync(CancellationToken cancellationToken = default);

    Task AppendAsync(IReadOnlyList<string> cells, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc–sửa–ghi trong cùng một khoá. Hàm mutate trả null nghĩa là không thay đổi.
    /// Trả về true nếu đã ghi.
    /// </summary>
    Task<bool> MutateAsync(
        Func<IReadOnlyList<string[]>, IReadOnlyList<string[]>?> mutate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Kho dòng trong bộ nhớ, dùng cho test và cho chế độ không có file.
/// Cùng ngữ nghĩa với CsvFileStore: khoá theo tên bảng, đệm ô rỗng cho dòng ngắn.
/// </summary>
public sealed class InMemoryRowStore : IRowStore
{
    private static readonly ConcurrentDictionary<(Type Store, string Name), SemaphoreSlim> Locks = new();

    private readonly List<string[]> _rows = new();
    private readonly SemaphoreSlim _lock;

    public InMemoryRowStore(string name, params string[] columns)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tên bảng không hợp lệ.", nameof(name));
        }

        Name = name;
        Columns = columns;
        _lock = Locks.GetOrAdd((typeof(InMemoryRowStore), name), _ => new SemaphoreSlim(1, 1));
    }

    public string Name { get; }

    public IReadOnlyList<string> Columns { get; }

    public async Task<IReadOnlyList<string[]>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _rows.Select(Pad).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AppendAsync(IReadOnlyList<string> cells, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _rows.Add(Pad(cells.ToArray()));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> MutateAsync(
        Func<IReadOnlyList<string[]>, IReadOnlyList<string[]>?> mutate,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = _rows.Select(Pad).ToList();
            var updated = mutate(snapshot);

            if (updated is null)
            {
                return false;
            }

            _rows.Clear();
            _rows.AddRange(updated.Select(row => Pad(row.ToArray())));
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private string[] Pad(string[] cells)
    {
        if (cells.Length >= Columns.Count)
        {
            return cells;
        }

        var padded = new string[Columns.Count];
        Array.Copy(cells, padded, cells.Length);
        for (var i = cells.Length; i < padded.Length; i++)
        {
            padded[i] = string.Empty;
        }

        return padded;
    }
}
