using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Disciplines;
using SIMS.Application.Security;
using SIMS.Domain.Clubs;
using SIMS.Domain.Disciplines;
using SIMS.Domain.Security;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module DISCIPLINE + MERITS: danh mục, bản ghi, thu hồi, điểm CLB/điểm trường và xếp hạng.</summary>
[Authorize]
public class DisciplinesController : Controller
{
    private readonly IDisciplineService _disciplines;
    private readonly IClubRepository _clubs;
    private readonly IClubMembershipRepository _memberships;
    private readonly IUserRepository _users;

    public DisciplinesController(
        IDisciplineService disciplines,
        IClubRepository clubs,
        IClubMembershipRepository memberships,
        IUserRepository users)
    {
        _disciplines = disciplines;
        _clubs = clubs;
        _memberships = memberships;
        _users = users;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    private bool IsAdmin => User.IsInRole("Admin");

    private Guid? ScopeClubId => IsAdmin ? null : CurrentClubId;

    private bool CanAccess(Guid clubId) => IsAdmin || CurrentClubId == clubId;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Discipline, "view")]
    public async Task<IActionResult> Index(Guid? clubId, CancellationToken cancellationToken)
    {
        var (selected, clubs) = await ResolveClubAsync(clubId, cancellationToken);
        var items = await _disciplines.ListDisciplinesAsync(
            selected == Guid.Empty ? ScopeClubId : selected, false, cancellationToken);

        ViewData["Clubs"] = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray();
        ViewData["SelectedClubId"] = selected;
        ViewData["IsAdmin"] = IsAdmin;

        return View(new DisciplinePageModel { Items = items });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Discipline, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid clubId,
        DisciplineFormModel model,
        CancellationToken cancellationToken)
    {
        if (!CanAccess(clubId) || !ModelState.IsValid)
        {
            TempData["Error"] = ModelState.IsValid
                ? "Bạn không có quyền với CLB này."
                : "Dữ liệu danh mục không hợp lệ.";
            return RedirectToAction(nameof(Index), new { clubId });
        }

        var result = await _disciplines.CreateDisciplineAsync(
            clubId, model.Name, model.Points, model.Description, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? $"Đã tạo danh mục {result.Value!.Code}." : ErrorOf(result);

        return RedirectToAction(nameof(Index), new { clubId });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Discipline, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        DisciplineFormModel model,
        CancellationToken cancellationToken)
    {
        var discipline = await FindDisciplineAsync(id, cancellationToken);
        if (discipline is null)
        {
            return NotFound();
        }

        if (!CanAccess(discipline.ClubId))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu danh mục không hợp lệ.";
            return RedirectToAction(nameof(Index), new { clubId = discipline.ClubId });
        }

        var result = await _disciplines.UpdateDisciplineAsync(
            id, model.Name, model.Points, model.Description, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã cập nhật danh mục." : ErrorOf(result);

        return RedirectToAction(nameof(Index), new { clubId = discipline.ClubId });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Discipline, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var discipline = await FindDisciplineAsync(id, cancellationToken);
        if (discipline is null)
        {
            return NotFound();
        }

        if (!CanAccess(discipline.ClubId))
        {
            return Forbid();
        }

        var result = await _disciplines.DeleteDisciplineAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã xoá danh mục." : ErrorOf(result);

        return RedirectToAction(nameof(Index), new { clubId = discipline.ClubId });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Discipline, "view")]
    public async Task<IActionResult> Records(Guid? clubId, CancellationToken cancellationToken)
    {
        var (selected, clubs) = await ResolveClubAsync(clubId, cancellationToken);
        var clubFilter = selected == Guid.Empty ? ScopeClubId : selected;
        var items = await _disciplines.ListRecordsAsync(clubFilter, null, false, cancellationToken);
        var members = selected == Guid.Empty
            ? Array.Empty<User>()
            : await ListMembersAsync(selected, cancellationToken);

        ViewData["Clubs"] = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray();
        ViewData["SelectedClubId"] = selected;

        return View(new DisciplineRecordsPageModel
        {
            Items = items,
            Disciplines = await _disciplines.ListDisciplinesAsync(clubFilter, true, cancellationToken),
            Members = members,
            UserNames = await UserNamesAsync(cancellationToken),
            Form = new DisciplineRecordFormModel
            {
                DisciplineId = (await _disciplines.ListDisciplinesAsync(clubFilter, true, cancellationToken))
                    .FirstOrDefault()?.Id ?? Guid.Empty,
            },
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Discipline, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(
        Guid clubId,
        DisciplineRecordFormModel model,
        CancellationToken cancellationToken)
    {
        if (!CanAccess(clubId))
        {
            return Forbid();
        }

        var result = await _disciplines.ApplyRecordAsync(
            clubId,
            model.UserId,
            model.DisciplineId,
            model.AppliedDate,
            model.Period,
            model.Notes,
            CurrentActor,
            cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã ghi nhận điểm." : ErrorOf(result);

        return RedirectToAction(nameof(Records), new { clubId });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Discipline, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRecord(
        Guid id,
        Guid disciplineId,
        DateOnly appliedDate,
        string? period,
        string? notes,
        CancellationToken cancellationToken)
    {
        var record = await _disciplines.ListRecordsAsync(null, null, false, cancellationToken);
        var target = record.FirstOrDefault(item => item.Record.Id == id)?.Record;
        if (target is null)
        {
            return NotFound();
        }

        if (!CanAccess(target.ClubId))
        {
            return Forbid();
        }

        var result = await _disciplines.UpdateRecordAsync(
            id, disciplineId, appliedDate, period, notes, CurrentActor, cancellationToken);

        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã cập nhật bản ghi." : ErrorOf(result);

        return RedirectToAction(nameof(Records), new { clubId = target.ClubId });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Discipline, "revoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var items = await _disciplines.ListRecordsAsync(null, null, false, cancellationToken);
        var target = items.FirstOrDefault(item => item.Record.Id == id)?.Record;
        if (target is null)
        {
            return NotFound();
        }

        if (!CanAccess(target.ClubId))
        {
            return Forbid();
        }

        var result = await _disciplines.RevokeRecordAsync(id, CurrentActor, cancellationToken);
        TempData[result.IsSuccess ? "Status" : "Error"] =
            result.IsSuccess ? "Đã thu hồi bản ghi." : ErrorOf(result);

        return RedirectToAction(nameof(Records), new { clubId = target.ClubId });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Discipline, "summary")]
    public async Task<IActionResult> Summary(Guid? clubId, CancellationToken cancellationToken)
    {
        var (selected, clubs) = await ResolveClubAsync(clubId, cancellationToken);
        var clubFilter = selected == Guid.Empty ? CurrentClubId : selected;
        IReadOnlyList<MemberPointRow> rows = clubFilter is null
            ? Array.Empty<MemberPointRow>()
            : await _disciplines.GetMemberPointsAsync(clubFilter.Value, cancellationToken);

        ViewData["Clubs"] = clubs.Select(club => new ClubOption(club.Id, club.Code, club.Name)).ToArray();
        ViewData["SelectedClubId"] = selected;

        return View(new DisciplineSummaryPageModel
        {
            Rows = rows,
            UserNames = await UserNamesAsync(cancellationToken),
        });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Discipline, "summary")]
    public async Task<IActionResult> ExportSummary(Guid clubId, CancellationToken cancellationToken)
    {
        if (!CanAccess(clubId))
        {
            return Forbid();
        }

        var rows = await _disciplines.GetMemberPointsAsync(clubId, cancellationToken);
        var users = await _users.ListAsync(cancellationToken);
        var names = users.ToDictionary(user => user.Id, user => user.DisplayName);
        var usernames = users.ToDictionary(user => user.Id, user => user.Username);

        var builder = new StringBuilder();
        builder.AppendLine("username,display_name,club_points,school_points,is_leader,rank");

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            builder.AppendLine(string.Join(
                ',',
                Escape(usernames.TryGetValue(row.UserId, out var username) ? username : row.UserId.ToString("D")),
                Escape(names.TryGetValue(row.UserId, out var name) ? name : "?"),
                Format(row.ClubPoints),
                Format(row.SchoolPoints),
                row.IsLeader ? "true" : "false",
                (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = Encoding.UTF8.GetBytes(builder.ToString());

        return File(bom.Concat(body).ToArray(), "text/csv", $"diem-ren-luyen-{clubId:N}.csv");
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Discipline, "my_points")]
    public async Task<IActionResult> MyPoints(CancellationToken cancellationToken)
    {
        if (CurrentClubId is null)
        {
            return NotFound();
        }

        var user = await _users.FindByUsernameAsync(CurrentActor, cancellationToken);
        if (user is null)
        {
            return Forbid();
        }

        var points = await _disciplines.GetUserPointsAsync(CurrentClubId.Value, user.Id, cancellationToken);
        var club = await _clubs.FindByIdAsync(CurrentClubId.Value, cancellationToken);

        return View(new MyPointsPageModel
        {
            Points = points ?? new UserPointsView(user.Id, 0m, 0m, false, null, Array.Empty<DisciplineRecordItem>()),
            ClubName = club?.Name ?? "?",
        });
    }

    private async Task<(Guid Selected, IReadOnlyList<Club> Clubs)> ResolveClubAsync(
        Guid? clubId,
        CancellationToken cancellationToken)
    {
        var clubs = await _clubs.ListAsync(cancellationToken);
        var available = IsAdmin
            ? clubs
            : clubs.Where(club => club.Id == CurrentClubId).ToArray();

        var selected = clubId is not null && available.Any(club => club.Id == clubId)
            ? clubId.Value
            : (CurrentClubId ?? available.FirstOrDefault()?.Id ?? Guid.Empty);

        return (selected, available);
    }

    private async Task<Discipline?> FindDisciplineAsync(Guid id, CancellationToken cancellationToken)
    {
        var items = await _disciplines.ListDisciplinesAsync(null, false, cancellationToken);

        return items.FirstOrDefault(discipline => discipline.Id == id);
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

    private static string Format(decimal value)
        => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;

        return text.Contains(',') || text.Contains('"')
            ? '"' + text.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : text;
    }

    private static string ErrorOf(Result result)
        => result.Errors.FirstOrDefault()?.Message ?? "Thao tác thất bại.";
}
