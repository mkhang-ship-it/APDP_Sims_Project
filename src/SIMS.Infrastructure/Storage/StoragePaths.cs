namespace SIMS.Infrastructure.Storage;

/// <summary>Đường dẫn dữ liệu CSV — mọi dữ liệu nằm trong một thư mục `data/` duy nhất.</summary>
public sealed class StoragePaths
{
    public StoragePaths(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("Thư mục dữ liệu không hợp lệ.", nameof(dataDirectory));
        }

        DataDirectory = Path.GetFullPath(dataDirectory);
    }

    public string DataDirectory { get; }

    public string UsersFile => Path.Combine(DataDirectory, "users.csv");

    public string StudentsFile => Path.Combine(DataDirectory, "students.csv");

    public string CoursesFile => Path.Combine(DataDirectory, "courses.csv");

    public string EnrolmentsFile => Path.Combine(DataDirectory, "enrolments.csv");

    public string ClubsFile => Path.Combine(DataDirectory, "clubs.csv");

    public string ClubMembershipsFile => Path.Combine(DataDirectory, "club_memberships.csv");

    public string PermissionsFile => Path.Combine(DataDirectory, "permissions.csv");

    public string EventsFile => Path.Combine(DataDirectory, "events.csv");

    public string AttendanceFile => Path.Combine(DataDirectory, "attendance.csv");

    public string EventPlansFile => Path.Combine(DataDirectory, "event_plans.csv");

    public string PlanObjectivesFile => Path.Combine(DataDirectory, "event_plan_objectives.csv");

    public string PlanActionsFile => Path.Combine(DataDirectory, "event_plan_actions.csv");

    public string PlanBudgetFile => Path.Combine(DataDirectory, "event_plan_budget_items.csv");

    public string PlanEquipmentFile => Path.Combine(DataDirectory, "event_plan_equipment.csv");

    public string PlanTimelineFile => Path.Combine(DataDirectory, "event_plan_timeline.csv");

    public string InvoicesFile => Path.Combine(DataDirectory, "invoices.csv");

    public string TransactionsFile => Path.Combine(DataDirectory, "transactions.csv");

    public string ExpensesFile => Path.Combine(DataDirectory, "expenses.csv");

    public string DocumentTypesFile => Path.Combine(DataDirectory, "document_types.csv");

    public string DocumentsFile => Path.Combine(DataDirectory, "documents.csv");

    public string DocumentAccessFile => Path.Combine(DataDirectory, "document_access.csv");

    public string DocumentRequestsFile => Path.Combine(DataDirectory, "document_requests.csv");

    public string DocumentRequestUsersFile => Path.Combine(DataDirectory, "document_request_users.csv");

    public string DisciplinesFile => Path.Combine(DataDirectory, "disciplines.csv");

    public string DisciplineRecordsFile => Path.Combine(DataDirectory, "discipline_records.csv");

    public string AwardsFile => Path.Combine(DataDirectory, "awards.csv");

    public string AwardRecipientsFile => Path.Combine(DataDirectory, "award_recipients.csv");

    public string LuckyDrawsFile => Path.Combine(DataDirectory, "lucky_draws.csv");

    public string LuckyPrizesFile => Path.Combine(DataDirectory, "lucky_prizes.csv");

    public string LuckyWinnersFile => Path.Combine(DataDirectory, "lucky_winners.csv");

    public string NotificationsFile => Path.Combine(DataDirectory, "notifications.csv");

    public string NotificationTargetsFile => Path.Combine(DataDirectory, "notification_targets.csv");

    public string NotificationReadsFile => Path.Combine(DataDirectory, "notification_reads.csv");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.csv");

    public string ShortUrlsFile => Path.Combine(DataDirectory, "short_urls.csv");

    public string ShortUrlVisitsFile => Path.Combine(DataDirectory, "short_url_visits.csv");

    public string EventImagesFile => Path.Combine(DataDirectory, "event_images.csv");

    public string FeatureRequestsFile => Path.Combine(DataDirectory, "feature_requests.csv");

    public string FilesDirectory => Path.Combine(DataDirectory, "files");

    public string AuditFile => Path.Combine(DataDirectory, "audit.csv");

    /// <summary>File mẫu có dòng lỗi để thử import thủ công.</summary>
    public string InvalidStudentsSampleFile => Path.Combine(DataDirectory, "students-invalid.csv");

    public void EnsureCreated() => Directory.CreateDirectory(DataDirectory);
}
