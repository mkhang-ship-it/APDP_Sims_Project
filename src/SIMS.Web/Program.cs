using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.WebEncoders;
using SIMS.Application.Abstractions;
using SIMS.Application.Checkin;
using SIMS.Application.Clubs;
using SIMS.Application.Courses;
using SIMS.Application.Dashboard;
using SIMS.Application.Enrolments;
using SIMS.Application.EventPlans;
using SIMS.Application.Events;
using SIMS.Application.Finance;
using SIMS.Application.Awards;
using SIMS.Application.Disciplines;
using SIMS.Application.Documents;
using SIMS.Application.LuckyDraws;
using SIMS.Application.Notifications;
using SIMS.Application.Settings;
using SIMS.Application.ShortUrls;
using SIMS.Application.FeatureRequests;
using SIMS.Application.Payments;
using SIMS.Application.Grades;
using SIMS.Application.Learning;
using SIMS.Application.Security;
using SIMS.Application.Students;
using SIMS.Application.Students.Import;
using SIMS.Application.Validation;
using SIMS.Domain.Events;
using SIMS.Infrastructure.Persistence;
using SIMS.Infrastructure.Security;
using SIMS.Infrastructure.Storage;
using SIMS.Infrastructure.Time;
using SIMS.Web.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks();

// Xuất tiếng Việt trực tiếp thay vì entity số (HTML nhỏ hơn, dễ đọc).
builder.Services.Configure<WebEncoderOptions>(options =>
{
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All);
});

// Kho dòng cho một bảng: SQL Server (mặc định) hoặc CSV, tuỳ Storage:Provider.
static IRowStore Stores(IServiceProvider sp, string table, params string[] columns)
    => sp.GetRequiredService<RowStoreFactory>().Create(table, columns);

// Lưu trữ: SQL Server là engine chính (Storage:Provider=SqlServer); CSV là chế độ phụ
// (Storage:Provider=Csv) để chạy offline/không cần engine. Repository không đổi — chỉ
// RowStoreFactory quyết định IRowStore. Resolve trong DI để test override được cấu hình.
builder.Services.AddSingleton<StoragePaths>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var environment = serviceProvider.GetRequiredService<IHostEnvironment>();

    var configured = configuration["Storage:DataDirectory"];
    var dataDirectory = string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(environment.ContentRootPath, "data")
        : Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);

    var paths = new StoragePaths(dataDirectory);

    // data/ luôn cần: nơi chứa file đính kèm + file mẫu import, kể cả khi dữ liệu nghiệp vụ ở SQL.
    paths.EnsureCreated();

    return paths;
});

builder.Services.AddSingleton<RowStoreFactory>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var paths = serviceProvider.GetRequiredService<StoragePaths>();

    return new RowStoreFactory(
        configuration["Storage:Provider"] ?? RowStoreFactory.SqlProvider,
        configuration.GetConnectionString("Sims") ?? configuration["Storage:ConnectionString"],
        paths.DataDirectory);
});

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

builder.Services.AddSingleton<IUserRepository>(
    sp => new CsvUserRepository(sp.GetRequiredService<RowStoreFactory>().Create(
        "users",
        "id", "username", "display_name", "role", "password_hash", "is_active", "created_at_utc", "email")));
builder.Services.AddSingleton<IStudentRepository>(
    sp => new CsvStudentRepository(Stores(sp, "students", "id", "student_code", "full_name", "email", "date_of_birth", "program", "status", "user_id", "club_id", "created_at_utc")));
builder.Services.AddSingleton<ICourseRepository>(
    sp => new CsvCourseRepository(Stores(sp, "courses", "id", "code", "title", "capacity", "prerequisite_code", "status", "created_at_utc")));
builder.Services.AddSingleton<IEnrolmentRepository>(
    sp => new CsvEnrolmentRepository(Stores(sp, "enrolments", "id", "student_id", "course_id", "status", "enrolled_at_utc", "score", "grade_letter")));
builder.Services.AddSingleton(
    sp => new CsvAuditLog(Stores(sp, "audit", "timestamp_utc", "actor", "action", "entity", "entity_id", "details")));
builder.Services.AddSingleton<IAuditLog>(sp => sp.GetRequiredService<CsvAuditLog>());
builder.Services.AddSingleton<IAuditLogReader>(sp => sp.GetRequiredService<CsvAuditLog>());
builder.Services.AddSingleton<IClubRepository>(
    sp => new CsvClubRepository(Stores(sp, "clubs", "id", "code", "name", "is_active", "created_at_utc")));
builder.Services.AddSingleton<IClubMembershipRepository>(
    sp => new CsvClubMembershipRepository(Stores(sp, "club_memberships", "id", "user_id", "club_id", "role", "is_active", "joined_at_utc")));
builder.Services.AddSingleton<IPermissionRepository>(
    sp => new CsvPermissionRepository(Stores(sp, "permissions", "role", "module", "action")));
builder.Services.AddSingleton<IEventRepository>(
    sp => new CsvEventRepository(Stores(sp, "events", "id", "code", "title", "description", "starts_at_utc", "ends_at_utc", "location", "capacity", "status", "club_id", "checkin_token", "created_at_utc", "venue_latitude", "venue_longitude")));
builder.Services.AddSingleton<IAttendanceRepository>(
    sp => new CsvAttendanceRepository(Stores(sp, "attendance", "id", "event_id", "student_id", "method", "checked_in_at_utc", "latitude", "longitude", "distance_km")));
builder.Services.AddSingleton<IEventPlanRepository>(
    sp => new CsvEventPlanRepository(Stores(sp, "event_plans", "id", "event_id", "summary", "created_at_utc", "updated_at_utc", "prepared_by", "implementation_date", "approved_by", "participants", "description", "responsible_organizer", "advisor_coordinator")));
builder.Services.AddSingleton<IPlanItemRepository<PlanObjective>>(
    sp => new CsvPlanObjectiveRepository(Stores(sp, "event_plan_objectives", "id", "event_plan_id", "content", "order")));
builder.Services.AddSingleton<IPlanItemRepository<PlanAction>>(
    sp => new CsvPlanActionRepository(Stores(sp, "event_plan_actions", "id", "event_plan_id", "content", "owner", "due_date", "order")));
builder.Services.AddSingleton<IPlanItemRepository<PlanBudgetItem>>(
    sp => new CsvPlanBudgetItemRepository(Stores(sp, "event_plan_budget_items", "id", "event_plan_id", "item_name", "quantity", "unit_price", "order")));
builder.Services.AddSingleton<IPlanItemRepository<PlanEquipment>>(
    sp => new CsvPlanEquipmentRepository(Stores(sp, "event_plan_equipment", "id", "event_plan_id", "name", "quantity", "note", "order")));
builder.Services.AddSingleton<IPlanItemRepository<PlanTimelineItem>>(
    sp => new CsvPlanTimelineRepository(Stores(sp, "event_plan_timeline", "id", "event_plan_id", "time_label", "activity", "order")));
builder.Services.AddSingleton<IInvoiceRepository>(
    sp => new CsvInvoiceRepository(Stores(sp, "invoices", "id", "invoice_code", "user_id", "club_id", "name", "total_amount", "due_date", "status", "period", "note", "created_at_utc", "updated_at_utc")));
builder.Services.AddSingleton<ITransactionRepository>(
    sp => new CsvTransactionRepository(Stores(sp, "transactions", "id", "invoice_id", "expense_id", "type", "paid_amount", "payment_date", "payment_method", "note", "club_id", "created_at_utc")));
builder.Services.AddSingleton<IExpenseRepository>(
    sp => new CsvExpenseRepository(Stores(sp, "expenses", "id", "name", "amount", "requested_by", "approved_by", "approved_at_utc", "requested_at_utc", "status", "club_id", "period", "description", "created_at_utc", "updated_at_utc")));
builder.Services.AddSingleton<IDocumentTypeRepository>(
    sp => new CsvDocumentTypeRepository(Stores(sp, "document_types", "id", "club_id", "code", "name", "description", "template_url", "is_active", "created_at_utc")));
builder.Services.AddSingleton<IDocumentRepository>(
    sp => new CsvDocumentRepository(Stores(sp, "documents", "id", "club_id", "document_type_id", "uploaded_by", "title", "file_path", "visibility", "status", "reason_rejected", "reviewed_by", "reviewed_at_utc", "created_at_utc")));
builder.Services.AddSingleton<IDocumentAccessRepository>(
    sp => new CsvDocumentAccessRepository(Stores(sp, "document_access", "id", "document_id", "user_id")));
builder.Services.AddSingleton<IDocumentRequestRepository>(
    sp => new CsvDocumentRequestRepository(Stores(sp, "document_requests", "id", "club_id", "document_type_id", "requested_by", "deadline", "created_at_utc")));
builder.Services.AddSingleton<IDocumentRequestUserRepository>(
    sp => new CsvDocumentRequestUserRepository(Stores(sp, "document_request_users", "id", "document_request_id", "user_id", "document_id", "submitted_at_utc")));
builder.Services.AddSingleton<IDisciplineRepository>(
    sp => new CsvDisciplineRepository(Stores(sp, "disciplines", "id", "club_id", "code", "name", "points", "description", "is_active", "created_at_utc")));
builder.Services.AddSingleton<IDisciplineRecordRepository>(
    sp => new CsvDisciplineRecordRepository(Stores(sp, "discipline_records", "id", "user_id", "club_id", "discipline_id", "applied_date", "period", "status", "applied_by", "notes", "created_at_utc")));
builder.Services.AddSingleton<IAwardRepository>(
    sp => new CsvAwardRepository(Stores(sp, "awards", "id", "club_id", "code", "title", "period", "award_date", "status", "notes", "created_at_utc")));
builder.Services.AddSingleton<IAwardRecipientRepository>(
    sp => new CsvAwardRecipientRepository(Stores(sp, "award_recipients", "id", "award_id", "user_id", "attachment", "created_at_utc")));
builder.Services.AddSingleton<IFileStorage>(
    sp => new LocalFileStorage(sp.GetRequiredService<StoragePaths>().FilesDirectory));
builder.Services.AddSingleton<ILuckyDrawRepository>(
    sp => new CsvLuckyDrawRepository(Stores(sp, "lucky_draws", "id", "club_id", "title", "slug", "security_code", "participants", "start_at_utc", "end_at_utc", "is_active", "created_by", "created_at_utc")));
builder.Services.AddSingleton<ILuckyPrizeRepository>(
    sp => new CsvLuckyPrizeRepository(Stores(sp, "lucky_prizes", "id", "lucky_draw_id", "name", "quantity", "order", "is_special")));
builder.Services.AddSingleton<ILuckyWinnerRepository>(
    sp => new CsvLuckyWinnerRepository(Stores(sp, "lucky_winners", "id", "lucky_draw_id", "participant", "prize_name", "won_at_utc")));
builder.Services.AddSingleton<INotificationRepository>(
    sp => new CsvNotificationRepository(Stores(sp, "notifications", "id", "title", "content", "target_type", "sender", "is_active", "created_at_utc", "updated_at_utc")));
builder.Services.AddSingleton<INotificationTargetRepository>(
    sp => new CsvNotificationTargetRepository(Stores(sp, "notification_targets", "id", "notification_id", "club_id")));
builder.Services.AddSingleton<INotificationReadRepository>(
    sp => new CsvNotificationReadRepository(Stores(sp, "notification_reads", "id", "notification_id", "user_id", "read_at_utc")));
builder.Services.AddSingleton<ISettingRepository>(
    sp => new CsvSettingRepository(Stores(sp, "settings", "id", "club_id", "key", "value", "updated_at_utc")));
builder.Services.AddSingleton<IShortUrlRepository>(
    sp => new CsvShortUrlRepository(Stores(sp, "short_urls", "id", "club_id", "code", "target_url", "title", "created_by", "expires_at_utc", "is_active", "created_at_utc")));
builder.Services.AddSingleton<IShortUrlVisitRepository>(
    sp => new CsvShortUrlVisitRepository(Stores(sp, "short_url_visits", "id", "short_url_id", "visited_at_utc", "ip_address", "user_agent")));
builder.Services.AddSingleton<IEventImageRepository>(
    sp => new CsvEventImageRepository(Stores(sp, "event_images", "id", "event_id", "file_path", "caption", "uploaded_by", "created_at_utc")));
builder.Services.AddSingleton<IFeatureRequestRepository>(
    sp => new CsvFeatureRequestRepository(Stores(sp, "feature_requests", "id", "club_id", "title", "content", "created_by", "status", "response", "created_at_utc", "updated_at_utc")));

// ISP: cùng một instance nhưng tách interface đọc/ghi.
builder.Services.AddSingleton<IStudentReader>(sp => sp.GetRequiredService<IStudentRepository>());
builder.Services.AddSingleton<IStudentWriter>(sp => sp.GetRequiredService<IStudentRepository>());

builder.Services.AddSingleton<IStudentImportParser, CsvStudentImportParser>();

builder.Services.AddSingleton<IValidator<StudentInput>, StudentValidator>();
builder.Services.AddSingleton<IValidator<UserInput>, UserInputValidator>();
builder.Services.AddSingleton<IValidator<CourseInput>, CourseValidator>();
builder.Services.AddSingleton<IValidator<ClubInput>, ClubValidator>();
builder.Services.AddSingleton<IValidator<EventInput>, EventValidator>();

builder.Services.AddSingleton<IPrerequisitePolicy, CompletedCoursePrerequisitePolicy>();
builder.Services.AddSingleton<IGradePolicy, TenPointGradePolicy>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IClubService, ClubService>();
builder.Services.AddScoped<IMyLearningService, MyLearningService>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<ICheckinService, CheckinService>();
builder.Services.AddScoped<IEventCalendarService, EventCalendarService>();
builder.Services.AddScoped<IEventPlanService, EventPlanService>();
builder.Services.AddSingleton<IInvoiceCodeGenerator, RandomInvoiceCodeGenerator>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddSingleton<IReferenceCodeGenerator, RandomReferenceCodeGenerator>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IDocumentRequestService, DocumentRequestService>();
builder.Services.AddScoped<IDisciplineService, DisciplineService>();
builder.Services.AddScoped<IAwardService, AwardService>();
builder.Services.AddScoped<ILuckyDrawService, LuckyDrawService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IShortUrlService, ShortUrlService>();
builder.Services.AddScoped<IEventImageService, EventImageService>();
builder.Services.AddScoped<IFeatureRequestService, FeatureRequestService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".sims.session";
});

builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEnrolmentService, EnrolmentService>();
builder.Services.AddScoped<IGradeService, GradeService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

// DIP + Decorator: service nghiệp vụ thuần, audit bọc bên ngoài.
builder.Services.AddScoped<CourseService>();
builder.Services.AddScoped<ICourseService>(sp => new AuditedCourseService(
    sp.GetRequiredService<CourseService>(),
    sp.GetRequiredService<IAuditLog>(),
    sp.GetRequiredService<IClock>()));

builder.Services.AddScoped<StudentImportService>();
builder.Services.AddScoped<IStudentImportService>(sp => new AuditedStudentImportService(
    sp.GetRequiredService<StudentImportService>(),
    sp.GetRequiredService<IAuditLog>(),
    sp.GetRequiredService<IClock>()));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "sims.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Chuẩn bị hạ tầng lưu trữ (schema SQL Server hoặc thư mục CSV) trước khi seed.
await app.Services.GetRequiredService<RowStoreFactory>().EnsureCreatedAsync();

// Seed dữ liệu demo khi CSV còn rỗng (idempotent).
await using (var scope = app.Services.CreateAsyncScope())
{
    var provider = scope.ServiceProvider;

    await DefaultDataSeeder.SeedAsync(
        provider.GetRequiredService<IUserRepository>(),
        provider.GetRequiredService<IStudentRepository>(),
        provider.GetRequiredService<ICourseRepository>(),
        provider.GetRequiredService<IEnrolmentRepository>(),
        provider.GetRequiredService<IClubRepository>(),
        provider.GetRequiredService<IClubMembershipRepository>(),
        provider.GetRequiredService<IPermissionRepository>(),
        provider.GetRequiredService<IEventRepository>(),
        provider.GetRequiredService<IEventPlanService>(),
        provider.GetRequiredService<IInvoiceService>(),
        provider.GetRequiredService<IExpenseService>(),
        provider.GetRequiredService<IDocumentService>(),
        provider.GetRequiredService<IDocumentRequestService>(),
        provider.GetRequiredService<IDisciplineService>(),
        provider.GetRequiredService<IAwardService>(),
        provider.GetRequiredService<ILuckyDrawService>(),
        provider.GetRequiredService<INotificationService>(),
        provider.GetRequiredService<ISettingsService>(),
        provider.GetRequiredService<IAuditLog>(),
        provider.GetRequiredService<StoragePaths>(),
        provider.GetRequiredService<IPasswordHasher>(),
        provider.GetRequiredService<IClock>());
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseMiddleware<MaintenanceMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHealthChecks("/health");

app.Run();

/// <summary>Marker cho WebApplicationFactory trong test tích hợp.</summary>
public partial class Program
{
}
