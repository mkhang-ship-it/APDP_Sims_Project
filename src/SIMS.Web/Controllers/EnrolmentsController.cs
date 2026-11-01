using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Abstractions;
using SIMS.Application.Common;
using SIMS.Application.Enrolments;
using SIMS.Application.Grades;
using SIMS.Application.Security;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module ENROLMENTS + GRADES — Admin và Faculty.</summary>
[Authorize]
public class EnrolmentsController : Controller
{
    private readonly IEnrolmentService _enrolmentService;
    private readonly IGradeService _gradeService;
    private readonly IStudentReader _students;
    private readonly ICourseRepository _courses;

    public EnrolmentsController(
        IEnrolmentService enrolmentService,
        IGradeService gradeService,
        IStudentReader students,
        ICourseRepository courses)
    {
        _enrolmentService = enrolmentService;
        _gradeService = gradeService;
        _students = students;
        _courses = courses;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    [HttpGet]
    [RequirePermission(PermissionCatalog.Enrolments, "view")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var enrolments = await _enrolmentService.ListAsync(cancellationToken);
        var students = await _students.ListAsync(cancellationToken);
        var courses = await _courses.ListAsync(cancellationToken);

        var studentById = students.ToDictionary(s => s.Id);
        var courseById = courses.ToDictionary(c => c.Id);

        var rows = enrolments
            .Select(e => new EnrolmentRow(
                e,
                studentById.GetValueOrDefault(e.StudentId),
                courseById.GetValueOrDefault(e.CourseId)))
            .OrderBy(r => r.Course?.Code)
            .ThenBy(r => r.Student?.StudentCode)
            .ToArray();

        return View(new EnrolmentIndexViewModel { Rows = rows });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Enrolments, "create")]
    public async Task<IActionResult> Enrol(CancellationToken cancellationToken)
    {
        var model = new EnrolFormModel
        {
            Students = await _students.ListAsync(cancellationToken),
            Courses = await _courses.ListAsync(cancellationToken),
        };

        return View(model);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Enrolments, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enrol(Guid studentId, Guid courseId, CancellationToken cancellationToken)
    {
        var result = await _enrolmentService.EnrolAsync(studentId, courseId, CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không thể đăng ký học phần.";
            return RedirectToAction(nameof(Enrol));
        }

        TempData["Status"] = "Đã đăng ký học phần.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Enrolments, "grade")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Grade(Guid enrolmentId, decimal score, CancellationToken cancellationToken)
    {
        var result = await _gradeService.AssignAsync(enrolmentId, score, CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không nhập được điểm.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Status"] = $"Đã nhập điểm {result.Value!.Score} ({result.Value.GradeLetter}).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Enrolments, "drop")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Drop(Guid enrolmentId, CancellationToken cancellationToken)
    {
        var result = await _enrolmentService.DropAsync(enrolmentId, CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không huỷ được đăng ký.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Status"] = "Đã huỷ đăng ký.";
        return RedirectToAction(nameof(Index));
    }

    private void AddErrors(Result result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Field, error.Message);
        }
    }
}
