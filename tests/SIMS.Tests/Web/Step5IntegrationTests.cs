using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SIMS.Application.Abstractions;
using SIMS.Application.Students;

namespace SIMS.Tests.Web;

/// <summary>Batch 5: phân quyền theo module + Student↔User + multi-club.</summary>
public sealed class Step5IntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public Step5IntegrationTests(SimsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClientWithoutRedirect();
        var response = await SimsWebApplicationFactory.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    [Fact]
    public async Task Student_can_see_own_learning_profile_and_grades()
    {
        using var client = await LoginAsync("student1", "Student@12345");

        var response = await client.GetAsync("/MyLearning");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("SV0001", html);
        Assert.Contains("8.5", html);
        Assert.Contains(">B</span>", html);
    }

    [Fact]
    public async Task Student_menu_shows_learning_but_not_students_module()
    {
        using var client = await LoginAsync("student1", "Student@12345");

        var html = await client.GetStringAsync("/");

        Assert.Contains("href=\"/MyLearning\"", html);
        Assert.DoesNotContain("href=\"/Students\"", html);
        Assert.DoesNotContain("href=\"/Courses\"", html);
    }

    [Fact]
    public async Task Faculty_student_list_is_scoped_to_current_club()
    {
        // Tạo sinh viên thuộc CLB khác (CLB02) — faculty1 đang ở CLB01 nên không thấy.
        using (var scope = _factory.Services.CreateScope())
        {
            var clubs = scope.ServiceProvider.GetRequiredService<IClubRepository>();
            var students = scope.ServiceProvider.GetRequiredService<IStudentService>();
            var otherClub = await clubs.FindByCodeAsync("CLB02");

            var created = await students.CreateAsync(
                new StudentInput("SV8802", "Sinh viên CLB khác", "sv8802@example.com", new DateOnly(2004, 3, 3), "CNTT"),
                "tests",
                otherClub!.Id);

            Assert.True(created.IsSuccess);
        }

        using var client = await LoginAsync("faculty1", "Faculty@12345");
        var html = await client.GetStringAsync("/Students");

        Assert.DoesNotContain("SV8802", html);
    }

    [Fact]
    public async Task Student_can_switch_between_joined_clubs()
    {
        Guid englishClubId;
        using (var scope = _factory.Services.CreateScope())
        {
            var clubs = scope.ServiceProvider.GetRequiredService<IClubRepository>();
            englishClubId = (await clubs.FindByCodeAsync("CLB02"))!.Id;
        }

        using var client = await LoginAsync("student1", "Student@12345");
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Clubs");

        var switchResponse = await client.PostAsync("/Clubs/Switch", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["clubId"] = englishClubId.ToString("D"),
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, switchResponse.StatusCode);
        Assert.Equal("/", switchResponse.Headers.Location?.OriginalString);

        var clubsHtml = await client.GetStringAsync("/Clubs");
        Assert.Contains("Câu lạc bộ Tiếng Anh", clubsHtml);
        Assert.Contains("Đang chọn", clubsHtml);
        Assert.True(
            clubsHtml.IndexOf("Đang chọn", StringComparison.Ordinal) >
            clubsHtml.IndexOf("CLB02", StringComparison.Ordinal),
            "CLB hiện tại phải là CLB02 sau khi chuyển.");
    }

    [Fact]
    public async Task Faculty_cannot_open_club_creation_without_permission()
    {
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var response = await client.GetAsync("/Clubs/Create");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);

        var denied = await client.GetAsync("/Account/AccessDenied");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Admin_can_open_club_creation()
    {
        using var client = await LoginAsync("admin", "Admin@12345");

        var response = await client.GetAsync("/Clubs/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
