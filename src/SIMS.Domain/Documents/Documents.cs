using System.Text.RegularExpressions;
using SIMS.Domain.Common;

namespace SIMS.Domain.Documents;

public enum DocumentVisibility
{
    Public,
    Private,
    Restricted,
}

public enum DocumentStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>Loại văn bản của CLB (mã DT-######).</summary>
public sealed class DocumentType : Entity
{
    private static readonly Regex CodePattern = new(@"^DT-\d{6}$", RegexOptions.Compiled);

    private DocumentType()
    {
        Code = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
        TemplateUrl = string.Empty;
    }

    public Guid ClubId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    /// <summary>Liên kết file mẫu (không lưu nhị phân trong CSV).</summary>
    public string TemplateUrl { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static DocumentType Create(
        Guid clubId,
        string code,
        string name,
        string? description,
        string? templateUrl,
        DateTimeOffset now)
    {
        if (clubId == Guid.Empty)
        {
            throw new DomainException("CLB không hợp lệ.");
        }

        return new DocumentType
        {
            ClubId = clubId,
            Code = RequireCode(code),
            Name = RequireText(name, 255, "Tên loại văn bản"),
            Description = OptionalText(description, 1000, "Mô tả"),
            TemplateUrl = OptionalText(templateUrl, 500, "Liên kết mẫu"),
            IsActive = true,
            CreatedAtUtc = now,
        };
    }

    public static DocumentType Restore(
        Guid id,
        Guid clubId,
        string code,
        string name,
        string? description,
        string? templateUrl,
        bool isActive,
        DateTimeOffset createdAtUtc)
    {
        var type = Create(clubId, code, name, description, templateUrl, createdAtUtc);
        type.Id = id;
        type.IsActive = isActive;

        return type;
    }

    public void Update(string name, string? description, string? templateUrl)
    {
        Name = RequireText(name, 255, "Tên loại văn bản");
        Description = OptionalText(description, 1000, "Mô tả");
        TemplateUrl = OptionalText(templateUrl, 500, "Liên kết mẫu");
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private static string RequireCode(string code)
    {
        var value = (code ?? string.Empty).Trim();

        return CodePattern.IsMatch(value)
            ? value
            : throw new DomainException("Mã loại văn bản phải có dạng DT-NNNNNN.");
    }

    private static string RequireText(string value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new DomainException($"{fieldName} là bắt buộc.");
        }

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text;
    }

    private static string OptionalText(string? value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }
}

/// <summary>Văn bản/tài liệu của CLB với file đính kèm và luồng duyệt.</summary>
public sealed class Document : Entity
{
    private Document()
    {
        Title = string.Empty;
        FilePath = string.Empty;
        UploadedBy = string.Empty;
        ReasonRejected = string.Empty;
        ReviewedBy = string.Empty;
    }

    public Guid ClubId { get; private set; }

    public Guid DocumentTypeId { get; private set; }

    public string UploadedBy { get; private set; }

    public string Title { get; private set; }

    /// <summary>Khoá tương đối trong kho file (ví dụ documents/abc.pdf).</summary>
    public string FilePath { get; private set; }

    public DocumentVisibility Visibility { get; private set; }

    public DocumentStatus Status { get; private set; }

    public string ReasonRejected { get; private set; }

    public string ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Document Create(
        Guid clubId,
        Guid documentTypeId,
        string uploadedBy,
        string title,
        string filePath,
        DocumentVisibility visibility,
        DocumentStatus status,
        DateTimeOffset now)
    {
        if (clubId == Guid.Empty || documentTypeId == Guid.Empty)
        {
            throw new DomainException("CLB hoặc loại văn bản không hợp lệ.");
        }

        return new Document
        {
            ClubId = clubId,
            DocumentTypeId = documentTypeId,
            UploadedBy = RequireActor(uploadedBy),
            Title = RequireText(title, 255, "Tiêu đề văn bản"),
            FilePath = RequireText(filePath, 500, "Tệp đính kèm"),
            Visibility = visibility,
            Status = status,
            ReasonRejected = string.Empty,
            ReviewedBy = string.Empty,
            CreatedAtUtc = now,
        };
    }

    public static Document Restore(
        Guid id,
        Guid clubId,
        Guid documentTypeId,
        string uploadedBy,
        string title,
        string filePath,
        DocumentVisibility visibility,
        DocumentStatus status,
        string? reasonRejected,
        string? reviewedBy,
        DateTimeOffset? reviewedAtUtc,
        DateTimeOffset createdAtUtc)
    {
        var document = Create(
            clubId, documentTypeId, uploadedBy, title, filePath, visibility, status, createdAtUtc);
        document.Id = id;

        var reason = (reasonRejected ?? string.Empty).Trim();
        if (reason.Length > 1000)
        {
            throw new DomainException("Lý do từ chối tối đa 1000 ký tự.");
        }

        document.ReasonRejected = reason;
        document.ReviewedBy = (reviewedBy ?? string.Empty).Trim();
        document.ReviewedAtUtc = reviewedAtUtc;

        return document;
    }

    public void Approve(string actor, DateTimeOffset now)
    {
        Status = DocumentStatus.Approved;
        ReviewedBy = RequireActor(actor);
        ReviewedAtUtc = now;
        ReasonRejected = string.Empty;
    }

    public void Reject(string actor, string? reason, DateTimeOffset now)
    {
        var text = (reason ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new DomainException("Cần nêu lý do từ chối.");
        }

        if (text.Length > 1000)
        {
            throw new DomainException("Lý do từ chối tối đa 1000 ký tự.");
        }

        Status = DocumentStatus.Rejected;
        ReasonRejected = text
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        ReviewedBy = RequireActor(actor);
        ReviewedAtUtc = now;
    }

    public void SetVisibility(DocumentVisibility visibility)
        => Visibility = visibility;

    private static string RequireActor(string actor)
    {
        var value = (actor ?? string.Empty).Trim();

        return value.Length == 0 ? "system" : value;
    }

    private static string RequireText(string value, int maxLength, string fieldName)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new DomainException($"{fieldName} là bắt buộc.");
        }

        if (text.Length > maxLength)
        {
            throw new DomainException($"{fieldName} tối đa {maxLength} ký tự.");
        }

        return text;
    }
}

/// <summary>Quyền xem văn bản "restricted" cho từng người dùng.</summary>
public sealed class DocumentAccess : Entity
{
    private DocumentAccess()
    {
    }

    public Guid DocumentId { get; private set; }

    public Guid UserId { get; private set; }

    public static DocumentAccess Create(Guid documentId, Guid userId)
    {
        if (documentId == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainException("Quyền truy cập văn bản không hợp lệ.");
        }

        return new DocumentAccess { DocumentId = documentId, UserId = userId };
    }

    public static DocumentAccess Restore(Guid id, Guid documentId, Guid userId)
    {
        var access = Create(documentId, userId);
        access.Id = id;

        return access;
    }
}
