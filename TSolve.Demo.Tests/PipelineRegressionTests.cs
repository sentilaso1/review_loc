using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TSolve.Demo.Models;
using TSolve.Demo.Options;
using TSolve.Demo.Services;
using TSolve.Demo.Services.Jira;

namespace TSolve.Demo.Tests;

public sealed class PipelineRegressionTests
{
    [Fact]
    public async Task ClusterRisk_UsesMaximumMemberRisk_AndRecordsReason()
    {
        var (pipeline, store) = Pipeline(new PipelineOptions { NearDuplicateThreshold = 0.99, ClusterSimilarityThreshold = 0.22 });
        await pipeline.ImportExcelAsync([
            Ticket("R-1", "Dashboard", "Cannot update task", "1. Check task data. 2. Update the end date. 3. Verify the task."),
            Ticket("R-2", "Dashboard", "Cannot update task in production", "1. Check task data. 2. Update the end date. 3. Verify the task."),
            Ticket("R-3", "Dashboard", "Task update fails", "1. Check task data. 2. Update the end date. 3. Verify the task.")
        ], "risk.xlsx", CancellationToken.None);

        var state = await store.ReadAsync(value => value);
        var cluster = Assert.Single(state.Clusters.Where(item => item.TicketIds.Count >= 2));
        var memberRisks = state.Tickets.Where(ticket => cluster.TicketIds.Contains(ticket.Id)).Select(ticket => ticket.Risk);
        Assert.Equal(memberRisks.Max(), cluster.Risk);
        Assert.Contains("MAX strategy", cluster.RiskReason);
    }

    [Fact]
    public async Task Clustering_DoesNotJoinUnrelatedTicketsThatOnlyShareTaxonomy()
    {
        var (pipeline, store) = Pipeline();
        await pipeline.ImportExcelAsync([
            Ticket("C-1", "", "Printer queue stops", "1. Clear the print queue. 2. Restart spooler. 3. Verify printing."),
            Ticket("C-2", "", "Printer queue is blocked", "1. Clear the print queue. 2. Restart spooler. 3. Verify printing."),
            Ticket("C-3", "", "Meeting room display fails", "1. Disconnect HDMI cable. 2. Reconnect the display. 3. Verify the screen.")
        ], "clusters.xlsx", CancellationToken.None);

        var state = await store.ReadAsync(value => value);
        var printerCluster = state.Clusters.Single(cluster => cluster.TicketIds.Contains(state.Tickets.Single(ticket => ticket.ExternalId == "C-1").Id));
        var displayTicket = state.Tickets.Single(ticket => ticket.ExternalId == "C-3");
        Assert.DoesNotContain(displayTicket.Id, printerCluster.TicketIds);
    }

    [Fact]
    public async Task NearDuplicate_FindsTheTwoMissedHrmsAttendanceTickets()
    {
        var (pipeline, store) = Pipeline(new PipelineOptions { NearDuplicateThreshold = 0.55, NearDuplicateTitleThreshold = 0.25 });
        const string common = "<p>Truy cập Server máy cấm công -&gt; Download các máy chấm công nhân sự chấm -&gt; nếu có dữ liệu mới thì sẽ tự động đồng bộ về Timesheet -&gt; sau khi đồng bộ chạy lại tính công</p>";
        await pipeline.ImportExcelAsync([
            Ticket("15444", "HRMS/TMS System", "Nhờ check dữ liệu chấm công ngày 17/9/2026", $"[Comment 1] {common} ----- [Comment 2] <p>AMS đã hỗ trợ</p>"),
            Ticket("15434", "HRMS/TMS System", "Nhờ kiểm tra ngày công 15/09/2026", $"[Comment 1] {common} ----- [Comment 2] <p>AMS đã hỗ trợ</p> ----- [Comment 3] <p>Cảm ơn anh/chị đã liên hệ với AMS</p>")
        ], "near-duplicates.xlsx", CancellationToken.None);

        var state = await store.ReadAsync(value => value);
        var duplicate = state.Tickets.Single(ticket => ticket.ExternalId == "15434");
        Assert.Equal(ProcessingDecision.Duplicate, duplicate.Decision);
        Assert.NotNull(duplicate.DuplicateOfTicketId);
        Assert.Contains(state.SimilarityLinks, link => link.SourceTicketId == duplicate.Id && link.Method.Contains("Near duplicate"));
    }

    [Fact]
    public async Task DraftSynthesis_UsesCrossTicketEvidence_AndKeepsPiiOut()
    {
        var (pipeline, store) = Pipeline(new PipelineOptions { ClusterSimilarityThreshold = 0.30, NearDuplicateThreshold = 0.99 });
        await pipeline.ImportExcelAsync([
            Ticket("S-1", "Dashboard", "Task end date cannot update", "@Alice - CMC Global: 1. Check task permissions. 2. Update the end date. 3. Verify the task."),
            Ticket("S-2", "Dashboard", "Unable to update task end date", "alice@example.com 1. Check task permissions. 2. Update the end date. 3. Verify the task."),
            Ticket("S-3", "Dashboard", "Task end date update error", "1. Check task permissions. 2. Update the end date. 3. Verify the task.")
        ], "synthesis.xlsx", CancellationToken.None);

        var state = await store.ReadAsync(value => value);
        var solution = Assert.Single(state.Solutions);
        Assert.Equal(SolutionStatus.InReview, solution.Status);
        Assert.Equal(3, solution.SourceTicketIds.Count);
        Assert.DoesNotContain("@", solution.Problem + solution.Procedure);
        Assert.DoesNotContain("example.com", solution.Problem + solution.Procedure, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Comment", solution.Procedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supported by", solution.Procedure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cleaning_MasksMentionsEmailsLinks_AndUsesApplicationTaxonomy()
    {
        var cleaner = new TextProcessingService();
        var result = cleaner.Clean(Ticket("P-1", "CRM System", "Không add được payment vào invoice", "@Bich. Dinh - CMC Global APMO xem alice@example.com tại https://internal.cmcglobal.vn/item/1. 1. Check invoice. 2. Add payment. 3. Verify."));
        var clean = result.Title + result.Description + result.Resolution;
        Assert.DoesNotContain("@", clean);
        Assert.DoesNotContain("example.com", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal.cmcglobal.vn", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("CRM", result.Category);
        Assert.Equal("INVOICE", result.Subcategory);
    }

    [Fact]
    public void QualityThreshold_IsConfigurableAndExplained()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PipelineOptions { CandidateQualityThreshold = 90 });
        var result = new TextProcessingService(options).Clean(Ticket("Q-1", "Dashboard", "Task update error", "1. Check data. 2. Update task. 3. Verify result."));
        Assert.Equal(ProcessingDecision.SearchOnly, result.Decision);
        Assert.Contains("configured threshold 90", result.Reason);
    }

    [Fact]
    public async Task PublishedSolutionMatch_ReturnsConfidenceAndAutoLinks()
    {
        var (pipeline, store) = Pipeline(new PipelineOptions { PublishedMatchThreshold = 0.10 });
        await store.WriteAsync(state =>
        {
            state.Solutions.Add(new KnowledgeSolution
            {
                ClusterId = Guid.NewGuid(),
                Workspace = "GENERAL",
                Title = "Dashboard task end date",
                Problem = "Task end date cannot be updated",
                Procedure = "Check task permissions, update the end date, and verify the task.",
                Risk = RiskLevel.Low,
                Status = SolutionStatus.Published,
                ReviewDueAt = DateTimeOffset.UtcNow.AddMonths(3)
            });
            return true;
        });

        var run = await pipeline.ImportExcelAsync([
            Ticket("M-1", "Dashboard", "Task end date cannot be updated", "1. Check task permissions. 2. Update the end date. 3. Verify the task.")
        ], "match.xlsx", CancellationToken.None);

        var matched = await store.ReadAsync(state => state.Tickets.Single());
        Assert.Equal(1, run.AutoLinked);
        Assert.Equal(ProcessingDecision.AutoLinked, matched.Decision);
        Assert.True(matched.MatchConfidence > 0);
    }

    [Fact]
    public async Task BulkAndDailyImports_UseSeparateQueueNames()
    {
        var (pipeline, store) = Pipeline();
        await pipeline.ImportExcelAsync([Ticket("B-1", "Dashboard", "Task update error", "1. Check data. 2. Update task. 3. Verify result.")], "backfill.xlsx", CancellationToken.None);
        await pipeline.ImportAsync(ImportMode.Daily, CancellationToken.None);
        var queues = await store.ReadAsync(state => state.Runs.Select(run => run.QueueName).ToList());
        Assert.Contains("BACKFILL_QUEUE", queues);
        Assert.Contains("DAILY_QUEUE", queues);
    }

    private static (KnowledgePipelineService Pipeline, DemoStore Store) Pipeline(PipelineOptions? pipelineOptions = null)
    {
        var jiraOptions = Microsoft.Extensions.Options.Options.Create(new JiraOptions { Mode = "Mock", BulkLimit = 20, DailyLimit = 2 });
        var store = new DemoStore();
        var configuredPipelineOptions = Microsoft.Extensions.Options.Options.Create(pipelineOptions ?? new PipelineOptions());
        var cleaner = new TextProcessingService(configuredPipelineOptions);
        var selector = new JiraClientSelector(new MockJiraClient(), new RealJiraClient(new HttpClient(), jiraOptions), jiraOptions);
        return (new KnowledgePipelineService(store, cleaner, selector, jiraOptions, NullLogger<KnowledgePipelineService>.Instance, configuredPipelineOptions), store);
    }

    private static SourceTicket Ticket(string id, string application, string title, string resolution) => new()
    {
        Source = "Excel",
        Workspace = "GENERAL",
        ExternalId = id,
        SourceUrl = $"excel://fixture#row={id}",
        Title = title,
        Application = application,
        Description = $"Detailed context for {title} affecting multiple users and requiring a verified resolution.",
        Comments = [resolution],
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        ResolvedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
