using System.Collections.Concurrent;

namespace SIMS.Infrastructure.Persistence;

/// <summary>
/// Kho CSV dùng chung cho mọi bảng dữ liệu:
/// - khoá theo từng file (SemaphoreSlim chia sẻ giữa các instance),
/// - ghi nguyên tử: ghi file tạm cùng thư mục rồi File.Move(overwrite),
/// - luôn ghi dòng header đầu tiên.
/// </summary>
public sealed class CsvFileStore : IRowStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string[] _header;
    private readonly SemaphoreSlim _lock;

    public CsvFileStore(string filePath, params string[] header)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Đường dẫn CSV không hợp lệ.", nameof(filePath));
        }

        FilePath = Path.GetFullPath(filePath);
        _header = header;
        _lock = FileLocks.GetOrAdd(FilePath, _ => new SemaphoreSlim(1, 1));
    }

    public string FilePath { get; }

    public string Name => FilePath;

    public IReadOnlyList<string> Columns => _header;

    public async Task<IReadOnlyList<string[]>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
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
            EnsureDirectory();
            var isNewFile = !File.Exists(FilePath);

            await using var stream = new FileStream(
                FilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream);

            if (isNewFile)
            {
                await writer.WriteLineAsync(CsvFormat.Join(_header).AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            await writer.WriteLineAsync(CsvFormat.Join(cells).AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Đọc–sửa–ghi trong cùng một khoá file. Hàm mutate trả về null nghĩa là không thay đổi.
    /// Trả về true nếu đã ghi file.
    /// </summary>
    public async Task<bool> MutateAsync(
        Func<IReadOnlyList<string[]>, IReadOnlyList<string[]>?> mutate,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rows = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            var updated = mutate(rows);

            if (updated is null)
            {
                return false;
            }

            await WriteAllCoreAsync(updated, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<string[]>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return Array.Empty<string[]>();
        }

        var lines = await File.ReadAllLinesAsync(FilePath, cancellationToken).ConfigureAwait(false);
        var rows = new List<string[]>(Math.Max(0, lines.Length - 1));

        // Dòng đầu là header — bỏ qua.
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var cells = CsvFormat.Split(lines[i]);

            // Tương thích ngược: bảng thêm cột mới thì dòng cũ thiếu ô được đệm rỗng.
            if (cells.Length < _header.Length)
            {
                var padded = new string[_header.Length];
                Array.Copy(cells, padded, cells.Length);
                for (var j = cells.Length; j < padded.Length; j++)
                {
                    padded[j] = string.Empty;
                }

                cells = padded;
            }

            rows.Add(cells);
        }

        return rows;
    }

    private async Task WriteAllCoreAsync(IReadOnlyList<string[]> rows, CancellationToken cancellationToken)
    {
        EnsureDirectory();
        var tempPath = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 8192,
                useAsync: true))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteLineAsync(CsvFormat.Join(_header).AsMemory(), cancellationToken).ConfigureAwait(false);

                foreach (var row in rows)
                {
                    await writer.WriteLineAsync(CsvFormat.Join(row).AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            // Ghi nguyên tử: thay thế file cũ bằng file tạm đã flush.
            File.Move(tempPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
