using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TSolve.Demo.Models;
using TSolve.Demo.Options;
using TSolve.Demo.Services;

namespace TSolve.Demo.Controllers;

public class HomeController(
    ILogger<HomeController> logger,
    DemoStore store,
    KnowledgePipelineService pipeline,
    ExcelTicketReader excel,
    IOptions<JiraOptions> jiraOptions) : Controller
{
    public async Task<IActionResult> Index()
    {
        var state = await store.ReadAsync(x => x);
        var options = jiraOptions.Value;
        return View(new DashboardViewModel
        {
            State = state,
            Jira = new JiraConnectionSummary(options.IsRealConfigured, options.IsRealConfigured ? "Real Jira" : "Mock Jira", options.ProjectKey, options.IsRealConfigured ? options.BaseUrl : "Local deterministic simulator"),
            Notice = TempData["Notice"] as string,
            Error = TempData["Error"] as string
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> BulkImport(CancellationToken cancellationToken) => RunImport(ImportMode.Bulk, cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> DailySync(CancellationToken cancellationToken) => RunImport(ImportMode.Daily, cancellationToken);

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> ImportExcel(IFormFile? file, CancellationToken cancellationToken)
    {
        try
        {
            if (file is null || file.Length == 0) throw new InvalidDataException("Choose a non-empty .xlsx file.");
            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Only .xlsx files are supported.");

            await using var stream = file.OpenReadStream();
            var tickets = excel.Read(stream, file.FileName);
            var run = await pipeline.ImportExcelAsync(tickets, file.FileName, cancellationToken);
            TempData["Notice"] = $"Excel import completed: {run.Imported} imported, {run.IdempotentSkipped} already imported, {run.DraftsCreated} solution draft(s).";
        }
        catch (InvalidDataException exception)
        {
            TempData["Error"] = exception.Message;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Excel import failed");
            TempData["Error"] = "Excel import failed. Check the file and try again.";
        }

        return RedirectToAction(nameof(Index), null, "pipeline");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(Guid id, string decision, string? reason)
    {
        var parsed = decision.Equals("approve", StringComparison.OrdinalIgnoreCase) ? ReviewDecision.Approved : ReviewDecision.Rejected;
        var success = await pipeline.DecideReviewAsync(id, parsed, string.IsNullOrWhiteSpace(reason) ? "Demo review decision" : reason.Trim());
        TempData[success ? "Notice" : "Error"] = success
            ? parsed == ReviewDecision.Approved ? "Solution approved and published." : "Solution rejected."
            : "Review item was not found or was already decided.";
        return RedirectToAction(nameof(Index), null, "review-queue");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset()
    {
        await store.ResetAsync();
        TempData["Notice"] = "Demo state reset successfully.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> RunImport(ImportMode mode, CancellationToken cancellationToken)
    {
        try
        {
            var run = await pipeline.ImportAsync(mode, cancellationToken);
            TempData["Notice"] = $"{mode} completed: {run.Imported} imported, {run.DraftsCreated} solution drafts, {run.ManagerReviewsCreated} manager review(s).";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{Mode} import failed", mode);
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
