using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Finance;
using SIMS.Application.Security;
using SIMS.Domain.Clubs;
using SIMS.Domain.Finance;
using SIMS.Domain.Security;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module INVOICES: hoá đơn thu theo thành viên, thanh toán từng phần, quá hạn, phiếu in.</summary>
[Authorize]
public class InvoicesController : Controller
{
    private readonly IInvoiceService _invoices;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IClubRepository _clubs;
    private readonly IClubMembershipRepository _memberships;
    private readonly IUserRepository _users;
    private readonly IAuthorizationService _authorization;

    public InvoicesController(
        IInvoiceService invoices,
        IInvoiceRepository invoiceRepository,
        IClubRepository clubs,
        IClubMembershipRepository memberships,
        IUserRepository users,
        IAuthorizationService authorization)
    {
        _invoices = invoices;
        _invoiceRepository = invoiceRepository;
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
    [RequirePermission(PermissionCatalog.Invoices, "view")]
    public async Task<IActionResult> Index(
        string? status,
        int? month,
        int? year,
        CancellationToken cancellationToken)
    {
        var statusFilter = ParseStatus(status);
        var rows = await _invoices.ListAsync(ScopeClubId, statusFilter, cancellationToken);

        if (month is >= 1 and <= 12)
        {
            rows = rows
                .Where(row => row.Invoice.CreatedAtUtc.UtcDateTime.Month == month)
                .ToArray();
        }

        if (year is >= 2000 and <= 2100)
        {
            rows = rows
                .Where(row => row.Invoice.CreatedAtUtc.UtcDateTime.Year == year)
                .ToArray();
        }

        var model = new InvoiceListPageModel
        {
            Items = await MapItemsAsync(rows, cancellationToken),
            Summary = InvoiceSummary.From(rows),
            Status = status,
            Month = month,
            Year = year,
        };

        return View(model);
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Invoices, "create")]
    public async Task<IActionResult> Create(Guid? clubId, CancellationToken cancellationToken)
    {
        var clubs = await AvailableClubsAsync(cancellationToken);
        if (clubs.Count == 0)
        {
            TempData["Error"] = "Bạn chưa thuộc CLB nào để tạo hoá đơn.";
            return RedirectToAction(nameof(Index));
        }

        var selectedClubId = clubId is not null && clubs.Any(club => club.Id == clubId)
            ? clubId.Value
            : clubs[0].Id;

        return View(new InvoiceCreatePageModel
        {
            Form = new InvoiceFormModel { ClubId = selectedClubId },
            Clubs = clubs,
            Members = await ListMembersAsync(selectedClubId, cancellationToken),
            IsAdmin = IsAdmin,
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Invoices, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InvoiceFormModel model, CancellationToken cancellationToken)
    {
        if (!CanAccess(model.ClubId))
        {
            return Forbid();
        }

        var members = await ListMembersAsync(model.ClubId, cancellationToken);
        var memberIds = members.Select(member => member.Id).ToHashSet();

        if (!ModelState.IsValid || model.MemberIds.Count == 0 ||
            model.MemberIds.Any(id => !memberIds.Contains(id)))
        {
            if (model.MemberIds.Count == 0)
            {
                ModelState.AddModelError(nameof(model.MemberIds), "Chọn ít nhất một thành viên.");
            }
            else if (model.MemberIds.Any(id => !memberIds.Contains(id)))
            {
                ModelState.AddModelError(nameof(model.MemberIds), "Có thành viên không thuộc CLB đã chọn.");
            }

            return View(new InvoiceCreatePageModel
            {
                Form = model,
                Clubs = await AvailableClubsAsync(cancellationToken),
                Members = members,
                IsAdmin = IsAdmin,
            });
        }

        var result = await _invoices.CreateAsync(
            model.ToInput(), model.MemberIds, CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            AddErrors(result);

            return View(new InvoiceCreatePageModel
            {
                Form = model,
                Clubs = await AvailableClubsAsync(cancellationToken),
                Members = members,
                IsAdmin = IsAdmin,
            });
        }

        TempData["Status"] = $"Đã tạo {result.Value!.Count} hoá đơn.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Invoices, "view")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _invoices.GetAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        if (!CanAccess(detail.Invoice.ClubId))
        {
            return Forbid();
        }

        var member = await _users.FindByIdAsync(detail.Invoice.UserId, cancellationToken);
        var club = await _clubs.FindByIdAsync(detail.Invoice.ClubId, cancellationToken);

        return View(new InvoiceDetailsPageModel
        {
            Detail = detail,
            MemberName = member?.DisplayName ?? "?",
            ClubName = club?.Name ?? "?",
        });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Invoices, "edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FindByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (!CanAccess(invoice.ClubId))
        {
            return Forbid();
        }

        var member = await _users.FindByIdAsync(invoice.UserId, cancellationToken);

        return View(new InvoiceEditPageModel
        {
            Invoice = invoice,
            Form = InvoiceFormModel.FromInvoice(invoice),
            MemberName = member?.DisplayName ?? "?",
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Invoices, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, InvoiceFormModel model, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FindByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (!CanAccess(invoice.ClubId))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            var member = await _users.FindByIdAsync(invoice.UserId, cancellationToken);

            return View(new InvoiceEditPageModel
            {
                Invoice = invoice,
                Form = model,
                MemberName = member?.DisplayName ?? "?",
            });
        }

        var result = await _invoices.UpdateAsync(
            id, model.Title, model.TotalAmount, model.DueDate, model.Period, model.Note,
            CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            AddErrors(result);

            var member = await _users.FindByIdAsync(invoice.UserId, cancellationToken);

            return View(new InvoiceEditPageModel
            {
                Invoice = invoice,
                Form = model,
                MemberName = member?.DisplayName ?? "?",
            });
        }

        TempData["Status"] = "Đã cập nhật hoá đơn.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Invoices, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FindByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (!CanAccess(invoice.ClubId))
        {
            return Forbid();
        }

        var result = await _invoices.DeleteAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá hoá đơn." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Invoices, "mark_paid")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPaid(
        Guid id,
        decimal amount,
        DateOnly paymentDate,
        string? method,
        string? note,
        CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FindByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (!CanAccess(invoice.ClubId))
        {
            return Forbid();
        }

        var paymentMethod = Enum.TryParse<PaymentMethod>(method, ignoreCase: true, out var parsed)
            ? parsed
            : PaymentMethod.Cash;

        var result = await _invoices.RecordPaymentAsync(
            id, amount, paymentDate, paymentMethod, note, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã ghi nhận thanh toán." : ErrorOf(result);

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Invoices, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkOverdue(CancellationToken cancellationToken)
    {
        var result = await _invoices.MarkOverdueAsync(
            ScopeClubId, DateOnly.FromDateTime(DateTime.UtcNow), CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] = result.IsSuccess
            ? $"Đã cập nhật {result.Value} hoá đơn quá hạn."
            : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Invoices, "my_invoices")]
    public async Task<IActionResult> MyInvoices(CancellationToken cancellationToken)
    {
        var user = await _users.FindByUsernameAsync(CurrentActor, cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        var rows = await _invoices.ListForUserAsync(CurrentClubId, user.Id, null, cancellationToken);

        return View(new MyInvoicesPageModel
        {
            Items = await MapItemsAsync(rows, cancellationToken),
            Summary = InvoiceSummary.From(rows),
        });
    }

    [HttpGet]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _invoices.GetAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        var isStaff = (await _authorization.AuthorizeAsync(User, HttpContext, "perm:invoices:view")).Succeeded;
        var currentUser = await _users.FindByUsernameAsync(CurrentActor, cancellationToken);
        var isOwner = currentUser is not null && detail.Invoice.UserId == currentUser.Id;

        if (isStaff && !CanAccess(detail.Invoice.ClubId))
        {
            return Forbid();
        }

        if (!isStaff && !isOwner)
        {
            return Forbid();
        }

        var member = await _users.FindByIdAsync(detail.Invoice.UserId, cancellationToken);
        var club = await _clubs.FindByIdAsync(detail.Invoice.ClubId, cancellationToken);
        var paid = detail.Transactions
            .Where(item => item.Type == TransactionType.Income)
            .Sum(item => item.PaidAmount);

        return View(new InvoiceReceiptPageModel
        {
            Invoice = detail.Invoice,
            MemberName = member?.DisplayName ?? "?",
            ClubName = club?.Name ?? "?",
            Transactions = detail.Transactions,
            PaidAmount = paid,
        });
    }

    private async Task<IReadOnlyList<InvoiceListItem>> MapItemsAsync(
        IReadOnlyList<InvoiceRow> rows,
        CancellationToken cancellationToken)
    {
        var users = (await _users.ListAsync(cancellationToken))
            .ToDictionary(user => user.Id, user => user.DisplayName);
        var clubs = (await _clubs.ListAsync(cancellationToken))
            .ToDictionary(club => club.Id, club => club.Name);

        return rows
            .Select(row => new InvoiceListItem(
                row,
                users.TryGetValue(row.Invoice.UserId, out var name) ? name : "?",
                clubs.TryGetValue(row.Invoice.ClubId, out var clubName) ? clubName : "?"))
            .ToArray();
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

    private static InvoiceStatus? ParseStatus(string? status)
        => Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;

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
