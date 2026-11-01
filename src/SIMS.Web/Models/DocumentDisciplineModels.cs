using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using SIMS.Application.Awards;
using SIMS.Application.Disciplines;
using SIMS.Application.Documents;
using SIMS.Domain.Awards;
using SIMS.Domain.Disciplines;
using SIMS.Domain.Documents;
using SIMS.Domain.Security;

namespace SIMS.Web.Models;

public sealed class DocumentTypeFormModel
{
    [Required(ErrorMessage = "Tên loại văn bản là bắt buộc.")]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(500)]
    public string? TemplateUrl { get; set; }

    public static DocumentTypeFormModel FromType(DocumentType type)
        => new() { Name = type.Name, Description = type.Description, TemplateUrl = type.TemplateUrl };
}

public sealed class DocumentUploadFormModel
{
    public Guid ClubId { get; set; }

    [Required(ErrorMessage = "Chọn loại văn bản.")]
    public Guid DocumentTypeId { get; set; }

    [Required(ErrorMessage = "Tiêu đề là bắt buộc.")]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    public DocumentVisibility Visibility { get; set; } = DocumentVisibility.Private;

    public IFormFile? File { get; set; }
}

public sealed class DocumentListPageModel
{
    public IReadOnlyList<DocumentListItem> Items { get; init; } = Array.Empty<DocumentListItem>();

    public IReadOnlyList<DocumentType> Types { get; init; } = Array.Empty<DocumentType>();

    public IReadOnlyDictionary<Guid, string> UserNames { get; init; } = new Dictionary<Guid, string>();

    public string? Status { get; init; }

    public Guid? DocumentTypeId { get; init; }
}

public sealed class DocumentCreatePageModel
{
    public DocumentUploadFormModel Form { get; init; } = new();

    public IReadOnlyList<DocumentType> Types { get; init; } = Array.Empty<DocumentType>();

    public IReadOnlyList<ClubOption> Clubs { get; init; } = Array.Empty<ClubOption>();

    public bool IsAdmin { get; init; }
}

public sealed record ClubOption(Guid Id, string Code, string Name);

public sealed class DocumentDetailsPageModel
{
    public Document Document { get; init; } = null!;

    public DocumentType? Type { get; init; }

    public IReadOnlyList<DocumentAccess> Access { get; init; } = Array.Empty<DocumentAccess>();

    public IReadOnlyList<User> Members { get; init; } = Array.Empty<User>();

    public IReadOnlyDictionary<Guid, string> UserNames { get; init; } = new Dictionary<Guid, string>();
}

public sealed class MyDocumentsPageModel
{
    public IReadOnlyList<DocumentListItem> Items { get; init; } = Array.Empty<DocumentListItem>();

    public IReadOnlyList<DocumentType> Types { get; init; } = Array.Empty<DocumentType>();

    public DocumentUploadFormModel Form { get; init; } = new();
}

public sealed class DocumentRequestFormModel
{
    public Guid ClubId { get; set; }

    [Required(ErrorMessage = "Chọn loại văn bản.")]
    public Guid DocumentTypeId { get; set; }

    [Required(ErrorMessage = "Hạn nộp là bắt buộc.")]
    public DateOnly Deadline { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(7));

    public List<Guid> UserIds { get; set; } = new();
}

public sealed class DocumentRequestsPageModel
{
    public IReadOnlyList<DocumentRequestRow> Rows { get; init; } = Array.Empty<DocumentRequestRow>();

    public IReadOnlyList<DocumentType> Types { get; init; } = Array.Empty<DocumentType>();

    public IReadOnlyList<User> Members { get; init; } = Array.Empty<User>();

    public IReadOnlyDictionary<Guid, string> UserNames { get; init; } = new Dictionary<Guid, string>();
}

public sealed class MyDocumentRequestsPageModel
{
    public IReadOnlyList<DocumentRequestItem> Items { get; init; } = Array.Empty<DocumentRequestItem>();
}

public sealed class DisciplineFormModel
{
    [Required(ErrorMessage = "Tên danh mục là bắt buộc.")]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [Range(-1000, 1000, ErrorMessage = "Điểm phải từ -1000 đến 1000.")]
    public decimal Points { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    public static DisciplineFormModel FromDiscipline(Discipline discipline)
        => new() { Name = discipline.Name, Points = discipline.Points, Description = discipline.Description };
}

public sealed class DisciplinePageModel
{
    public IReadOnlyList<Discipline> Items { get; init; } = Array.Empty<Discipline>();
}

public sealed class DisciplineRecordFormModel
{
    public Guid UserId { get; set; }

    public Guid DisciplineId { get; set; }

    public DateOnly AppliedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [StringLength(40)]
    public string? Period { get; set; } = "HK1 2026-2027";

    [StringLength(500)]
    public string? Notes { get; set; }
}

public sealed class DisciplineRecordsPageModel
{
    public IReadOnlyList<DisciplineRecordItem> Items { get; init; } = Array.Empty<DisciplineRecordItem>();

    public IReadOnlyList<Discipline> Disciplines { get; init; } = Array.Empty<Discipline>();

    public IReadOnlyList<User> Members { get; init; } = Array.Empty<User>();

    public IReadOnlyDictionary<Guid, string> UserNames { get; init; } = new Dictionary<Guid, string>();

    public DisciplineRecordFormModel Form { get; init; } = new();
}

public sealed class DisciplineSummaryPageModel
{
    public IReadOnlyList<MemberPointRow> Rows { get; init; } = Array.Empty<MemberPointRow>();

    public IReadOnlyDictionary<Guid, string> UserNames { get; init; } = new Dictionary<Guid, string>();
}

public sealed class MyPointsPageModel
{
    public UserPointsView Points { get; init; } = null!;

    public string ClubName { get; init; } = "?";
}

public sealed class AwardFormModel
{
    [Required(ErrorMessage = "Tên giải thưởng là bắt buộc.")]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [StringLength(40)]
    public string? Period { get; set; } = "HK1 2026-2027";

    public DateOnly AwardDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public AwardStatus Status { get; set; } = AwardStatus.Pending;

    [StringLength(1000)]
    public string? Notes { get; set; }

    public static AwardFormModel FromAward(Award award)
        => new()
        {
            Title = award.Title,
            Period = award.Period,
            AwardDate = award.AwardDate,
            Status = award.Status,
            Notes = award.Notes,
        };
}

public sealed class AwardPageModel
{
    public IReadOnlyList<AwardListItem> Items { get; init; } = Array.Empty<AwardListItem>();

    public IReadOnlyDictionary<Guid, string> UserNames { get; init; } = new Dictionary<Guid, string>();
}

public sealed class AwardDetailsPageModel
{
    public Award Award { get; init; } = null!;

    public IReadOnlyList<AwardRecipient> Recipients { get; init; } = Array.Empty<AwardRecipient>();

    public IReadOnlyList<User> AvailableMembers { get; init; } = Array.Empty<User>();

    public IReadOnlyDictionary<Guid, string> UserNames { get; init; } = new Dictionary<Guid, string>();
}
