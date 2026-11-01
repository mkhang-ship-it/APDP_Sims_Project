namespace SIMS.Infrastructure.Persistence;

/// <summary>
/// Chọn nơi lưu dòng cho toàn bộ repository: SQL Server (mặc định) hoặc CSV trên đĩa.
/// Chỉ thay <see cref="IRowStore"/>, mọi repository/ứng dụng giữ nguyên — đây là seam duy nhất
/// quyết định engine lưu trữ.
/// </summary>
public sealed class RowStoreFactory
{
    public const string SqlProvider = "SqlServer";
    public const string CsvProvider = "Csv";

    private readonly string? _connectionString;
    private readonly string? _dataDirectory;

    public RowStoreFactory(string provider, string? connectionString, string? dataDirectory)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? SqlProvider : provider.Trim();
        _connectionString = connectionString;
        _dataDirectory = dataDirectory;

        if (UsesSql && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Storage:Provider=SqlServer nhưng chưa cấu hình connection string 'Storage:ConnectionString'.");
        }

        if (!UsesSql && string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new InvalidOperationException(
                "Storage:Provider=Csv nhưng chưa cấu hình 'Storage:DataDirectory'.");
        }
    }

    public string Provider { get; }

    public bool UsesSql => string.Equals(Provider, SqlProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>Kho dòng cho bảng <paramref name="table"/>; <paramref name="columns"/> chỉ cần cho CSV/khởi tạo.</summary>
    public IRowStore Create(string table, params string[] columns)
    {
        // Chặn ở đây chứ không chỉ trong SqlRowStore: nhánh CSV ghép tên bảng vào đường dẫn
        // nên "../../evil" sẽ ghi ra ngoài thư mục dữ liệu.
        if (!SqlRowStore.IsValidTableName(table))
        {
            throw new ArgumentException(
                $"Tên bảng không hợp lệ: '{table}'. Chỉ cho phép chữ, số và gạch dưới, bắt đầu bằng chữ cái.",
                nameof(table));
        }

        return UsesSql
            ? new SqlRowStore(_connectionString!, table, columns)
            : new CsvFileStore(Path.Combine(_dataDirectory!, table + ".csv"), columns);
    }

    /// <summary>Chuẩn bị hạ tầng một lần (schema SQL hoặc thư mục dữ liệu CSV).</summary>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        if (UsesSql)
        {
            await SqlRowStore.EnsureDatabaseAsync(_connectionString!, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Directory.CreateDirectory(_dataDirectory!);
        }
    }
}
