using SIMS.Domain.Security;

namespace SIMS.Application.Security;

/// <summary>
/// Danh mục module/action của hệ thống (rút từ inventory PHP: 29 module, thu gọn còn các module đã port).
/// Ma trận mặc định: Admin toàn quyền, Faculty quản lý học vụ, Student chỉ xem hồ sơ của mình.
/// </summary>
public static class PermissionCatalog
{
    public const string Dashboard = "dashboard";
    public const string Students = "students";
    public const string Courses = "courses";
    public const string Enrolments = "enrolments";
    public const string Clubs = "clubs";
    public const string Learning = "learning";
    public const string Events = "events";
    public const string Checkin = "checkin";
    public const string EventPlans = "event_plans";
    public const string Invoices = "invoices";
    public const string Expenses = "expenses";
    public const string Documents = "documents";
    public const string Discipline = "discipline";
    public const string Awards = "awards";
    public const string LuckyDraws = "lucky_draws";
    public const string Notifications = "notifications";
    public const string Settings = "settings";
    public const string ShortUrls = "short_urls";
    public const string FeatureRequests = "feature_requests";
    public const string Logs = "logs";

    public static IReadOnlyList<string> Modules { get; } = new[]
    {
        Dashboard, Students, Courses, Enrolments, Clubs, Learning, Events, Checkin, EventPlans, Invoices, Expenses, Documents, Discipline, Awards, LuckyDraws, Notifications, Settings, ShortUrls, FeatureRequests, Logs,
    };

    public static IReadOnlyList<(string Module, string Action)> Actions { get; } = new[]
    {
        (Dashboard, "view"),
        (Students, "view"),
        (Students, "create"),
        (Students, "edit"),
        (Students, "delete"),
        (Students, "import"),
        (Courses, "view"),
        (Courses, "create"),
        (Courses, "edit"),
        (Courses, "delete"),
        (Enrolments, "view"),
        (Enrolments, "create"),
        (Enrolments, "grade"),
        (Enrolments, "drop"),
        (Clubs, "view"),
        (Clubs, "create"),
        (Clubs, "edit"),
        (Clubs, "delete"),
        (Learning, "view_own"),
        (Events, "view"),
        (Events, "create"),
        (Events, "edit"),
        (Events, "delete"),
        (Events, "publish"),
        (Events, "checkin"),
        (Checkin, "create"),
        (EventPlans, "view"),
        (EventPlans, "edit"),
        (Invoices, "view"),
        (Invoices, "create"),
        (Invoices, "edit"),
        (Invoices, "delete"),
        (Invoices, "mark_paid"),
        (Invoices, "my_invoices"),
        (Expenses, "view"),
        (Expenses, "create"),
        (Expenses, "edit"),
        (Expenses, "delete"),
        (Expenses, "approve"),
        (Expenses, "reject"),
        (Expenses, "my_expenses"),
        (Expenses, "my_create"),
        (Expenses, "my_edit"),
        (Expenses, "my_delete"),
        (Documents, "view"),
        (Documents, "create"),
        (Documents, "edit"),
        (Documents, "delete"),
        (Documents, "approve"),
        (Documents, "my_documents"),
        (Documents, "upload_own"),
        (Documents, "requests"),
        (Documents, "request_create"),
        (Discipline, "view"),
        (Discipline, "create"),
        (Discipline, "edit"),
        (Discipline, "delete"),
        (Discipline, "revoke"),
        (Discipline, "summary"),
        (Discipline, "my_points"),
        (Awards, "view"),
        (Awards, "create"),
        (Awards, "edit"),
        (Awards, "delete"),
        (LuckyDraws, "view"),
        (LuckyDraws, "create"),
        (LuckyDraws, "edit"),
        (LuckyDraws, "delete"),
        (LuckyDraws, "draw"),
        (Notifications, "view"),
        (Notifications, "create"),
        (Notifications, "edit"),
        (Notifications, "delete"),
        (Notifications, "inbox"),
        (Settings, "admin"),
        (Settings, "club"),
        (ShortUrls, "view"),
        (ShortUrls, "create"),
        (ShortUrls, "edit"),
        (ShortUrls, "delete"),
        (FeatureRequests, "view"),
        (FeatureRequests, "create"),
        (FeatureRequests, "manage"),
        (Logs, "view"),
    };

    public static IReadOnlyList<RolePermission> Defaults { get; } = BuildDefaults();

    /// <summary>Trả về các quyền mặc định còn thiếu (dùng khi nâng cấp dữ liệu cũ).</summary>
    public static IReadOnlyList<RolePermission> MissingDefaults(IEnumerable<RolePermission> existing)
    {
        var current = existing.ToArray();

        return Defaults
            .Where(defaultPermission => !current.Any(item =>
                item.Role == defaultPermission.Role &&
                string.Equals(item.Module, defaultPermission.Module, StringComparison.Ordinal) &&
                string.Equals(item.Action, defaultPermission.Action, StringComparison.Ordinal)))
            .ToArray();
    }

    private static IReadOnlyList<RolePermission> BuildDefaults()
    {
        var permissions = new List<RolePermission>();

        // Admin: toàn bộ.
        foreach (var (module, action) in Actions)
        {
            permissions.Add(RolePermission.Create(Role.Admin, module, action));
        }

        // Faculty: học vụ (không xoá sinh viên/học phần, không quản trị CLB).
        foreach (var (module, action) in new[]
        {
            (Dashboard, "view"),
            (Students, "view"), (Students, "create"), (Students, "edit"), (Students, "import"),
            (Courses, "view"), (Courses, "create"), (Courses, "edit"),
            (Enrolments, "view"), (Enrolments, "create"), (Enrolments, "grade"), (Enrolments, "drop"),
            (Clubs, "view"),
            (Events, "view"), (Events, "create"), (Events, "edit"), (Events, "publish"), (Events, "checkin"),
            (Checkin, "create"),
            (EventPlans, "view"), (EventPlans, "edit"),
            (Invoices, "view"), (Invoices, "create"), (Invoices, "edit"),
            (Invoices, "delete"), (Invoices, "mark_paid"),
            (Expenses, "view"), (Expenses, "create"), (Expenses, "edit"),
            (Expenses, "delete"), (Expenses, "approve"), (Expenses, "reject"),
            (Documents, "view"), (Documents, "create"), (Documents, "edit"),
            (Documents, "delete"), (Documents, "approve"),
            (Documents, "requests"), (Documents, "request_create"),
            (Discipline, "view"), (Discipline, "create"), (Discipline, "edit"),
            (Discipline, "delete"), (Discipline, "revoke"), (Discipline, "summary"),
            (Awards, "view"), (Awards, "create"), (Awards, "edit"), (Awards, "delete"),
            (LuckyDraws, "view"), (LuckyDraws, "create"), (LuckyDraws, "edit"),
            (LuckyDraws, "delete"), (LuckyDraws, "draw"),
            (Notifications, "view"), (Notifications, "create"), (Notifications, "edit"),
            (Notifications, "delete"), (Notifications, "inbox"),
            (Settings, "club"),
            (ShortUrls, "view"), (ShortUrls, "create"), (ShortUrls, "edit"), (ShortUrls, "delete"),
            (FeatureRequests, "view"), (FeatureRequests, "manage"),
        })
        {
            permissions.Add(RolePermission.Create(Role.Faculty, module, action));
        }

        // Student: xem dashboard, xem danh sách CLB (để chuyển CLB), hồ sơ học tập và check-in sự kiện.
        permissions.Add(RolePermission.Create(Role.Student, Dashboard, "view"));
        permissions.Add(RolePermission.Create(Role.Student, Clubs, "view"));
        permissions.Add(RolePermission.Create(Role.Student, Learning, "view_own"));
        permissions.Add(RolePermission.Create(Role.Student, Events, "view"));
        permissions.Add(RolePermission.Create(Role.Student, Checkin, "create"));
        permissions.Add(RolePermission.Create(Role.Student, Invoices, "my_invoices"));
        permissions.Add(RolePermission.Create(Role.Student, Expenses, "my_expenses"));
        permissions.Add(RolePermission.Create(Role.Student, Expenses, "my_create"));
        permissions.Add(RolePermission.Create(Role.Student, Expenses, "my_edit"));
        permissions.Add(RolePermission.Create(Role.Student, Expenses, "my_delete"));
        permissions.Add(RolePermission.Create(Role.Student, Documents, "my_documents"));
        permissions.Add(RolePermission.Create(Role.Student, Documents, "upload_own"));
        permissions.Add(RolePermission.Create(Role.Student, Discipline, "my_points"));
        permissions.Add(RolePermission.Create(Role.Student, Notifications, "inbox"));
        permissions.Add(RolePermission.Create(Role.Student, FeatureRequests, "view"));
        permissions.Add(RolePermission.Create(Role.Student, FeatureRequests, "create"));

        return permissions;
    }
}
