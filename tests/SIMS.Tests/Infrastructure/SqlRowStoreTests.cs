using Microsoft.Data.SqlClient;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

/// <summary>
/// Kiểm kho dòng SQL Server thật. Bỏ qua (skip) khi máy không có SQL Server cục bộ —
/// test vẫn xanh trên CI, và chạy thật ở máy có engine (đúng môi trường phát hành).
/// </summary>
public sealed class SqlRowStoreTests
{
    private const string DefaultConnection =
        "Server=localhost;Database=SIMS_APDP;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

    private static string ConnectionString
        => Environment.GetEnvironmentVariable("SIMS_SQL_CONNECTION") ?? DefaultConnection;

    private static string NewTable()
        => "t_" + Guid.NewGuid().ToString("N")[..12];

    private static async Task<bool> SqlAvailableAsync()
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task DropAsync(string table)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE IF EXISTS [sims].[{table}]";
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            // Bảng có thể chưa được tạo — bỏ qua, không che lỗi test.
        }
    }

    [Fact]
    public async Task Append_and_read_keeps_cell_order_and_pads()
    {
        if (!await SqlAvailableAsync())
        {
            return;
        }

        var table = NewTable();
        try
        {
            var store = new SqlRowStore(ConnectionString, table, "id", "name", "note");

            await store.AppendAsync(new[] { "1", "Alpha" });
            await store.AppendAsync(new[] { "2", "Beta", "ghi chú tiếng Việt" });

            var rows = await store.ReadAllAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal("1", rows[0][0]);
            Assert.Equal("Alpha", rows[0][1]);
            Assert.Equal(string.Empty, rows[0][2]);
            Assert.Equal("ghi chú tiếng Việt", rows[1][2]);
        }
        finally
        {
            await DropAsync(table);
        }
    }

    [Fact]
    public async Task Mutate_updates_in_place_and_reports_change()
    {
        if (!await SqlAvailableAsync())
        {
            return;
        }

        var table = NewTable();
        try
        {
            var store = new SqlRowStore(ConnectionString, table, "id", "name");
            await store.AppendAsync(new[] { "1", "Alpha" });
            await store.AppendAsync(new[] { "2", "Beta" });

            var changed = await store.MutateAsync(rows =>
            {
                var copy = rows.Select(r => r.ToArray()).ToList();
                copy[0][1] = "Alpha2";
                return copy;
            });

            Assert.True(changed);
            var rows = await store.ReadAllAsync();
            Assert.Equal("Alpha2", rows[0][1]);
            Assert.Equal("Beta", rows[1][1]);
        }
        finally
        {
            await DropAsync(table);
        }
    }

    [Fact]
    public async Task Mutate_returns_false_when_handler_declines()
    {
        if (!await SqlAvailableAsync())
        {
            return;
        }

        var table = NewTable();
        try
        {
            var store = new SqlRowStore(ConnectionString, table, "id");
            await store.AppendAsync(new[] { "1" });

            Assert.False(await store.MutateAsync(_ => null));
            Assert.Single(await store.ReadAllAsync());
        }
        finally
        {
            await DropAsync(table);
        }
    }

    [Fact]
    public void Factory_defaults_to_sql_and_rejects_missing_connection()
    {
        var factory = new RowStoreFactory(RowStoreFactory.SqlProvider, DefaultConnection, dataDirectory: null);

        Assert.IsType<SqlRowStore>(factory.Create("fact_probe", "id"));
        Assert.Throws<InvalidOperationException>(
            () => new RowStoreFactory(RowStoreFactory.SqlProvider, null, dataDirectory: null));
    }

    [Theory]
    [InlineData("a; DROP TABLE x--")]
    [InlineData("../../evil")]
    [InlineData("")]
    [InlineData("1items")]
    [InlineData("has space")]
    [InlineData("tên_bảng")]
    public void Invalid_table_names_are_rejected(string table)
    {
        Assert.False(SqlRowStore.IsValidTableName(table));
        Assert.Throws<ArgumentException>(() => new SqlRowStore(DefaultConnection, table, "id"));

        // Nhánh CSV cũng phải chặn: tên bảng ghép thẳng vào đường dẫn file.
        var factory = new RowStoreFactory(RowStoreFactory.CsvProvider, null, Path.GetTempPath());
        Assert.Throws<ArgumentException>(() => factory.Create(table, "id"));
    }

    [Theory]
    [InlineData("students")]
    [InlineData("t_0123456789ab")]
    [InlineData("A1_b2")]
    public void Valid_table_names_are_accepted(string table)
        => Assert.True(SqlRowStore.IsValidTableName(table));

    [Fact]
    public async Task Concurrent_appends_do_not_mix_cells()
    {
        if (!await SqlAvailableAsync())
        {
            return;
        }

        var table = NewTable();
        try
        {
            var store = new SqlRowStore(ConnectionString, table, "id", "name", "note");

            // 8 luồng append song song: mỗi bản ghi chỉ được chứa ô của chính nó.
            var writes = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
                await store.AppendAsync(new[] { i.ToString(), "name" + i, "note" + i })));

            await Task.WhenAll(writes);

            var rows = await store.ReadAllAsync();
            Assert.Equal(8, rows.Count);
            foreach (var row in rows)
            {
                Assert.Equal("name" + row[0], row[1]);
                Assert.Equal("note" + row[0], row[2]);
            }
        }
        finally
        {
            await DropAsync(table);
        }
    }

    [Fact]
    public void Factory_creates_csv_store_for_csv_provider()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sims-factory-" + Guid.NewGuid().ToString("N"));
        try
        {
            var factory = new RowStoreFactory(RowStoreFactory.CsvProvider, null, directory);

            Assert.IsType<CsvFileStore>(factory.Create("items", "id"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
