using System.Text;
using SIMS.Application.Abstractions;
using SIMS.Application.EventPlans;
using SIMS.Application.Finance;
using SIMS.Application.Awards;
using SIMS.Application.Disciplines;
using SIMS.Application.Documents;
using SIMS.Application.LuckyDraws;
using SIMS.Application.Notifications;
using SIMS.Application.Settings;
using SIMS.Application.Security;
using SIMS.Domain.Clubs;
using SIMS.Domain.Courses;
using SIMS.Domain.Enrolments;
using SIMS.Domain.Events;
using SIMS.Domain.Finance;
using SIMS.Domain.Awards;
using SIMS.Domain.Documents;
using SIMS.Domain.Security;
using SIMS.Domain.Students;
using SIMS.Infrastructure.Storage;

namespace SIMS.Infrastructure.Security;

/// <summary>
/// Seed dữ liệu demo (idempotent): users, students, courses, enrolments, permissions, clubs,
/// memberships, audit, file CSV mẫu có lỗi. Chỉ chạy phần còn thiếu.
/// </summary>
public static class DefaultDataSeeder
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "Admin@12345";
    public const string FacultyUsername = "faculty1";
    public const string FacultyPassword = "Faculty@12345";
    public const string StudentUsername = "student1";
    public const string StudentPassword = "Student@12345";

    private const string InvalidImportSample = """
        student_code,full_name,email,date_of_birth,program
        SV9001,Nguyễn Văn Tốt,tot@example.com,2004-01-15,Công nghệ thông tin
        SV9002,Trần Thị Sai Email,khong-phai-email,2004-02-20,Kế toán
        SV9003,Lê Văn Sai Ngày,sai-ngay@example.com,31/02/2004,Quản trị kinh doanh
        """;

    public static async Task SeedAsync(
        IUserRepository users,
        IStudentRepository students,
        ICourseRepository courses,
        IEnrolmentRepository enrolments,
        IClubRepository clubs,
        IClubMembershipRepository memberships,
        IPermissionRepository permissions,
        IEventRepository events,
        IEventPlanService eventPlans,
        IInvoiceService invoices,
        IExpenseService expenses,
        IDocumentService documents,
        IDocumentRequestService documentRequests,
        IDisciplineService disciplines,
        IAwardService awards,
        ILuckyDrawService luckyDraws,
        INotificationService notifications,
        ISettingsService settings,
        IAuditLog audit,
        StoragePaths storage,
        IPasswordHasher passwordHasher,
        IClock clock,
        CancellationToken cancellationToken = default)
    {
        var isFirstRun = !await users.AnyAsync(cancellationToken).ConfigureAwait(false);

        await SeedUsersAsync(users, passwordHasher, clock, isFirstRun, cancellationToken).ConfigureAwait(false);
        var seededStudents = await SeedStudentsAsync(students, clock, cancellationToken).ConfigureAwait(false);
        var seededCourses = await SeedCoursesAsync(courses, clock, cancellationToken).ConfigureAwait(false);
        await SeedEnrolmentsAsync(enrolments, seededStudents, seededCourses, clock, cancellationToken).ConfigureAwait(false);
        await SeedPermissionsAsync(permissions, cancellationToken).ConfigureAwait(false);
        var seededClubs = await SeedClubsAsync(clubs, clock, cancellationToken).ConfigureAwait(false);
        await SeedMembershipsAsync(users, memberships, seededClubs, clock, cancellationToken).ConfigureAwait(false);
        await LinkDemoStudentAsync(users, students, seededStudents, seededClubs, cancellationToken).ConfigureAwait(false);
        await SeedEventsAsync(events, eventPlans, seededClubs, clock, cancellationToken).ConfigureAwait(false);
        await SeedFinanceAsync(users, invoices, expenses, seededClubs, clock, cancellationToken).ConfigureAwait(false);
        await SeedDocumentsAsync(users, documents, documentRequests, seededClubs, clock, cancellationToken).ConfigureAwait(false);
        await SeedDisciplinesAsync(users, disciplines, seededClubs, clock, cancellationToken).ConfigureAwait(false);
        await SeedAwardsAsync(users, awards, seededClubs, clock, cancellationToken).ConfigureAwait(false);
        await SeedEngagementAsync(
            users, luckyDraws, notifications, settings, seededClubs, clock, cancellationToken).ConfigureAwait(false);
        await SeedSampleImportFileAsync(storage, cancellationToken).ConfigureAwait(false);

        if (isFirstRun)
        {
            await audit.WriteAsync(
                new AuditEntry(clock.UtcNow, "system", "system.seed", "system", "bootstrap", "Khởi tạo dữ liệu demo"),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedUsersAsync(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IClock clock,
        bool isFirstRun,
        CancellationToken cancellationToken)
    {
        if (!isFirstRun)
        {
            return;
        }

        var accounts = new (string Username, string Password, string DisplayName, Role Role, string Email)[]
        {
            (AdminUsername, AdminPassword, "Quản trị hệ thống", Role.Admin, "admin@example.com"),
            (FacultyUsername, FacultyPassword, "Giảng viên demo", Role.Faculty, "faculty1@example.com"),
            (StudentUsername, StudentPassword, "Sinh viên demo", Role.Student, "demo@example.com"),
        };

        foreach (var account in accounts)
        {
            var user = User.Create(
                account.Username,
                account.DisplayName,
                account.Role,
                passwordHasher.Hash(account.Password),
                clock.UtcNow,
                account.Email);

            await users.AddAsync(user, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<Student>> SeedStudentsAsync(
        IStudentRepository students,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var existing = await students.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return existing;
        }

        var student = Student.Create(
            "SV0001",
            "Nguyễn Văn Demo",
            "demo@example.com",
            new DateOnly(2004, 1, 15),
            "Công nghệ thông tin",
            clock.UtcNow);

        await students.AddAsync(student, cancellationToken).ConfigureAwait(false);
        return new[] { student };
    }

    private static async Task<IReadOnlyList<Course>> SeedCoursesAsync(
        ICourseRepository courses,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var existing = await courses.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return existing;
        }

        var cs101 = Course.Create("CS101", "Nhập môn lập trình", 30, null, clock.UtcNow);
        var cs102 = Course.Create("CS102", "Cấu trúc dữ liệu", 20, "CS101", clock.UtcNow);
        var ma101 = Course.Create("MA101", "Toán rời rạc", 40, null, clock.UtcNow);

        foreach (var course in new[] { cs101, cs102, ma101 })
        {
            await courses.AddAsync(course, cancellationToken).ConfigureAwait(false);
        }

        return new[] { cs101, cs102, ma101 };
    }

    private static async Task SeedEnrolmentsAsync(
        IEnrolmentRepository enrolments,
        IReadOnlyList<Student> students,
        IReadOnlyList<Course> courses,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var existing = await enrolments.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0 || students.Count == 0)
        {
            return;
        }

        var student = students[0];
        var cs101 = courses.FirstOrDefault(c => c.Code == "CS101");
        var ma101 = courses.FirstOrDefault(c => c.Code == "MA101");

        if (cs101 is not null)
        {
            var completed = Enrolment.Create(student.Id, cs101.Id, clock.UtcNow.AddDays(-30));
            completed.AssignGrade(8.5m, "B");
            await enrolments.AddAsync(completed, cancellationToken).ConfigureAwait(false);
        }

        if (ma101 is not null)
        {
            var active = Enrolment.Create(student.Id, ma101.Id, clock.UtcNow.AddDays(-5));
            await enrolments.AddAsync(active, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedPermissionsAsync(
        IPermissionRepository permissions,
        CancellationToken cancellationToken)
    {
        var existing = await permissions.ListAsync(cancellationToken).ConfigureAwait(false);
        var missing = PermissionCatalog.MissingDefaults(existing);

        if (missing.Count == 0)
        {
            return;
        }

        // Bổ sung quyền mới khi nâng cấp (data cũ vẫn giữ nguyên quyền đã có).
        await permissions.AddRangeAsync(missing, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<Club>> SeedClubsAsync(
        IClubRepository clubs,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var existing = await clubs.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return existing;
        }

        var technology = Club.Create("CLB01", "Câu lạc bộ Công nghệ", clock.UtcNow);
        var english = Club.Create("CLB02", "Câu lạc bộ Tiếng Anh", clock.UtcNow);

        foreach (var club in new[] { technology, english })
        {
            await clubs.AddAsync(club, cancellationToken).ConfigureAwait(false);
        }

        return new[] { technology, english };
    }

    private static async Task SeedMembershipsAsync(
        IUserRepository users,
        IClubMembershipRepository memberships,
        IReadOnlyList<Club> clubs,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var existing = await memberships.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0 || clubs.Count == 0)
        {
            return;
        }

        var technology = clubs[0];
        var english = clubs.Count > 1 ? clubs[1] : clubs[0];

        var assignments = new (string Username, Club Club, Role Role)[]
        {
            (AdminUsername, technology, Role.Admin),
            (FacultyUsername, technology, Role.Faculty),
            (StudentUsername, technology, Role.Student),
            (StudentUsername, english, Role.Student),
        };

        foreach (var (username, club, role) in assignments)
        {
            var user = await users.FindByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                continue;
            }

            await memberships.AddAsync(
                ClubMembership.Create(user.Id, club.Id, role, clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task LinkDemoStudentAsync(
        IUserRepository users,
        IStudentRepository students,
        IReadOnlyList<Student> seededStudents,
        IReadOnlyList<Club> clubs,
        CancellationToken cancellationToken)
    {
        if (seededStudents.Count == 0 || clubs.Count == 0)
        {
            return;
        }

        var student = seededStudents[0];
        if (student.UserId is not null && student.ClubId is not null)
        {
            return;
        }

        var demoUser = await users.FindByUsernameAsync(StudentUsername, cancellationToken).ConfigureAwait(false);
        if (demoUser is null)
        {
            return;
        }

        if (student.UserId != demoUser.Id)
        {
            student.LinkUser(demoUser.Id);
        }

        if (student.ClubId is null)
        {
            student.AssignClub(clubs[0].Id);
        }

        await students.UpdateAsync(student, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedEventsAsync(
        IEventRepository events,
        IEventPlanService eventPlans,
        IReadOnlyList<Club> clubs,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var existing = await events.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0 || clubs.Count == 0)
        {
            return;
        }

        var clubId = clubs[0].Id;
        var now = clock.UtcNow;

        var published = Event.Create(
            "EVT001",
            "Ngày hội công nghệ",
            "Sự kiện demo đang mở check-in.",
            now.AddHours(-1),
            now.AddHours(6),
            "Hội trường A",
            50,
            clubId,
            now);
        published.Publish();

        var draft = Event.Create(
            "EVT002",
            "Workshop kỹ năng mềm",
            "Sự kiện demo ở trạng thái nháp.",
            now.AddDays(3),
            now.AddDays(3).AddHours(3),
            "Phòng B203",
            30,
            clubId,
            now);

        foreach (var @event in new[] { published, draft })
        {
            await events.AddAsync(@event, cancellationToken).ConfigureAwait(false);
        }

        // Kế hoạch mẫu cho sự kiện đã công bố (idempotent: chỉ thêm khi kế hoạch rỗng).
        var plan = await eventPlans.GetOrCreateAsync(published.Id, "system", cancellationToken).ConfigureAwait(false);
        if (plan.IsSuccess && plan.Value is not null && plan.Value.Objectives.Count == 0)
        {
            await eventPlans.AddObjectiveAsync(
                published.Id, "Tổ chức ngày hội công nghệ cho sinh viên", "system", cancellationToken).ConfigureAwait(false);
            await eventPlans.AddActionAsync(
                published.Id, "Chuẩn bị hội trường", "Ban tổ chức", DateOnly.FromDateTime(now.AddDays(1).UtcDateTime), "system", cancellationToken).ConfigureAwait(false);
            await eventPlans.AddBudgetItemAsync(
                published.Id, "Nước uống", 100, 5000m, "system", cancellationToken).ConfigureAwait(false);
            await eventPlans.AddEquipmentAsync(
                published.Id, "Máy chiếu", 2, "Phòng A", "system", cancellationToken).ConfigureAwait(false);
            await eventPlans.AddTimelineAsync(
                published.Id, "08:00", "Khai mạc", "system", cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedFinanceAsync(
        IUserRepository users,
        IInvoiceService invoices,
        IExpenseService expenses,
        IReadOnlyList<Club> clubs,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (clubs.Count == 0)
        {
            return;
        }

        var clubId = clubs[0].Id;
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var existingInvoices = await invoices.ListAsync(clubId, null, cancellationToken).ConfigureAwait(false);
        if (existingInvoices.Count == 0)
        {
            var student = await users.FindByUsernameAsync(StudentUsername, cancellationToken).ConfigureAwait(false);
            if (student is not null)
            {
                var created = await invoices.CreateAsync(
                    new InvoiceBatchInput(
                        clubId,
                        "Phí sinh hoạt học kỳ",
                        200_000m,
                        today.AddDays(7),
                        "HK1 2026-2027",
                        "Hoá đơn demo cho thành viên."),
                    new[] { student.Id },
                    "system",
                    cancellationToken).ConfigureAwait(false);

                if (created.IsSuccess && created.Value is { Count: > 0 })
                {
                    await invoices.RecordPaymentAsync(
                        created.Value[0].Id,
                        50_000m,
                        today,
                        PaymentMethod.Cash,
                        "Đóng một phần",
                        "system",
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        var existingExpenses = await expenses.ListAsync(clubId, null, cancellationToken).ConfigureAwait(false);
        if (existingExpenses.Count == 0)
        {
            await expenses.CreateAsync(
                clubId,
                "Mua văn phòng phẩm",
                150_000m,
                "HK1 2026-2027",
                "Chi phí demo đã duyệt, chờ chi tiền.",
                autoApprove: true,
                requestedBy: "system",
                actor: "system",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedDocumentsAsync(
        IUserRepository users,
        IDocumentService documents,
        IDocumentRequestService documentRequests,
        IReadOnlyList<Club> clubs,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (clubs.Count == 0)
        {
            return;
        }

        var clubId = clubs[0].Id;
        var types = await documents.ListTypesAsync(clubId, false, cancellationToken).ConfigureAwait(false);

        if (types.Count == 0)
        {
            var created = await documents.CreateTypeAsync(
                clubId,
                "Đơn xác nhận tham gia",
                "Mẫu đơn xác nhận tham gia câu lạc bộ.",
                null,
                "system",
                cancellationToken).ConfigureAwait(false);

            types = created.IsSuccess && created.Value is not null
                ? new[] { created.Value }
                : Array.Empty<DocumentType>();
        }

        var existingDocuments = await documents
            .ListAsync(clubId, null, null, cancellationToken)
            .ConfigureAwait(false);

        if (types.Count > 0 && existingDocuments.Count == 0)
        {
            await using var content = new MemoryStream(
                Encoding.UTF8.GetBytes("Mẫu đơn xác nhận tham gia câu lạc bộ (dữ liệu demo)."));

            var uploaded = await documents.UploadAsync(
                clubId,
                types[0].Id,
                "Mẫu đơn xác nhận tham gia",
                StudentUsername,
                DocumentVisibility.Public,
                content,
                "mau-don.txt",
                "system",
                cancellationToken).ConfigureAwait(false);

            if (uploaded.IsSuccess && uploaded.Value is not null)
            {
                await documents.ReviewAsync(
                    uploaded.Value.Id, true, null, "system", cancellationToken).ConfigureAwait(false);
            }
        }

        var existingRequests = await documentRequests
            .ListByClubAsync(clubId, cancellationToken)
            .ConfigureAwait(false);

        if (types.Count > 0 && existingRequests.Count == 0)
        {
            var student = await users.FindByUsernameAsync(StudentUsername, cancellationToken).ConfigureAwait(false);
            if (student is not null)
            {
                await documentRequests.CreateAsync(
                    clubId,
                    types[0].Id,
                    DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(7),
                    new[] { student.Id },
                    "system",
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SeedDisciplinesAsync(
        IUserRepository users,
        IDisciplineService disciplines,
        IReadOnlyList<Club> clubs,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (clubs.Count == 0)
        {
            return;
        }

        var clubId = clubs[0].Id;
        var types = await disciplines.ListDisciplinesAsync(clubId, false, cancellationToken).ConfigureAwait(false);

        if (types.Count == 0)
        {
            await disciplines.CreateDisciplineAsync(
                clubId, "Tham gia đầy đủ hoạt động", 10m,
                "Cộng điểm rèn luyện khi tham gia đủ hoạt động.", "system", cancellationToken)
                .ConfigureAwait(false);
            await disciplines.CreateDisciplineAsync(
                clubId, "Vắng không phép", -5m,
                "Trừ điểm rèn luyện khi vắng không phép.", "system", cancellationToken)
                .ConfigureAwait(false);

            types = await disciplines
                .ListDisciplinesAsync(clubId, false, cancellationToken)
                .ConfigureAwait(false);
        }

        var records = await disciplines.ListRecordsAsync(clubId, null, false, cancellationToken).ConfigureAwait(false);
        if (records.Count > 0 || types.Count < 2)
        {
            return;
        }

        var student = await users.FindByUsernameAsync(StudentUsername, cancellationToken).ConfigureAwait(false);
        var faculty = await users.FindByUsernameAsync(FacultyUsername, cancellationToken).ConfigureAwait(false);
        var merit = types.First(discipline => discipline.Points > 0m);
        var penalty = types.First(discipline => discipline.Points < 0m);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        if (student is not null)
        {
            await disciplines.ApplyRecordAsync(
                clubId, student.Id, merit.Id, today.AddDays(-10), "HK1 2026-2027",
                "Tham gia đủ 5/5 hoạt động.", "system", cancellationToken).ConfigureAwait(false);
            await disciplines.ApplyRecordAsync(
                clubId, student.Id, penalty.Id, today.AddDays(-3), "HK1 2026-2027",
                "Vắng buổi sinh hoạt tháng 10.", "system", cancellationToken).ConfigureAwait(false);
        }

        if (faculty is not null)
        {
            await disciplines.ApplyRecordAsync(
                clubId, faculty.Id, merit.Id, today.AddDays(-10), "HK1 2026-2027",
                "Điều hành hoạt động CLB.", "system", cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedAwardsAsync(
        IUserRepository users,
        IAwardService awards,
        IReadOnlyList<Club> clubs,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (clubs.Count == 0)
        {
            return;
        }

        var clubId = clubs[0].Id;
        var existing = await awards.ListAsync(clubId, cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return;
        }

        var student = await users.FindByUsernameAsync(StudentUsername, cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var created = await awards.CreateAsync(
            clubId,
            "Thành viên xuất sắc học kỳ",
            "HK1 2026-2027",
            today,
            AwardStatus.Awarded,
            "Giải thưởng demo cho thành viên tích cực.",
            "system",
            cancellationToken).ConfigureAwait(false);

        if (created.IsSuccess && created.Value is not null && student is not null)
        {
            await awards.AddRecipientAsync(
                created.Value.Id,
                student.Id,
                attachment: null,
                fileName: null,
                actor: "system",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedEngagementAsync(
        IUserRepository users,
        ILuckyDrawService luckyDraws,
        INotificationService notifications,
        ISettingsService settings,
        IReadOnlyList<Club> clubs,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (clubs.Count == 0)
        {
            return;
        }

        var clubId = clubs[0].Id;
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var globalSettings = await settings.ListAsync(null, cancellationToken).ConfigureAwait(false);
        if (globalSettings.Count == 0)
        {
            await settings.SetAsync(
                null, SettingKeys.SiteTitle, "SIMS — Quản lý câu lạc bộ", "system", cancellationToken)
                .ConfigureAwait(false);
            await settings.SetAsync(
                null, SettingKeys.SiteDescription,
                "Hệ thống quản lý sinh viên và câu lạc bộ (bản ASP.NET Core).", "system", cancellationToken)
                .ConfigureAwait(false);
            await settings.SetAsync(null, SettingKeys.SiteTheme, "light", "system", cancellationToken)
                .ConfigureAwait(false);
            await settings.SetAsync(null, SettingKeys.SitePrimaryColor, "#0d6efd", "system", cancellationToken)
                .ConfigureAwait(false);
            await settings.SetAsync(
                null, SettingKeys.SiteSupportEmail, "support@example.com", "system", cancellationToken)
                .ConfigureAwait(false);
        }

        var clubSettings = await settings.ListAsync(clubId, cancellationToken).ConfigureAwait(false);
        if (clubSettings.Count == 0)
        {
            await settings.SetAsync(
                clubId, SettingKeys.ClubContactEmail, "clb01@example.com", "system", cancellationToken)
                .ConfigureAwait(false);
            await settings.SetAsync(
                clubId, SettingKeys.ClubContactPhone, "0900000000", "system", cancellationToken)
                .ConfigureAwait(false);
            await settings.SetAsync(
                clubId, SettingKeys.ClubAddress, "Cơ sở BTEC FPT Đà Nẵng", "system", cancellationToken)
                .ConfigureAwait(false);
        }

        var draws = await luckyDraws.ListAsync(clubId, cancellationToken).ConfigureAwait(false);
        if (draws.Count == 0)
        {
            var student = await users.FindByUsernameAsync(StudentUsername, cancellationToken).ConfigureAwait(false);
            var faculty = await users.FindByUsernameAsync(FacultyUsername, cancellationToken).ConfigureAwait(false);
            var participants = new List<string>();

            if (student is not null)
            {
                participants.Add(student.DisplayName);
            }

            if (faculty is not null)
            {
                participants.Add(faculty.DisplayName);
            }

            participants.Add("Khách mời demo");

            var created = await luckyDraws.CreateAsync(
                clubId,
                "Bốc thăm may mắn tháng 11",
                "123456",
                participants,
                now.AddHours(-1),
                now.AddDays(30),
                isActive: true,
                "system",
                cancellationToken).ConfigureAwait(false);

            if (created.IsSuccess && created.Value is not null)
            {
                await luckyDraws.AddPrizeAsync(
                    created.Value.Id, "Giải nhất", 1, isSpecial: true, "system", cancellationToken)
                    .ConfigureAwait(false);
                await luckyDraws.AddPrizeAsync(
                    created.Value.Id, "Giải khuyến khích", 2, isSpecial: false, "system", cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var notificationRows = await notifications
            .ListForManagementAsync(clubId, cancellationToken)
            .ConfigureAwait(false);

        if (notificationRows.Count == 0)
        {
            await notifications.CreateAsync(
                "Chào mừng đến với SIMS",
                "Hệ thống quản lý câu lạc bộ đã sẵn sàng. Vui lòng cập nhật hồ sơ và theo dõi thông báo thường xuyên.",
                targetAllClubs: true,
                Array.Empty<Guid>(),
                sender: "system",
                actor: "system",
                cancellationToken).ConfigureAwait(false);

            await notifications.CreateAsync(
                "Sinh hoạt CLB tháng 11",
                $"Buổi sinh hoạt diễn ra lúc 18:00 ngày {today.AddDays(5):dd/MM/yyyy} tại hội trường. Điểm danh đúng giờ.",
                targetAllClubs: false,
                new[] { clubId },
                sender: "system",
                actor: "system",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedSampleImportFileAsync(StoragePaths storage, CancellationToken cancellationToken)
    {
        var path = storage.InvalidStudentsSampleFile;
        if (File.Exists(path))
        {
            return;
        }

        await File.WriteAllTextAsync(path, InvalidImportSample, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }
}
