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
        var duplicate = Assert.Single(state.Tickets.Where(ticket => ticket.Decision == ProcessingDecision.Duplicate));
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
        var aiRun = Assert.Single(state.AIRuns);
        Assert.True(aiRun.InputWasMasked);
        Assert.False(string.IsNullOrWhiteSpace(aiRun.PromptHash));
        Assert.Equal(solution.ClusterId, aiRun.ClusterId);
    }

    [Fact]
    public async Task BadgeTicket15178_IsNeverClusteredWithAttendanceDashboardOrPipelineTickets()
    {
        var (pipeline, store) = Pipeline(new PipelineOptions { NearDuplicateThreshold = 0.99, ClusterSimilarityThreshold = 0.20 });
        await pipeline.ImportExcelAsync([
            Ticket("15178", "", "Xin cấp dây thẻ", "1. Xác nhận thông tin nhân viên. 2. Cấp dây thẻ ra vào. 3. Bàn giao thẻ."),
            Ticket("15369", "", "Không ghi nhận giờ chấm công", "1. Tải dữ liệu máy chấm công. 2. Đồng bộ Timesheet. 3. Chạy lại tính công."),
            Ticket("15239", "", "HRMS không hiện ngày công", "1. Kiểm tra dữ liệu HRMS. 2. Đồng bộ Timesheet. 3. Xác minh ngày công."),
            Ticket("15435", "", "Dashboard không cập nhật cảnh báo", "1. Làm mới dữ liệu Dashboard. 2. Chạy tác vụ đồng bộ. 3. Kiểm tra biểu đồ."),
            Ticket("15451", "", "Lỗi khi log pipeline", "1. Kiểm tra pipeline CRM. 2. Chạy lại workflow. 3. Xác minh trạng thái.")
        ], "cluster-051b7e78.xlsx", CancellationToken.None);

        var state = await store.ReadAsync(value => value);
        var badge = state.Tickets.Single(ticket => ticket.ExternalId == "15178");
        Assert.Equal("ACCESS", badge.Category);
        Assert.Equal("PHYSICAL_BADGE", badge.Subcategory);
        Assert.DoesNotContain(state.Tickets.Where(ticket => ticket.ExternalId != "15178"), ticket => ticket.ClusterId == badge.ClusterId);
    }

    [Fact]
    public async Task BulkClustering_IsIndependentOfInputOrder()
    {
        var source = Enumerable.Range(1, 24).Select(index => (index % 3) switch
        {
            0 => Ticket($"O-{index:00}", "Dashboard", "Task end date cannot update", $"1. Check task permission. 2. Update end date. 3. Verify dashboard. Reference {index}."),
            1 => Ticket($"O-{index:00}", "HRMS/TMS System", "Timesheet attendance missing", $"1. Download attendance. 2. Sync timesheet. 3. Recalculate attendance. Reference {index}."),
            _ => Ticket($"O-{index:00}", "CRM System", "Invoice payment missing", $"1. Check invoice. 2. Add payment. 3. Verify billing status. Reference {index}.")
        }).ToArray();

        var normal = await ClusterSignature(source);
        var reverse = await ClusterSignature(source.Reverse().ToArray());
        var shuffled = await ClusterSignature(source.OrderBy(ticket => HashCode.Combine(ticket.ExternalId, 42)).ToArray());
        Assert.Equal(normal, reverse);
        Assert.Equal(normal, shuffled);
    }

    private static async Task<string> ClusterSignature(IReadOnlyList<SourceTicket> tickets)
    {
        var (pipeline, store) = Pipeline(new PipelineOptions { NearDuplicateThreshold = 1.01, NearDuplicateTitleThreshold = 1.01, ClusterSimilarityThreshold = 0.35, PromotionMinEvidence = 100 });
        await pipeline.ImportExcelAsync(tickets, "order.xlsx", CancellationToken.None);
        return await store.ReadAsync(state => string.Join("|", state.Clusters
            .Select(cluster => string.Join(",", state.Tickets.Where(ticket => cluster.TicketIds.Contains(ticket.Id)).Select(ticket => ticket.ExternalId).OrderBy(value => value)))
            .OrderBy(value => value)));
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
    public void Embedding_IsDeterministicNormalizedAnd384Dimensional()
    {
        var text = new TextProcessingService();
        var first = text.Embedding("Cannot update the dashboard task end date");
        var second = text.Embedding("Cannot update the dashboard task end date");

        Assert.Equal(384, first.Length);
        Assert.Equal(first, second);
        Assert.Equal(1, Math.Sqrt(first.Sum(value => value * value)), 10);
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
