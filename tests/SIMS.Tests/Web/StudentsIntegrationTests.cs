using System.Net;
using SIMS.Domain.Security;

namespace SIMS.Tests.Web;

public sealed class StudentsIntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public StudentsIntegrationTests(SimsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Student_role_gets_403_on_students_module()
    {
        await _factory.CreateUserAsync("sinhvien1", "Student@123", Role.Student);
        using var client = _factory.CreateClientWithoutRedirect();

        await SimsWebApplicationFactory.LoginAsync(client, "sinhvien1", "Student@123");
        var forbidden = await client.GetAsync("/Students");

        Assert.Equal(HttpStatusCode.Redirect, forbidden.StatusCode);
        Assert.Equal("/Account/AccessDenied", forbidden.Headers.Location?.AbsolutePath);

        var denied = await client.GetAsync("/Account/AccessDenied");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Faculty_can_create_student_and_see_it_in_list()
    {
        await _factory.CreateUserAsync("giangvien1", "Faculty@123", Role.Faculty);
        using var client = _factory.CreateClientWithoutRedirect();

        await SimsWebApplicationFactory.LoginAsync(client, "giangvien1", "Faculty@123");
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Students/Create");

        var create = await client.PostAsync("/Students/Create", Form(new Dictionary<string, string>
        {
            ["StudentCode"] = "SV9001",
            ["FullName"] = "Nguyễn Văn Test",
            ["Email"] = "sv9001@example.com",
            ["DateOfBirth"] = "2004-05-20",
            ["Program"] = "Công nghệ thông tin",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Equal("/Students", create.Headers.Location?.OriginalString);

        var list = await client.GetStringAsync("/Students");
        Assert.Contains("SV9001", list);
        Assert.Contains("Nguyễn Văn Test", list);

        var audit = await File.ReadAllTextAsync(Path.Combine(_factory.DataDirectory, "audit.csv"));
        Assert.Contains("student.create", audit);
        Assert.Contains("giangvien1", audit);
    }

    [Fact]
    public async Task Duplicate_student_code_is_rejected_with_field_error()
    {
        await _factory.CreateUserAsync("giangvien2", "Faculty@123", Role.Faculty);
        using var client = _factory.CreateClientWithoutRedirect();
        await SimsWebApplicationFactory.LoginAsync(client, "giangvien2", "Faculty@123");

        var token1 = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Students/Create");
        await client.PostAsync("/Students/Create", Form(new Dictionary<string, string>
        {
            ["StudentCode"] = "SV9002",
            ["FullName"] = "Nguyễn Văn Test",
            ["Email"] = "sv9002@example.com",
            ["DateOfBirth"] = "2004-05-20",
            ["Program"] = "CNTT",
            ["__RequestVerificationToken"] = token1,
        }));

        var token2 = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Students/Create");
        var duplicate = await client.PostAsync("/Students/Create", Form(new Dictionary<string, string>
        {
            ["StudentCode"] = "SV9002",
            ["FullName"] = "Trần Thị Khác",
            ["Email"] = "khac@example.com",
            ["DateOfBirth"] = "2005-01-01",
            ["Program"] = "Kế toán",
            ["__RequestVerificationToken"] = token2,
        }));

        var html = await duplicate.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("đã tồn tại", html);
    }

    [Fact]
    public async Task Invalid_student_form_shows_validation_errors()
    {
        await _factory.CreateUserAsync("giangvien3", "Faculty@123", Role.Faculty);
        using var client = _factory.CreateClientWithoutRedirect();
        await SimsWebApplicationFactory.LoginAsync(client, "giangvien3", "Faculty@123");

        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Students/Create");
        var response = await client.PostAsync("/Students/Create", Form(new Dictionary<string, string>
        {
            ["StudentCode"] = "A",
            ["FullName"] = "",
            ["Email"] = "khong-hop-le",
            ["DateOfBirth"] = "",
            ["Program"] = "",
            ["__RequestVerificationToken"] = token,
        }));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("MSSV phải có từ 3 đến 20 ký tự", html);
        Assert.Contains("Email không hợp lệ", html);
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values)
        => new(values);
}
