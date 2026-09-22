using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TSolve.Demo.Models;
using TSolve.Demo.Options;
using TSolve.Demo.Services;
using TSolve.Demo.Services.Jira;

namespace TSolve.Demo.Tests;

public sealed class PipelineScenarioTests
{
    [Fact]
    public async Task BulkThenDaily_ProducesSmallReviewQueueAndReusableKnowledge()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new JiraOptions { Mode = "Mock", BulkLimit = 500, DailyLimit = 10 });
        var store = new DemoStore();
        var cleaner = new TextProcessingService();
        var mock = new MockJiraClient();
        var real = new RealJiraClient(new HttpClient(), options);
        var selector = new JiraClientSelector(mock, real, options);
        var pipeline = new KnowledgePipelineService(store, cleaner, selector, options, NullLogger<KnowledgePipelineService>.Instance);

        var bulk = await pipeline.ImportAsync(ImportMode.Bulk, CancellationToken.None);
        Assert.Equal(500, bulk.Imported);
        Assert.InRange(bulk.DraftsCreated, 1, 25);
        Assert.True(bulk.ManagerReviewsCreated < bulk.DraftsCreated);
        Assert.True(1d - (double)bulk.DraftsCreated / bulk.Imported >= .90);

        var reviewIds = await store.ReadAsync(x => x.Reviews.Select(review => review.Id).ToList());
        foreach (var reviewId in reviewIds)
            Assert.True(await pipeline.DecideReviewAsync(reviewId, ReviewDecision.Approved, "Automated scenario approval"));

        var daily = await pipeline.ImportAsync(ImportMode.Daily, CancellationToken.None);
        Assert.Equal(10, daily.Imported);
        Assert.True(daily.AutoLinked + daily.Duplicates > 0);
        Assert.InRange(daily.DraftsCreated, 0, 3);

        var snapshot = await store.ReadAsync(x => new { x.Tickets.Count, Published = x.Solutions.Count(s => s.Status == SolutionStatus.Published) });
        Assert.Equal(510, snapshot.Count);
        Assert.True(snapshot.Published > 0);
    }
}
