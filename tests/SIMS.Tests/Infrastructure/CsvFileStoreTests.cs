using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvFileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Append_writes_header_once_and_reads_rows()
    {
        var path = Path.Combine(_directory, "items.csv");
        var store = new CsvFileStore(path, "id", "name");

        await store.AppendAsync(new[] { "1", "Alpha" });
        await store.AppendAsync(new[] { "2", "Beta, với dấu phẩy" });

        var rows = await store.ReadAllAsync();
        var lines = await File.ReadAllLinesAsync(path);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Beta, với dấu phẩy", rows[1][1]);
        Assert.Equal("id,name", lines[0]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public async Task Mutate_replaces_file_atomically_and_reports_change()
    {
        var path = Path.Combine(_directory, "items.csv");
        var store = new CsvFileStore(path, "id", "name");
        await store.AppendAsync(new[] { "1", "Alpha" });

        var changed = await store.MutateAsync(rows =>
            rows.Select(r => r[0] == "1" ? new[] { "1", "Alpha (updated)" } : r).ToList());

        Assert.True(changed);
        var rowsAfter = await store.ReadAllAsync();
        Assert.Equal("Alpha (updated)", rowsAfter[0][1]);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*"));
    }

    [Fact]
    public async Task Mutate_returns_false_when_nothing_changes()
    {
        var path = Path.Combine(_directory, "items.csv");
        var store = new CsvFileStore(path, "id", "name");
        await store.AppendAsync(new[] { "1", "Alpha" });

        var changed = await store.MutateAsync(_ => null);

        Assert.False(changed);
    }

    [Fact]
    public async Task Concurrent_appends_are_serialized_per_file()
    {
        var path = Path.Combine(_directory, "concurrent.csv");
        var store = new CsvFileStore(path, "id", "value");

        var tasks = Enumerable.Range(0, 50)
            .Select(i => store.AppendAsync(new[] { i.ToString(), $"v{i}" }));

        await Task.WhenAll(tasks);

        var rows = await store.ReadAllAsync();
        Assert.Equal(50, rows.Count);
        Assert.Equal(50, rows.Select(r => r[0]).Distinct().Count());
    }
}
