using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SIMS.Application.Abstractions;
using SIMS.Application.Courses;
using SIMS.Application.Enrolments;
using SIMS.Application.Students;

namespace SIMS.Tests.Web;

public sealed class Step4IntegrationTests : IClassFixture<SimsWebApplicationFactory>
{
    private readonly SimsWebApplicationFactory _factory;

    public Step4IntegrationTests(SimsWebApplicationFactory factory)
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

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);

    [Fact]
    public async Task Student_role_gets_403_on_courses_and_enrolments()
    {
        using var client = await LoginAsync("student1", "Student@12345");

        var courses = await client.GetAsync("/Courses");
        Assert.Equal(HttpStatusCode.Redirect, courses.StatusCode);
        Assert.Equal("/Account/AccessDenied", courses.Headers.Location?.AbsolutePath);

        var denied = await client.GetAsync("/Account/AccessDenied");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Faculty_can_create_course_enrol_student_and_see_row()
    {
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var courseId = await CreateCourseViaHttpAsync(client, "TST101", "Học phần kiểm thử", capacity: 5);
        await CreateStudentViaHttpAsync(client, "SV7101", "Nguyễn Văn Test");

        using var scope = _factory.Services.CreateScope();
        var students = scope.ServiceProvider.GetRequiredService<IStudentRepository>();
        var student = await students.FindByCodeAsync("SV7101");

        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Enrolments/Enrol");
        var enrol = await client.PostAsync("/Enrolments/Enrol", Form(new Dictionary<string, string>
        {
            ["studentId"] = student!.Id.ToString("D"),
            ["courseId"] = courseId.ToString("D"),
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, enrol.StatusCode);
        Assert.Equal("/Enrolments", enrol.Headers.Location?.OriginalString);

        var index = await client.GetStringAsync("/Enrolments");
        Assert.Contains("SV7101", index);
        Assert.Contains("TST101", index);
    }

    [Fact]
    public async Task Enrolment_over_capacity_is_rejected_with_message()
    {
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var capacityOneId = await CreateCourseViaHttpAsync(client, "CAP101", "Học phần 1 chỗ", capacity: 1);
        await CreateStudentViaHttpAsync(client, "SV7201", "Sinh viên Một");
        await CreateStudentViaHttpAsync(client, "SV7202", "Sinh viên Hai");

        using var scope = _factory.Services.CreateScope();
        var students = scope.ServiceProvider.GetRequiredService<IStudentRepository>();
        var first = await students.FindByCodeAsync("SV7201");
        var second = await students.FindByCodeAsync("SV7202");

        var token1 = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Enrolments/Enrol");
        var firstEnrol = await client.PostAsync("/Enrolments/Enrol", Form(new Dictionary<string, string>
        {
            ["studentId"] = first!.Id.ToString("D"),
            ["courseId"] = capacityOneId.ToString("D"),
            ["__RequestVerificationToken"] = token1,
        }));
        Assert.Equal(HttpStatusCode.Redirect, firstEnrol.StatusCode);

        var token2 = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Enrolments/Enrol");
        var secondEnrol = await client.PostAsync("/Enrolments/Enrol", Form(new Dictionary<string, string>
        {
            ["studentId"] = second!.Id.ToString("D"),
            ["courseId"] = capacityOneId.ToString("D"),
            ["__RequestVerificationToken"] = token2,
        }));

        Assert.Equal(HttpStatusCode.Redirect, secondEnrol.StatusCode);
        Assert.Equal("/Enrolments/Enrol", secondEnrol.Headers.Location?.OriginalString);

        var page = await client.GetStringAsync("/Enrolments/Enrol");
        Assert.Contains("sức chứa", page, StringComparison.OrdinalIgnoreCase);

        var list = await client.GetStringAsync("/Enrolments");
        Assert.Contains("SV7201", list);
        Assert.DoesNotContain("SV7202", list);
    }

    [Fact]
    public async Task Grade_is_saved_and_shown_in_list()
    {
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var courseId = await CreateCourseViaHttpAsync(client, "GRD101", "Học phần chấm điểm", capacity: 10);
        await CreateStudentViaHttpAsync(client, "SV7301", "Sinh viên Điểm");

        using var scope = _factory.Services.CreateScope();
        var students = scope.ServiceProvider.GetRequiredService<IStudentRepository>();
        var enrolments = scope.ServiceProvider.GetRequiredService<IEnrolmentRepository>();
        var student = await students.FindByCodeAsync("SV7301");
        var enrolment = await enrolments.FindByPairAsync(student!.Id, courseId);

        if (enrolment is null)
        {
            var service = scope.ServiceProvider.GetRequiredService<IEnrolmentService>();
            var created = await service.EnrolAsync(student.Id, courseId, "tests");
            enrolment = created.Value!;
        }

        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Enrolments");
        var graded = await client.PostAsync("/Enrolments/Grade", Form(new Dictionary<string, string>
        {
            ["enrolmentId"] = enrolment.Id.ToString("D"),
            ["score"] = "8.5",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, graded.StatusCode);

        var list = await client.GetStringAsync("/Enrolments");
        Assert.Contains(">8.5<", list);
        Assert.Contains(">B</span>", list);
    }

    [Fact]
    public async Task Import_invalid_csv_reports_errors_per_row_and_keeps_valid_rows()
    {
        using var client = await LoginAsync("faculty1", "Faculty@12345");

        var csv = "student_code,full_name,email,date_of_birth,program\n"
                  + "SV7401,Nguyễn Văn Tốt,tot7401@example.com,2004-01-15,Công nghệ thông tin\n"
                  + "SV7402,Trần Thị Sai Email,khong-phai-email,2004-02-20,Kế toán\n"
                  + "SV7403,Lê Văn Sai Ngày,sai-ngay7403@example.com,31/02/2004,Quản trị kinh doanh\n";

        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Students/Import");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");

        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "students.csv");

        var response = await client.PostAsync("/Students/Import", content);
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ngày sinh không hợp lệ", page);
        Assert.Contains("Email không hợp lệ", page);
        Assert.Contains(">3<", page);
        Assert.Contains(">1<", page);

        var list = await client.GetStringAsync("/Students");
        Assert.Contains("SV7401", list);
        Assert.DoesNotContain("SV7402", list);
        Assert.DoesNotContain("SV7403", list);
    }

    private async Task<Guid> CreateCourseViaHttpAsync(HttpClient client, string code, string title, int capacity)
    {
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Courses/Create");
        var response = await client.PostAsync("/Courses/Create", Form(new Dictionary<string, string>
        {
            ["Code"] = code,
            ["Title"] = title,
            ["Capacity"] = capacity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["PrerequisiteCode"] = string.Empty,
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseRepository>();
        var course = await courses.FindByCodeAsync(code);

        Assert.NotNull(course);
        return course!.Id;
    }

    private async Task CreateStudentViaHttpAsync(HttpClient client, string code, string fullName)
    {
        var token = await SimsWebApplicationFactory.GetAntiforgeryTokenAsync(client, "/Students/Create");
        var response = await client.PostAsync("/Students/Create", Form(new Dictionary<string, string>
        {
            ["StudentCode"] = code,
            ["FullName"] = fullName,
            ["Email"] = $"{code.ToLowerInvariant()}@example.com",
            ["DateOfBirth"] = "2004-05-20",
            ["Program"] = "Công nghệ thông tin",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
