using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Documents;
using SIMS.Application.Security;
using SIMS.Domain.Clubs;
using SIMS.Domain.Documents;
using SIMS.Domain.Security;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>
/// Module DOCUMENTS: loại văn bản, văn bản + file đính kèm, duyệt/từ chối, chia sẻ,
/// yêu cầu nộp và nộp văn bản của thành viên.
/// </summary>
[Authorize]
public class DocumentsController : Controller
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IDocumentService _documents;
    private readonly IDocumentRequestService _requests;
    private readonly IClubRepository _clubs;
    private readonly IClubMembershipRepository _memberships;
    private readonly IUserRepository _users;
    private readonly IAuthorizationService _authorization;

    public DocumentsController(
        IDocumentService documents,
        IDocumentRequestService requests,
        IClubRepository clubs,
        IClubMembershipRepository memberships,
        IUserRepository users,
        IAuthorizationService authorization)
    {
        _documents = documents;
        _requests = requests;
        _clubs = clubs;
        _memberships = memberships;
        _users = users;
        _authorization = authorization;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    private bool IsAdmin => User.IsInRole("Admin");

    private Guid? ScopeClubId => IsAdmin ? null : CurrentClubId;

    private bool CanAccess(Guid clubId) => IsAdmin || CurrentClubId == clubId;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Documents, "view")]
    public async Task<IActionResult> Index(
        string? status,
        Guid? documentTypeId,
        CancellationToken cancellationToken)
    {
        var statusFilter = Enum.TryParse<DocumentStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : (DocumentStatus?)null;

        var items = await _documents
            .ListAsync(ScopeClubId, statusFilter, documentTypeId, cancellationToken);

        return View(new DocumentListPageModel
        {
            Items = items,
            Types = await _documents.ListTypesAsync(ScopeClubId, false, cancellationToken),
            UserNames = await UserNamesAsync(cancellationToken),
            Status = status,
            DocumentTypeId = documentTypeId,
        });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Documents, "create")]
    public async Task<IActionResult> Create(Guid? clubId, CancellationToken cancellationToken)
    {
        var clubs = await AvailableClubsAsync(cancellationToken);
        if (clubs.Count == 0)
        {
            TempData["Error"] = "Bạn chưa thuộc CLB nào để tải văn bản.";
            return RedirectToAction(nameof(Index));
        }

        var selected = clubId is not null && clubs.Any(club => club.Id == clubId)
            ? clubId.Value
            : clubs[0].Id;

        return View(new DocumentCreatePageModel
        {
            Form = new DocumentUploadFormModel { ClubId = selected },
            Types = await _documents.ListTypesAsync(selected, true, cancellationToken),
            Clubs = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray(),
            IsAdmin = IsAdmin,
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "create")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes + 4096)]
    public async Task<IActionResult> Create(DocumentUploadFormModel model, CancellationToken cancellationToken)
    {
        if (!CanAccess(model.ClubId))
        {
            return Forbid();
        }

        var clubs = await AvailableClubsAsync(cancellationToken);
        var types = await _documents.ListTypesAsync(model.ClubId, true, cancellationToken);
        var fileError = ValidateFile(model.File);

        if (!ModelState.IsValid || fileError is not null)
        {
            if (fileError is not null)
            {
                ModelState.AddModelError(nameof(model.File), fileError);
            }

            return View(new DocumentCreatePageModel
            {
                Form = model,
                Types = types,
                Clubs = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray(),
                IsAdmin = IsAdmin,
            });
        }

        await using var stream = model.File!.OpenReadStream();
        var result = await _documents.UploadAsync(
            model.ClubId,
            model.DocumentTypeId,
            model.Title,
            CurrentActor,
            model.Visibility,
            stream,
            model.File.FileName,
            CurrentActor,
            cancellationToken);

        if (!result.IsSuccess)
        {
            AddErrors(result);

            return View(new DocumentCreatePageModel
            {
                Form = model,
                Types = types,
                Clubs = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray(),
                IsAdmin = IsAdmin,
            });
        }

        TempData["Status"] = $"Đã tải văn bản {result.Value!.Title}.";
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Documents, "view")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var document = await _documents.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (!CanAccess(document.ClubId))
        {
            return Forbid();
        }

        return View(new DocumentDetailsPageModel
        {
            Document = document,
            Type = await _documents.GetTypeAsync(document.DocumentTypeId, cancellationToken),
            Access = await _documents.ListAccessAsync(id, cancellationToken),
            Members = await ListMembersAsync(document.ClubId, cancellationToken),
            UserNames = await UserNamesAsync(cancellationToken),
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(
        Guid id,
        bool approve,
        string? reason,
        CancellationToken cancellationToken)
    {
        var document = await _documents.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (!CanAccess(document.ClubId))
        {
            return Forbid();
        }

        var result = await _documents.ReviewAsync(id, approve, reason, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] = result.IsSuccess
            ? (approve ? "Đã duyệt văn bản." : "Đã từ chối văn bản.")
            : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Visibility(
        Guid id,
        DocumentVisibility visibility,
        CancellationToken cancellationToken)
    {
        var document = await _documents.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (!CanAccess(document.ClubId))
        {
            return Forbid();
        }

        var result = await _documents.SetVisibilityAsync(id, visibility, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã đổi chế độ hiển thị." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Access(
        Guid id,
        Guid[]? userIds,
        CancellationToken cancellationToken)
    {
        var document = await _documents.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (!CanAccess(document.ClubId))
        {
            return Forbid();
        }

        var requested = userIds ?? Array.Empty<Guid>();
        var allowed = (await ListMembersAsync(document.ClubId, cancellationToken))
            .Select(member => member.Id)
            .ToHashSet();

        if (requested.Any(userId => !allowed.Contains(userId)))
        {
            return Forbid();
        }

        var result = await _documents.SetAccessAsync(id, requested, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã cập nhật danh sách được xem." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var document = await _documents.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (!CanAccess(document.ClubId))
        {
            return Forbid();
        }

        var result = await _documents.DeleteAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá văn bản." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var document = await _documents.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var isStaff = (await _authorization
            .AuthorizeAsync(User, HttpContext, "perm:documents:view")).Succeeded;
        var user = await _users.FindByUsernameAsync(CurrentActor, cancellationToken);

        if (isStaff && CanAccess(document.ClubId))
        {
            return StreamFile(document);
        }

        if (user is not null &&
            await _documents.CanViewAsync(document, user.Id, CurrentActor, cancellationToken))
        {
            return StreamFile(document);
        }

        return Forbid();
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Documents, "my_documents")]
    public async Task<IActionResult> My(CancellationToken cancellationToken)
    {
        var items = await _documents.ListMineAsync(CurrentClubId, CurrentActor, cancellationToken);

        return View(new MyDocumentsPageModel
        {
            Items = items,
            Types = await _documents.ListTypesAsync(CurrentClubId, true, cancellationToken),
            Form = new DocumentUploadFormModel { ClubId = CurrentClubId ?? Guid.Empty },
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "upload_own")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes + 4096)]
    public async Task<IActionResult> MyUpload(DocumentUploadFormModel model, CancellationToken cancellationToken)
    {
        if (CurrentClubId is null)
        {
            TempData["Error"] = "Bạn chưa thuộc CLB nào để tải văn bản.";
            return RedirectToAction(nameof(My));
        }

        var fileError = ValidateFile(model.File);
        if (fileError is not null)
        {
            TempData["Error"] = fileError;
            return RedirectToAction(nameof(My));
        }

        await using var stream = model.File!.OpenReadStream();
        var result = await _documents.UploadAsync(
            CurrentClubId.Value,
            model.DocumentTypeId,
            model.Title,
            CurrentActor,
            DocumentVisibility.Private,
            stream,
            model.File.FileName,
            CurrentActor,
            cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã tải văn bản, chờ duyệt." : ErrorOf(result);

        return RedirectToAction(nameof(My));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Documents, "my_documents")]
    public async Task<IActionResult> Accessible(CancellationToken cancellationToken)
    {
        if (CurrentClubId is null)
        {
            return View(new DocumentListPageModel());
        }

        var user = await _users.FindByUsernameAsync(CurrentActor, cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        var items = await _documents
            .ListAccessibleAsync(CurrentClubId.Value, user.Id, CurrentActor, cancellationToken);

        return View(new DocumentListPageModel
        {
            Items = items,
            Types = await _documents.ListTypesAsync(CurrentClubId, true, cancellationToken),
            UserNames = await UserNamesAsync(cancellationToken),
        });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Documents, "view")]
    public async Task<IActionResult> Types(Guid? clubId, CancellationToken cancellationToken)
    {
        var clubs = await AvailableClubsAsync(cancellationToken);
        var selected = clubId is not null && clubs.Any(club => club.Id == clubId)
            ? clubId
            : (IsAdmin ? (Guid?)null : CurrentClubId);

        var types = await _documents.ListTypesAsync(selected, false, cancellationToken);

        ViewData["Clubs"] = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray();
        ViewData["SelectedClubId"] = selected;
        ViewData["IsAdmin"] = IsAdmin;

        return View(types);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateType(
        Guid clubId,
        DocumentTypeFormModel model,
        CancellationToken cancellationToken)
    {
        if (!CanAccess(clubId))
        {
            return Forbid();
        }

        var result = await _documents.CreateTypeAsync(
            clubId, model.Name, model.Description, model.TemplateUrl, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? $"Đã tạo loại văn bản {result.Value!.Code}." : ErrorOf(result);

        return RedirectToAction(nameof(Types), new { clubId });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditType(
        Guid id,
        DocumentTypeFormModel model,
        CancellationToken cancellationToken)
    {
        var type = await _documents.GetTypeAsync(id, cancellationToken);
        if (type is null)
        {
            return NotFound();
        }

        if (!CanAccess(type.ClubId))
        {
            return Forbid();
        }

        var result = await _documents.UpdateTypeAsync(
            id, model.Name, model.Description, model.TemplateUrl, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã cập nhật loại văn bản." : ErrorOf(result);

        return RedirectToAction(nameof(Types), new { clubId = type.ClubId });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteType(Guid id, CancellationToken cancellationToken)
    {
        var type = await _documents.GetTypeAsync(id, cancellationToken);
        if (type is null)
        {
            return NotFound();
        }

        if (!CanAccess(type.ClubId))
        {
            return Forbid();
        }

        var result = await _documents.DeleteTypeAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá loại văn bản." : ErrorOf(result);

        return RedirectToAction(nameof(Types), new { clubId = type.ClubId });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Documents, "requests")]
    public async Task<IActionResult> Requests(Guid? clubId, CancellationToken cancellationToken)
    {
        var clubs = await AvailableClubsAsync(cancellationToken);
        var selected = clubId is not null && clubs.Any(club => club.Id == clubId)
            ? clubId.Value
            : (CurrentClubId ?? clubs.FirstOrDefault()?.Id ?? Guid.Empty);

        var rows = selected == Guid.Empty
            ? Array.Empty<DocumentRequestRow>()
            : await _requests.ListByClubAsync(selected, cancellationToken);

        ViewData["Clubs"] = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray();
        ViewData["SelectedClubId"] = selected;

        return View(new DocumentRequestsPageModel
        {
            Rows = rows,
            Types = await _documents.ListTypesAsync(selected == Guid.Empty ? null : selected, true, cancellationToken),
            Members = selected == Guid.Empty
                ? Array.Empty<User>()
                : await ListMembersAsync(selected, cancellationToken),
            UserNames = await UserNamesAsync(cancellationToken),
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "request_create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRequest(
        DocumentRequestFormModel model,
        CancellationToken cancellationToken)
    {
        if (!CanAccess(model.ClubId))
        {
            return Forbid();
        }

        var members = await ListMembersAsync(model.ClubId, cancellationToken);
        var allowed = members.Select(member => member.Id).ToHashSet();

        if (model.UserIds.Count == 0 || model.UserIds.Any(id => !allowed.Contains(id)))
        {
            TempData["Error"] = model.UserIds.Count == 0
                ? "Chọn ít nhất một thành viên cần nộp."
                : "Có thành viên không thuộc CLB đã chọn.";
            return RedirectToAction(nameof(Requests), new { clubId = model.ClubId });
        }

        var result = await _requests.CreateAsync(
            model.ClubId, model.DocumentTypeId, model.Deadline, model.UserIds,
            CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã tạo yêu cầu nộp văn bản." : ErrorOf(result);

        return RedirectToAction(nameof(Requests), new { clubId = model.ClubId });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "request_create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRequest(Guid id, CancellationToken cancellationToken)
    {
        var result = await _requests.DeleteAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá yêu cầu." : ErrorOf(result);

        return RedirectToAction(nameof(Requests));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Documents, "my_documents")]
    public async Task<IActionResult> MyRequests(CancellationToken cancellationToken)
    {
        var user = await _users.FindByUsernameAsync(CurrentActor, cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        return View(new MyDocumentRequestsPageModel
        {
            Items = await _requests.ListMineAsync(user.Id, cancellationToken),
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Documents, "upload_own")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes + 4096)]
    public async Task<IActionResult> SubmitRequest(
        Guid requestUserId,
        string title,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var fileError = ValidateFile(file);
        if (fileError is not null)
        {
            TempData["Error"] = fileError;
            return RedirectToAction(nameof(MyRequests));
        }

        var user = await _users.FindByUsernameAsync(CurrentActor, cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        await using var stream = file!.OpenReadStream();
        var result = await _requests.SubmitAsync(
            requestUserId,
            user.Id,
            title,
            stream,
            file.FileName,
            CurrentActor,
            cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã nộp văn bản, chờ duyệt." : ErrorOf(result);

        return RedirectToAction(nameof(MyRequests));
    }

    private IActionResult StreamFile(Document document)
    {
        var stream = _documents.OpenFile(document);
        if (stream is null)
        {
            return NotFound();
        }

        var extension = Path.GetExtension(document.FilePath);
        var downloadName = document.Title + (string.IsNullOrWhiteSpace(extension) ? string.Empty : extension);

        return File(stream, "application/octet-stream", downloadName);
    }

    private static string? ValidateFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return "Chọn tệp cần tải lên.";
        }

        if (file.Length > MaxUploadBytes)
        {
            return "Tệp tối đa 10 MB.";
        }

        var extension = Path.GetExtension(file.FileName);
        return !string.IsNullOrWhiteSpace(extension) && DocumentService.AllowedExtensions.Contains(extension)
            ? null
            : "Chỉ nhận tệp PDF, Word, ảnh JPG/PNG hoặc TXT.";
    }

    private async Task<IReadOnlyList<Club>> AvailableClubsAsync(CancellationToken cancellationToken)
    {
        var clubs = await _clubs.ListAsync(cancellationToken);

        return IsAdmin
            ? clubs
            : clubs.Where(club => club.Id == CurrentClubId).ToArray();
    }

    private async Task<IReadOnlyList<User>> ListMembersAsync(
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var memberships = await _memberships.ListByClubAsync(clubId, cancellationToken);
        var users = new List<User>();

        foreach (var membership in memberships.Where(item => item.IsActive))
        {
            var user = await _users.FindByIdAsync(membership.UserId, cancellationToken);
            if (user is not null)
            {
                users.Add(user);
            }
        }

        return users
            .OrderBy(user => user.DisplayName, StringComparer.CurrentCulture)
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(CancellationToken cancellationToken)
        => (await _users.ListAsync(cancellationToken))
            .ToDictionary(user => user.Id, user => user.DisplayName);

    private static string ErrorOf(Result result)
        => result.Errors.FirstOrDefault()?.Message ?? "Thao tác thất bại.";

    private void AddErrors(Result result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Field, error.Message);
        }
    }
}
