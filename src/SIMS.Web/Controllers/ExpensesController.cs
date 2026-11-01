using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Finance;
using SIMS.Application.Security;
using SIMS.Domain.Clubs;
using SIMS.Domain.Finance;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module EXPENSES: đề nghị chi, duyệt/từ chối, đánh dấu đã chi (ghi giao dịch chi).</summary>
[Authorize]
public class ExpensesController : Controller
{
    private readonly IExpenseService _expenses;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IClubRepository _clubs;

    public ExpensesController(
        IExpenseService expenses,
        IExpenseRepository expenseRepository,
        IClubRepository clubs)
    {
        _expenses = expenses;
        _expenseRepository = expenseRepository;
        _clubs = clubs;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    private bool IsAdmin => User.IsInRole("Admin");

    private Guid? ScopeClubId => IsAdmin ? null : CurrentClubId;

    private bool CanAccess(Guid clubId) => IsAdmin || CurrentClubId == clubId;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Expenses, "view")]
    public async Task<IActionResult> Index(string? status, CancellationToken cancellationToken)
    {
        var statusFilter = ParseStatus(status);
        var expenses = await _expenses.ListAsync(ScopeClubId, statusFilter, cancellationToken);
        var clubs = (await _clubs.ListAsync(cancellationToken))
            .ToDictionary(club => club.Id, club => club.Name);

        return View(new ExpenseListPageModel
        {
            Items = expenses
                .Select(expense => new ExpenseListItem(
                    expense,
                    clubs.TryGetValue(expense.ClubId, out var name) ? name : "?"))
                .ToArray(),
            Summary = ExpenseSummary.From(expenses),
            Status = status,
        });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Expenses, "create")]
    public async Task<IActionResult> Create(Guid? clubId, CancellationToken cancellationToken)
    {
        var clubs = await AvailableClubsAsync(cancellationToken);
        if (clubs.Count == 0)
        {
            TempData["Error"] = "Bạn chưa thuộc CLB nào để tạo chi phí.";
            return RedirectToAction(nameof(Index));
        }

        var selectedClubId = clubId is not null && clubs.Any(club => club.Id == clubId)
            ? clubId.Value
            : clubs[0].Id;

        return View(new ExpenseCreatePageModel
        {
            Form = new ExpenseFormModel { ClubId = selectedClubId },
            Clubs = clubs,
            IsAdmin = IsAdmin,
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Expenses, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ExpenseFormModel model, CancellationToken cancellationToken)
    {
        if (!CanAccess(model.ClubId))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View(new ExpenseCreatePageModel
            {
                Form = model,
                Clubs = await AvailableClubsAsync(cancellationToken),
                IsAdmin = IsAdmin,
            });
        }

        var result = await _expenses.CreateAsync(
            model.ClubId, model.Title, model.Amount, model.Period, model.Description,
            autoApprove: true, requestedBy: CurrentActor, actor: CurrentActor,
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            AddErrors(result);

            return View(new ExpenseCreatePageModel
            {
                Form = model,
                Clubs = await AvailableClubsAsync(cancellationToken),
                IsAdmin = IsAdmin,
            });
        }

        TempData["Status"] = $"Đã tạo chi phí {result.Value!.Title}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Expenses, "edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.FindByIdAsync(id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (!CanAccess(expense.ClubId))
        {
            return Forbid();
        }

        return View(new ExpenseEditPageModel
        {
            Expense = expense,
            Form = ExpenseFormModel.FromExpense(expense),
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Expenses, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ExpenseFormModel model, CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.FindByIdAsync(id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (!CanAccess(expense.ClubId))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View(new ExpenseEditPageModel { Expense = expense, Form = model });
        }

        var result = await _expenses.UpdateAsync(
            id, model.Title, model.Amount, model.Period, model.Description,
            CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            AddErrors(result);
            return View(new ExpenseEditPageModel { Expense = expense, Form = model });
        }

        TempData["Status"] = "Đã cập nhật chi phí.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Expenses, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.FindByIdAsync(id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (!CanAccess(expense.ClubId))
        {
            return Forbid();
        }

        var result = await _expenses.DeleteAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá chi phí." : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Expenses, "approve")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
        => ChangeStatusAsync(id, "Đã duyệt chi phí.", _expenses.ApproveAsync, cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCatalog.Expenses, "reject")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken)
        => ChangeStatusAsync(id, "Đã từ chối chi phí.", _expenses.RejectAsync, cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCatalog.Expenses, "edit")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> MarkPaid(Guid id, CancellationToken cancellationToken)
        => ChangeStatusAsync(id, "Đã đánh dấu chi tiền.", _expenses.MarkPaidAsync, cancellationToken);

    [HttpGet]
    [RequirePermission(PermissionCatalog.Expenses, "my_expenses")]
    public async Task<IActionResult> MyExpenses(CancellationToken cancellationToken)
    {
        var expenses = await _expenses.ListMineAsync(CurrentClubId, CurrentActor, null, cancellationToken);
        var clubs = (await _clubs.ListAsync(cancellationToken))
            .ToDictionary(club => club.Id, club => club.Name);

        return View(new MyExpensesPageModel
        {
            Items = expenses
                .Select(expense => new ExpenseListItem(
                    expense,
                    clubs.TryGetValue(expense.ClubId, out var name) ? name : "?"))
                .ToArray(),
            Summary = ExpenseSummary.From(expenses),
            NewRequest = new ExpenseFormModel { ClubId = CurrentClubId ?? Guid.Empty },
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Expenses, "my_create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MyCreate(ExpenseFormModel model, CancellationToken cancellationToken)
    {
        if (CurrentClubId is null)
        {
            TempData["Error"] = "Bạn chưa thuộc CLB nào để gửi yêu cầu chi.";
            return RedirectToAction(nameof(MyExpenses));
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .FirstOrDefault() ?? "Dữ liệu không hợp lệ.";
            return RedirectToAction(nameof(MyExpenses));
        }

        var result = await _expenses.CreateAsync(
            CurrentClubId.Value, model.Title, model.Amount, model.Period, model.Description,
            autoApprove: false, requestedBy: CurrentActor, actor: CurrentActor,
            cancellationToken: cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã gửi yêu cầu chi." : ErrorOf(result);

        return RedirectToAction(nameof(MyExpenses));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Expenses, "my_edit")]
    public async Task<IActionResult> MyEdit(Guid id, CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.FindByIdAsync(id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (!IsOwnedByCurrentUser(expense))
        {
            return Forbid();
        }

        return View(new ExpenseEditPageModel
        {
            Expense = expense,
            Form = ExpenseFormModel.FromExpense(expense),
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Expenses, "my_edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MyEdit(Guid id, ExpenseFormModel model, CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.FindByIdAsync(id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (!IsOwnedByCurrentUser(expense))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View(new ExpenseEditPageModel { Expense = expense, Form = model });
        }

        var result = await _expenses.UpdateAsync(
            id, model.Title, model.Amount, model.Period, model.Description,
            CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            AddErrors(result);
            return View(new ExpenseEditPageModel { Expense = expense, Form = model });
        }

        TempData["Status"] = "Đã cập nhật yêu cầu chi.";
        return RedirectToAction(nameof(MyExpenses));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Expenses, "my_delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MyDelete(Guid id, CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.FindByIdAsync(id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (!IsOwnedByCurrentUser(expense))
        {
            return Forbid();
        }

        var result = await _expenses.DeleteAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá yêu cầu chi." : ErrorOf(result);

        return RedirectToAction(nameof(MyExpenses));
    }

    private async Task<IActionResult> ChangeStatusAsync(
        Guid id,
        string success,
        Func<Guid, string, CancellationToken, Task<Result<Expense>>> change,
        CancellationToken cancellationToken)
    {
        var expense = await _expenseRepository.FindByIdAsync(id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (!CanAccess(expense.ClubId))
        {
            return Forbid();
        }

        var result = await change(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] = result.IsSuccess ? success : ErrorOf(result);

        return RedirectToAction(nameof(Index));
    }

    private bool IsOwnedByCurrentUser(Expense expense)
        => string.Equals(expense.RequestedBy, CurrentActor, StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<Club>> AvailableClubsAsync(CancellationToken cancellationToken)
    {
        var clubs = await _clubs.ListAsync(cancellationToken);

        return IsAdmin
            ? clubs
            : clubs.Where(club => club.Id == CurrentClubId).ToArray();
    }

    private static ExpenseStatus? ParseStatus(string? status)
        => Enum.TryParse<ExpenseStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;

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
