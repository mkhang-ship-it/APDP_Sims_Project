using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace SIMS.Infrastructure.Persistence;

/// <summary>
/// Kho dòng trên SQL Server (engine chính). Mỗi bảng là một bảng thật
/// <c>[sims].[Name]</c> với cột <c>Id INT IDENTITY</c> (khoá kỹ thuật),
/// <c>RowNo INT</c> (thứ tự bản ghi), <c>Ord INT</c> (thứ tự cột) và
/// <c>Value NVARCHAR(MAX)</c> — mô hình ô key/value để không phải viết 38 schema riêng,
/// đổi lại mỗi thao tác ghi là một transaction đọc–sửa–ghi.
/// Ghi nguyên tử bằng <c>SERIALIZABLE</c> + <c>sp_getapplock</c> theo bảng, giữ đúng
/// ngữ nghĩa read-modify-write của bản CSV.
/// </summary>
public sealed class SqlRowStore : IRowStore
{
    internal const string Schema = "sims";

    /// <summary>
    /// Tên bảng hợp lệ: chữ/số/gạch dưới, bắt đầu bằng chữ cái, tối đa 100 ký tự.
    /// Chặn cả SQL injection (tên bảng được ghép vào DDL/DML) lẫn thoát thư mục ở chế độ CSV
    /// (<c>../../x</c>). Không dùng tham số hoá được cho định danh trong SQL Server.
    /// </summary>
    private static readonly Regex TableNamePattern = new("^[A-Za-z][A-Za-z0-9_]{0,99}$", RegexOptions.Compiled);

    private static readonly SemaphoreSlim InitLock = new(1, 1);

    /// <summary>Connection string đã tạo schema — khoá theo chuỗi chứ không phải cờ toàn cục.</summary>
    private static readonly HashSet<string> InitializedConnections = new(StringComparer.Ordinal);

    private readonly string _connectionString;

    public SqlRowStore(string connectionString, string name, params string[] columns)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Chuỗi kết nối SQL Server không hợp lệ.", nameof(connectionString));
        }

        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(columns);

        if (!IsValidTableName(name))
        {
            throw new ArgumentException(
                $"Tên bảng không hợp lệ: '{name}'. Chỉ cho phép chữ, số và gạch dưới, bắt đầu bằng chữ cái.",
                nameof(name));
        }

        _connectionString = connectionString;
        Name = name;
        Columns = columns;
    }

    public string Name { get; }

    public IReadOnlyList<string> Columns { get; }

    /// <summary>Kiểm tên bảng trước khi nó đi vào SQL hoặc đường dẫn file.</summary>
    public static bool IsValidTableName(string? name)
        => !string.IsNullOrEmpty(name) && TableNamePattern.IsMatch(name);

    /// <summary>Tạo schema nếu chưa có (idempotent, mỗi connection string một lần mỗi process).</summary>
    public static async Task EnsureDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await InitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (InitializedConnections.Contains(connectionString))
            {
                return;
            }

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF SCHEMA_ID('{Schema}') IS NULL EXEC('CREATE SCHEMA [{Schema}]');";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            InitializedConnections.Add(connectionString);
        }
        finally
        {
            InitLock.Release();
        }
    }

    public async Task<IReadOnlyList<string[]>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await ReadCoreAsync(connection, transaction: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendAsync(IReadOnlyList<string> cells, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cells);
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        // Số thứ tự bản ghi phải nằm trong cùng transaction với INSERT, nếu không hai lần
        // append đồng thời sẽ nhận cùng RowNo và trộn ô của hai bản ghi vào nhau.
        await AcquireTableLockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        var rowNo = await NextRowNoAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await InsertAsync(connection, transaction, Normalize(cells), rowNo, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> NextRowNoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT ISNULL(MAX([RowNo]), 0) + 1 FROM [{Schema}].[{Table}]";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<bool> MutateAsync(
        Func<IReadOnlyList<string[]>, IReadOnlyList<string[]>?> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        await AcquireTableLockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        var snapshot = await ReadCoreAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var updated = mutate(snapshot);

        if (updated is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"DELETE FROM [{Schema}].[{Table}]";
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var r = 0; r < updated.Count; r++)
        {
            await InsertAsync(connection, transaction, Normalize(updated[r] ?? Array.Empty<string>()), r, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task AcquireTableLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        // Khoá applock theo bảng: tuần tự hoá read-modify-write giữa các tiến trình/kết nối.
        await using var lockCommand = connection.CreateCommand();
        lockCommand.Transaction = transaction;
        lockCommand.CommandText =
            "DECLARE @r INT; EXEC @r = sp_getapplock @Resource=@res, @LockMode='Exclusive', " +
            "@LockOwner='Transaction', @LockTimeout=15000; IF @r < 0 THROW 50000, 'Không khoá được bảng.', 1;";
        lockCommand.Parameters.AddWithValue("@res", "sims:" + Name);
        await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string Table => Name;

    private string[] Normalize(IReadOnlyList<string> cells)
    {
        if (cells.Count >= Columns.Count)
        {
            return cells.ToArray();
        }

        var padded = new string[Columns.Count];
        for (var i = 0; i < padded.Length; i++)
        {
            padded[i] = i < cells.Count ? cells[i] ?? string.Empty : string.Empty;
        }

        return padded;
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseAsync(_connectionString, cancellationToken).ConfigureAwait(false);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // DDL chạy dưới applock riêng theo bảng: hai request đồng thời cùng thấy
        // "chưa có index" rồi cùng CREATE INDEX sẽ nổ "index already exists"
        // (đã bắt được bằng test append song song). applock tuần tự hoá phần DDL;
        // TRY/CATCH vẫn giữ để chịu được DDL do process khác chen vào.
        command.CommandText =
            "DECLARE @r INT; EXEC @r = sp_getapplock @Resource=@ddl, @LockMode='Exclusive', " +
            "@LockOwner='Session', @LockTimeout=15000; " +
            "IF @r < 0 THROW 50000, 'Không khoá được schema bảng.', 1; " +
            $"IF OBJECT_ID('[{Schema}].[{Table}]', 'U') IS NULL " +
            $"CREATE TABLE [{Schema}].[{Table}] (" +
            " [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, " +
            " [RowNo] INT NOT NULL, " +
            " [Ord] INT NOT NULL, " +
            " [Value] NVARCHAR(MAX) NOT NULL); " +
            $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_{Table}_RowOrd' " +
            $"AND object_id = OBJECT_ID('[{Schema}].[{Table}]')) " +
            "BEGIN TRY " +
            $"CREATE UNIQUE INDEX [IX_{Table}_RowOrd] ON [{Schema}].[{Table}] ([RowNo], [Ord]); " +
            "END TRY BEGIN CATCH IF ERROR_NUMBER() <> 1913 THROW; END CATCH; " +
            "EXEC sp_releaseapplock @Resource=@ddl, @LockOwner='Session';";

        command.Parameters.AddWithValue("@ddl", "sims:ddl:" + Table);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string[]>> ReadCoreAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var grouped = new Dictionary<int, Dictionary<int, string>>();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT [RowNo], [Ord], [Value] FROM [{Schema}].[{Table}] ORDER BY [RowNo], [Ord]";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var rowNo = reader.GetInt32(0);
            var ord = reader.GetInt32(1);
            var value = reader.GetString(2);

            if (!grouped.TryGetValue(rowNo, out var byOrd))
            {
                byOrd = new Dictionary<int, string>();
                grouped[rowNo] = byOrd;
            }

            byOrd[ord] = value;
        }

        var rows = new List<string[]>(grouped.Count);
        foreach (var rowNo in grouped.Keys.OrderBy(x => x))
        {
            var byOrd = grouped[rowNo];
            var cells = new string[Math.Max(Columns.Count, byOrd.Count)];
            foreach (var pair in byOrd)
            {
                if (pair.Key >= 0 && pair.Key < cells.Length)
                {
                    cells[pair.Key] = pair.Value;
                }
            }

            for (var i = 0; i < cells.Length; i++)
            {
                cells[i] ??= string.Empty;
            }

            rows.Add(cells);
        }

        return rows;
    }

    private async Task InsertAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string[] cells,
        int rowNo,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO [{Schema}].[{Table}] ([RowNo], [Ord], [Value]) VALUES (@row, @ord, @val);";

        command.Parameters.Add("@row", System.Data.SqlDbType.Int).Value = rowNo;
        var ord = command.Parameters.Add("@ord", System.Data.SqlDbType.Int);
        var val = command.Parameters.Add("@val", System.Data.SqlDbType.NVarChar, -1);

        // Mỗi ô một dòng: (RowNo, Ord) xác định vị trí ô trong bản ghi.
        // Không gộp nhiều dòng VALUES: với NVARCHAR(MAX) SqlClient gửi nhiều result set
        // và chỉ dòng đầu được ghi (bug đã trả giá — xem porting-notes 1c).
        for (var i = 0; i < cells.Length; i++)
        {
            ord.Value = i;
            val.Value = cells[i] ?? string.Empty;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
