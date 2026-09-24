using Microsoft.AspNetCore.Mvc;
using TSolve.Demo.Models;
using TSolve.Demo.Services;

namespace TSolve.Demo.Controllers;

[ApiController]
[Route("api/demo")]
public sealed class DemoApiController(IStateStore store) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var result = await store.ReadAsync(state => new
        {
            tickets = state.Tickets.Count,
            blocked = state.Tickets.Count(x => x.Decision == ProcessingDecision.Blocked),
            searchOnly = state.Tickets.Count(x => x.Decision == ProcessingDecision.SearchOnly),
            duplicates = state.Tickets.Count(x => x.Decision == ProcessingDecision.Duplicate),
            autoLinked = state.Tickets.Count(x => x.Decision == ProcessingDecision.AutoLinked),
            clusters = state.Clusters.Count,
            candidatePool = state.Clusters.Count(x => !x.Promoted),
            solutionDrafts = state.Solutions.Count,
            publishedSolutions = state.Solutions.Count(x => x.Status == SolutionStatus.Published),
            pendingDomainReviews = state.Reviews.Count(x => x.Decision == ReviewDecision.Pending && x.RequiredRole == ReviewRole.DomainReviewer),
            pendingManagerReviews = state.Reviews.Count(x => x.Decision == ReviewDecision.Pending && x.RequiredRole == ReviewRole.ManagerSme),
            reviewReduction = state.Tickets.Count == 0 ? 0 : 1d - (double)state.Reviews.Count / state.Tickets.Count,
            latestRun = state.Runs.FirstOrDefault()
        });
        return Ok(result);
    }
}
