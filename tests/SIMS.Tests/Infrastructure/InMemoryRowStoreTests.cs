using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

/// <summary>
/// Kho dòng trong bộ nhớ có cùng ngữ nghĩa với CSV/SQL: pad ô rỗng, mutate nguyên tử.
/// </summary>
public sealed class InMemoryRowStoreTests
{
    [Fact]
    public async Task Append_pads_short_rows_to_column_count()
    {
        var store = new InMemoryRowStore("pad-" + Guid.NewGuid().ToString("N"), "a", "b", "c");

        await store.AppendAsync(new[] { "x" });

        var rows = await store.ReadAllAsync();
        Assert.Single(rows);
        Assert.Equal(new[] { "x", string.Empty, string.Empty }, rows[0]);
    }

    [Fact]
    public async Task Mutate_returns_false_when_handler_declines()
    {
        var store = new InMemoryRowStore("noop-" + Guid.NewGuid().ToString("N"), "a");
        await store.AppendAsync(new[] { "x" });

        var changed = await store.MutateAsync(_ => null);

        Assert.False(changed);
        Assert.Single(await store.ReadAllAsync());
    }

    [Fact]
    public async Task Mutate_replaces_contents()
    {
        var store = new InMemoryRowStore("mut-" + Guid.NewGuid().ToString("N"), "a", "b");
        await store.AppendAsync(new[] { "x", "y" });
        await store.AppendAsync(new[] { "p", "q" });

        var changed = await store.MutateAsync(rows => new List<string[]> { rows[1] });

        Assert.True(changed);
        var remaining = await store.ReadAllAsync();
        Assert.Single(remaining);
        Assert.Equal("p", remaining[0][0]);
    }
}
