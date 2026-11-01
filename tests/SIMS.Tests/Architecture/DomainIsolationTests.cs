namespace SIMS.Tests.Architecture;

/// <summary>
/// Khoá ràng buộc kiến trúc của bước 2: Domain là project lá, không tham chiếu
/// project khác và không kéo theo System.IO / System.Net.Http.
/// </summary>
public sealed class DomainIsolationTests
{
    [Fact]
    public void Domain_project_has_no_project_references()
    {
        var csproj = File.ReadAllText(DomainCsprojPath());

        Assert.DoesNotContain("<ProjectReference", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Domain_project_removes_io_and_http_implicit_usings()
    {
        var csproj = File.ReadAllText(DomainCsprojPath());

        Assert.Contains("<Using Remove=\"System.IO\" />", csproj, StringComparison.Ordinal);
        Assert.Contains("<Using Remove=\"System.Net.Http\" />", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Solution_contains_five_projects()
    {
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot(), "SIMS.sln"));
        var projectCount = solution.Split(".csproj", StringSplitOptions.None).Length - 1;

        Assert.Equal(5, projectCount);
    }

    private static string DomainCsprojPath()
        => Path.Combine(RepositoryRoot(), "src", "SIMS.Domain", "SIMS.Domain.csproj");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SIMS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Không tìm thấy SIMS.sln từ thư mục test.");
    }
}
