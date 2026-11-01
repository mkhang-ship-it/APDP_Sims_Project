using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SIMS.Application.Abstractions;
using SIMS.Application.Documents;
using SIMS.Domain.Awards;
using SIMS.Domain.Documents;

namespace SIMS.Tests.Web;

/// <summary>Batch 9: văn bản, kỷ luật/điểm rèn luyện và khen thưởng.</summary>
public sealed class GovernanceIntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public GovernanceIntegrationTests(SimsWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClientWithoutRedirect();
        var response = await SimsWebApplicationFactory.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    private async Task<(Guid ClubId, Guid StudentUserId, Guid FacultyUserId, Guid DocumentTypeId)> GetContextAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<IClubRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var types = scope.ServiceProvider.GetRequiredService<IDocumentTypeRepository>();

        var club = await clubs.FindByCodeAsync("CLB01");
        var student = await users.FindByUsernameAsync("student1");
        var faculty = await users.FindByUsernameAsync("faculty1");
        Assert.NotNull(club);
        Assert.NotNull(student);
        Assert.NotNull(faculty);

        var typeList = await types.ListByClubAsync(club!.Id);
        Assert.NotEmpty(typeList);

        return (club.Id, student!.Id, faculty!.Id, typeList[0].Id);
    }

    private async Task<Document> FindDocumentAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var match = (await documents.ListAsync()).FirstOrDefault(document => document.Title == title);
        Assert.NotNull(match);

        return match!;
    }

    private async Task<Award> FindAwardAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var awards = scope.ServiceProvider.GetRequiredService<IAwardRepository>();
        var match = (await awards.ListAsync()).FirstOrDefault(award => award.Title == title);
        Assert.NotNull(match);

        return match!;
    }

    private static async Task<HttpResponseMessage> PostMultipartAsync(
        HttpClient client,
        string url,
        string formUrl,
        Dictionary<string, string> fields,
        string fileField,
        string fileName,
        string fileContent)
    {
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, formUrl);
        using var content = new MultipartFormDataContent();

        foreach (var field in fields)
        {
            content.Add(new StringContent(field.Value, Encoding.UTF8), field.Key);
        }

        content.Add(new StringContent(token, Encoding.UTF8), "__RequestVerificationToken");
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContent));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, fileField, fileName);

        return await client.PostAsync(url, content);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string url,
        string formUrl,
        Dictionary<string, string> fields)
    {
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, formUrl);
        fields["__RequestVerificationToken"] = token;

        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task Faculty_sees_documents_types_and_downloads_file()
    {
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var index = await client.GetAsync("/Documents");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        var html = await index.Content.ReadAsStringAsync();
        Assert.Contains("Văn bản", html);
        Assert.Contains("Mẫu đơn xác nhận tham gia", html);

        var types = await client.GetAsync("/Documents/Types");
        Assert.Equal(HttpStatusCode.OK, types.StatusCode);
        Assert.Contains("Loại văn bản", await types.Content.ReadAsStringAsync());

        var document = await FindDocumentAsync("Mẫu đơn xác nhận tham gia");
        var details = await client.GetAsync($"/Documents/Details/{document.Id}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);

        var download = await client.GetAsync($"/Documents/Download/{document.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.True((await download.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task Faculty_uploads_and_approves_document()
    {
        var (clubId, _, _, typeId) = await GetContextAsync();
        using var client = await LoginAsync("faculty1", "Faculty@12345");
        var title = "Văn bản web " + Guid.NewGuid().ToString("N")[..6];

        var upload = await PostMultipartAsync(
            client,
            "/Documents/Create",
            "/Documents/Create",
            new Dictionary<string, string>
            {
                ["ClubId"] = clubId.ToString(),
                ["DocumentTypeId"] = typeId.ToString(),
                ["Title"] = title,
                ["Visibility"] = nameof(DocumentVisibility.Public),
            },
            "File",
            "van-ban.txt",
            "nội dung văn bản");

        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);
        var document = await FindDocumentAsync(title);
        Assert.Equal(DocumentStatus.Pending, document.Status);
        Assert.Equal("faculty1", document.UploadedBy);

        var approve = await PostFormAsync(
            client,
            $"/Documents/Review/{document.Id}",
            $"/Documents/Details/{document.Id}",
            new Dictionary<string, string> { ["approve"] = "true" });

        Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);
        var approved = await FindDocumentAsync(title);
        Assert.Equal(DocumentStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task Student_uploads_document_and_cannot_open_staff_list()
    {
        var (_, _, _, typeId) = await GetContextAsync();
        using var client = await LoginAsync("student1", "Student@12345");

        var denied = await client.GetAsync("/Documents");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);

        var myPage = await client.GetAsync("/Documents/My");
        Assert.Equal(HttpStatusCode.OK, myPage.StatusCode);

        var title = "Đơn của sinh viên " + Guid.NewGuid().ToString("N")[..6];
        var upload = await PostMultipartAsync(
            client,
            "/Documents/MyUpload",
            "/Documents/My",
            new Dictionary<string, string>
            {
                ["Title"] = title,
                ["DocumentTypeId"] = typeId.ToString(),
            },
            "File",
            "don.txt",
            "đơn xin nghỉ");

        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);
        var document = await FindDocumentAsync(title);
        Assert.Equal("student1", document.UploadedBy);
        Assert.Equal(DocumentStatus.Pending, document.Status);

        var accessible = await client.GetStringAsync("/Documents/Accessible");
        Assert.Contains("Mẫu đơn xác nhận tham gia", accessible);

        var seeded = await FindDocumentAsync("Mẫu đơn xác nhận tham gia");
        var download = await client.GetAsync($"/Documents/Download/{seeded.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task Student_cannot_download_private_document_of_another_member()
    {
        var (clubId, _, _, typeId) = await GetContextAsync();
        string title;

        using (var scope = _factory.Services.CreateScope())
        {
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            title = "Tài liệu riêng " + Guid.NewGuid().ToString("N")[..6];
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("riêng tư"));
            var uploaded = await documents.UploadAsync(
                clubId, typeId, title, "faculty1", DocumentVisibility.Private,
                stream, "rieng.txt", "faculty1");
            Assert.True(uploaded.IsSuccess);
            await documents.ReviewAsync(uploaded.Value!.Id, true, null, "faculty1");
        }

        var document = await FindDocumentAsync(title);
        using var client = await LoginAsync("student1", "Student@12345");
        var download = await client.GetAsync($"/Documents/Download/{document.Id}");

        Assert.Equal(HttpStatusCode.Redirect, download.StatusCode);
        Assert.Equal("/Account/AccessDenied", download.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Document_request_flow_from_faculty_to_student()
    {
        var (clubId, studentId, _, typeId) = await GetContextAsync();
        using var faculty = await LoginAsync("faculty1", "Faculty@12345");

        var create = await PostFormAsync(
            faculty,
            "/Documents/CreateRequest",
            "/Documents/Requests",
            new Dictionary<string, string>
            {
                ["ClubId"] = clubId.ToString(),
                ["DocumentTypeId"] = typeId.ToString(),
                ["Deadline"] = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd"),
                ["UserIds"] = studentId.ToString(),
            });

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        Guid assignmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var requestUsers = scope.ServiceProvider.GetRequiredService<IDocumentRequestUserRepository>();
            var assignments = await requestUsers.ListByUserAsync(studentId);
            Assert.NotEmpty(assignments);
            assignmentId = assignments
                .Where(item => !item.IsSubmitted)
                .OrderByDescending(item => item.Id)
                .First().Id;
        }

        using var student = await LoginAsync("student1", "Student@12345");
        var myRequests = await student.GetStringAsync("/Documents/MyRequests");
        Assert.Contains("Yêu cầu nộp", myRequests);

        var title = "Đơn nộp theo yêu cầu " + Guid.NewGuid().ToString("N")[..6];
        var submit = await PostMultipartAsync(
            student,
            "/Documents/SubmitRequest",
            "/Documents/MyRequests",
            new Dictionary<string, string>
            {
                ["requestUserId"] = assignmentId.ToString(),
                ["title"] = title,
            },
            "file",
            "don.txt",
            "nội dung nộp");

        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var requestUsers = scope.ServiceProvider.GetRequiredService<IDocumentRequestUserRepository>();
            var assignment = await requestUsers.FindByIdAsync(assignmentId);
            Assert.True(assignment!.IsSubmitted);
        }

        var submitted = await FindDocumentAsync(title);
        Assert.Equal(DocumentStatus.Pending, submitted.Status);

        var requestsPage = await faculty.GetStringAsync("/Documents/Requests");
        Assert.Contains("đã nộp", requestsPage);
    }

    [Fact]
    public async Task Discipline_pages_and_export_for_staff_and_my_points_for_student()
    {
        using var faculty = await LoginAsync("faculty1", "Faculty@12345");

        var index = await faculty.GetAsync("/Disciplines");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("Tham gia đầy đủ hoạt động", await index.Content.ReadAsStringAsync());

        var records = await faculty.GetAsync("/Disciplines/Records");
        Assert.Equal(HttpStatusCode.OK, records.StatusCode);

        var summary = await faculty.GetAsync("/Disciplines/Summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Contains("Điểm trường", await summary.Content.ReadAsStringAsync());

        var clubId = (await GetContextAsync()).ClubId;
        var export = await faculty.GetAsync($"/Disciplines/ExportSummary?clubId={clubId}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        var csv = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.Contains("school_points", csv);
        Assert.Contains("student1", csv);

        using var student = await LoginAsync("student1", "Student@12345");
        var denied = await student.GetAsync("/Disciplines");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);

        var myPoints = await student.GetAsync("/Disciplines/MyPoints");
        Assert.Equal(HttpStatusCode.OK, myPoints.StatusCode);
        var pointsHtml = await myPoints.Content.ReadAsStringAsync();
        Assert.Contains("Điểm rèn luyện của tôi", pointsHtml);
        Assert.Contains("Điểm CLB", pointsHtml);
    }

    [Fact]
    public async Task Awards_add_recipient_and_student_is_denied()
    {
        var (_, _, facultyId, _) = await GetContextAsync();
        var award = await FindAwardAsync("Thành viên xuất sắc học kỳ");
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var index = await client.GetAsync("/Awards");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("Thành viên xuất sắc học kỳ", await index.Content.ReadAsStringAsync());

        var details = await client.GetAsync($"/Awards/Details/{award.Id}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);

        var add = await PostFormAsync(
            client,
            $"/Awards/AddRecipient/{award.Id}",
            $"/Awards/Details/{award.Id}",
            new Dictionary<string, string> { ["userId"] = facultyId.ToString() });

        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var recipients = scope.ServiceProvider.GetRequiredService<IAwardRecipientRepository>();
            var items = await recipients.ListByAwardAsync(award.Id);
            Assert.Equal(2, items.Count);
        }

        using var student = await LoginAsync("student1", "Student@12345");
        var denied = await student.GetAsync("/Awards");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Anonymous_is_redirected_from_governance_modules()
    {
        using var client = _factory.CreateClientWithoutRedirect();

        foreach (var url in new[] { "/Documents", "/Disciplines", "/Awards" })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        }
    }
}
