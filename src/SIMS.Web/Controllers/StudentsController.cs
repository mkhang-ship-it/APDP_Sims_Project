using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Security;
using SIMS.Application.Students;
using SIMS.Application.Students.Import;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module STUDENTS — phân quyền theo module/action (permissions.csv).</summary>
[Authorize]
public class StudentsController : Controller
{
    private readonly IStudentService _studentService;
    private readonly IStudentImportService _importService;
    private readonly IUserRepository _users;

    public StudentsController(
        IStudentService studentService,
        IStudentImportService importService,
        IUserRepository users)
    {
        _studentService = studentService;
        _importService = importService;
        _users = users;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    private Guid? CurrentClubId
        => Guid.TryParse(User.FindFirst("club_id")?.Value, out var clubId) ? clubId : null;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Students, "view")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var students = await _studentService.ListAsync(cancellationToken);
        var clubId = CurrentClubId;

        if (clubId is not null && !User.IsInRole("Admin"))
        {
            // Multi-club: chỉ thấy sinh viên của CLB hiện tại + sinh viên chưa gán CLB.
            students = students.Where(s => s.ClubId == clubId || s.ClubId is null).ToArray();
        }

        return View(students);
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Students, "view")]
    public async Task<IActionResult> Export(string? format, CancellationToken cancellationToken)
    {
        var students = await _studentService.ListAsync(cancellationToken);
        var clubId = CurrentClubId;

        if (clubId is not null && !User.IsInRole("Admin"))
        {
            students = students.Where(s => s.ClubId == clubId || s.ClubId is null).ToArray();
        }

        var rows = new List<IReadOnlyList<string?>>
        {
            new[] { "student_code", "full_name", "email", "date_of_birth", "program", "status", "club_id" },
        };

        foreach (var student in students.OrderBy(item => item.StudentCode, StringComparer.Ordinal))
        {
            rows.Add(
            [
                student.StudentCode,
                student.FullName,
                student.Email ?? string.Empty,
                student.DateOfBirth.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                student.Program,
                student.Status.ToString(),
                student.ClubId?.ToString("D") ?? string.Empty,
            ]);
        }

        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            var workbook = Xlsx.Write("Sinh viên", rows);

            return File(
                workbook,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"sinh-vien-{DateTime.UtcNow:yyyyMMdd}.xlsx");
        }

        var builder = new System.Text.StringBuilder();
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', row.Select(Escape)));
        }

        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = System.Text.Encoding.UTF8.GetBytes(builder.ToString());

        return File(bom.Concat(body).ToArray(), "text/csv", $"sinh-vien-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;

        return text.Contains(',') || text.Contains('"')
            ? '"' + text.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : text;
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Students, "view")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var student = await _studentService.GetAsync(id, cancellationToken);
        return student is null ? NotFound() : View(student);
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Students, "create")]
    public IActionResult Create() => View(new StudentFormModel());

    [HttpPost]
    [RequirePermission(PermissionCatalog.Students, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StudentFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _studentService.CreateAsync(model.ToInput(), CurrentActor, CurrentClubId, cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            AddErrors(result);
            return View(model);
        }

        // Student↔User: nếu đã có tài khoản cùng email thì liên kết để vai trò Student xem được hồ sơ.
        var linkedUser = await _users.FindByEmailAsync(result.Value.Email, cancellationToken);
        if (linkedUser is not null)
        {
            await _studentService.LinkUserAsync(result.Value.Id, linkedUser.Id, CurrentActor, cancellationToken);
        }

        TempData["Status"] = $"Đã thêm sinh viên {result.Value.StudentCode}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Students, "edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var student = await _studentService.GetAsync(id, cancellationToken);
        return student is null ? NotFound() : View(StudentFormModel.FromStudent(student));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Students, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, StudentFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _studentService.UpdateAsync(id, model.ToInput(), CurrentActor, cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Status"] = $"Đã cập nhật sinh viên {result.Value.StudentCode}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Students, "delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var student = await _studentService.GetAsync(id, cancellationToken);
        return student is null ? NotFound() : View(student);
    }

    [HttpPost]
    [ActionName(nameof(Delete))]
    [RequirePermission(PermissionCatalog.Students, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        var result = await _studentService.DeleteAsync(id, CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không xoá được sinh viên.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Status"] = "Đã xoá sinh viên.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Students, "import")]
    public IActionResult Import() => View(new StudentImportViewModel());

    [HttpPost]
    [RequirePermission(PermissionCatalog.Students, "import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Vui lòng chọn file CSV hoặc Excel (.xlsx).");
            return View(new StudentImportViewModel());
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            ModelState.AddModelError(string.Empty, "Tệp tối đa 5 MB.");
            return View(new StudentImportViewModel());
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        Result<StudentImportResult> result;

        if (Xlsx.LooksLikeXlsx(bytes))
        {
            using var workbook = new MemoryStream(bytes);
            var csv = Xlsx.ToCsv(workbook);
            using var csvStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
            result = await _importService.ImportAsync(csvStream, CurrentActor, cancellationToken);
        }
        else
        {
            using var csvStream = new MemoryStream(bytes);
            result = await _importService.ImportAsync(csvStream, CurrentActor, cancellationToken);
        }

        if (!result.IsSuccess || result.Value is null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Message);
            }

            return View(new StudentImportViewModel());
        }

        TempData["Status"] = $"Import xong: {result.Value.Imported}/{result.Value.TotalRows} dòng hợp lệ.";
        return View(new StudentImportViewModel { Result = result.Value });
    }

    private void AddErrors(Result result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Field, error.Message);
        }
    }
}
