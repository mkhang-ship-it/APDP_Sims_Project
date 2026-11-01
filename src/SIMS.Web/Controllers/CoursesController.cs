using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIMS.Application.Common;
using SIMS.Application.Security;
using SIMS.Application.Courses;
using SIMS.Domain.Courses;
using SIMS.Web.Models;
using SIMS.Web.Security;

namespace SIMS.Web.Controllers;

/// <summary>Module COURSES — Admin và Faculty.</summary>
[Authorize]
public class CoursesController : Controller
{
    private readonly ICourseService _courseService;

    public CoursesController(ICourseService courseService)
    {
        _courseService = courseService;
    }

    private string CurrentActor => User.Identity?.Name ?? "anonymous";

    [HttpGet]
    [RequirePermission(PermissionCatalog.Courses, "view")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await _courseService.ListAsync(cancellationToken));

    [HttpGet]
    [RequirePermission(PermissionCatalog.Courses, "view")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var course = await _courseService.GetAsync(id, cancellationToken);
        return course is null ? NotFound() : View(course);
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Courses, "create")]
    public IActionResult Create() => View(new CourseFormModel());

    [HttpPost]
    [RequirePermission(PermissionCatalog.Courses, "create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CourseFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _courseService.CreateAsync(model.ToInput(), CurrentActor, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Status"] = $"Đã thêm học phần {result.Value.Code}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Courses, "edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var course = await _courseService.GetAsync(id, cancellationToken);
        return course is null ? NotFound() : View(CourseFormModel.FromCourse(course));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Courses, "edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, CourseFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _courseService.UpdateAsync(id, model.ToInput(), CurrentActor, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Status"] = $"Đã cập nhật học phần {result.Value.Code}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Courses, "delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var course = await _courseService.GetAsync(id, cancellationToken);
        return course is null ? NotFound() : View(course);
    }

    [HttpPost]
    [ActionName(nameof(Delete))]
    [RequirePermission(PermissionCatalog.Courses, "delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        var result = await _courseService.DeleteAsync(id, CurrentActor, cancellationToken);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Errors.FirstOrDefault()?.Message ?? "Không xoá được học phần.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Status"] = "Đã xoá học phần.";
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
