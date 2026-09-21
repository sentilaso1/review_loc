using TSolve.Demo.Models;
using TSolve.Demo.Options;
using TSolve.Demo.Services.Jira;
using Microsoft.Extensions.Options;

namespace TSolve.Demo.Services;

public sealed class KnowledgePipelineService(
    DemoStore store,
    TextProcessingService text,
    JiraClientSelector jiraSelector,
    IOptions<JiraOptions> options,
    ILogger<KnowledgePipelineService> logger)
{
    private readonly JiraOptions _options = options.Value;

    public async Task<PipelineRun> ImportAsync(ImportMode mode, CancellationToken cancellationToken)
    {
        var dailySyncNumber = await store.ReadAsync(x => x.DailySyncNumber);
        var limit = mode == ImportMode.Bulk ? _options.BulkLimit : _options.DailyLimit;
        var connector = jiraSelector.Current;
        var sourceTickets = await connector.GetResolvedTicketsAsync(mode, limit, dailySyncNumber, cancellationToken);

        return await ImportAsync(sourceTickets, mode, connector.Name, cancellationToken);
    }

    public Task<PipelineRun> ImportExcelAsync(IReadOnlyList<SourceTicket> sourceTickets, string fileName, CancellationToken cancellationToken)
        => ImportAsync(sourceTickets, ImportMode.Bulk, $"Excel: {Path.GetFileName(fileName)}", cancellationToken);

    private async Task<PipelineRun> ImportAsync(
        IReadOnlyList<SourceTicket> sourceTickets,
        ImportMode mode,
        string connector,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var run = await store.WriteAsync(state =>
        {
            var currentRun = new PipelineRun { Mode = mode, Connector = connector, Received = sourceTickets.Count };
            foreach (var source in sourceTickets)
                ProcessTicket(state, source, currentRun);

            if (mode == ImportMode.Bulk)
                PromoteBestBulkClusters(state, currentRun);
            else
                PromoteReadyDailyClusters(state, currentRun);

            currentRun.CompletedAt = DateTimeOffset.UtcNow;
            state.Runs.Insert(0, currentRun);
            if (mode == ImportMode.Daily) state.DailySyncNumber++;
            state.Audit.Insert(0, new AuditEntry
            {
                Action = connector.StartsWith("Excel:", StringComparison.Ordinal)
                    ? "EXCEL_IMPORT_COMPLETED"
                    : mode == ImportMode.Bulk ? "BULK_IMPORT_COMPLETED" : "DAILY_SYNC_COMPLETED",
                Resource = currentRun.Id.ToString(),
                Detail = $"Received {currentRun.Received}, imported {currentRun.Imported}, created {currentRun.DraftsCreated} drafts"
            });
            return currentRun;
        });

        logger.LogInformation("{Mode} import completed: {Imported}/{Received}, {Drafts} drafts", mode, run.Imported, run.Received, run.DraftsCreated);
        return run;
    }

    public Task<bool> DecideReviewAsync(Guid reviewId, ReviewDecision decision, string reason)
    {
        return store.WriteAsync(state =>
        {
            var review = state.Reviews.FirstOrDefault(x => x.Id == reviewId && x.Decision == ReviewDecision.Pending);
            if (review is null) return false;
            var solution = state.Solutions.First(x => x.Id == review.SolutionId);
            review.Decision = decision;
            review.DecisionReason = reason;
            review.DecidedAt = DateTimeOffset.UtcNow;
            solution.Status = decision == ReviewDecision.Approved ? SolutionStatus.Published : SolutionStatus.Rejected;
            if (decision == ReviewDecision.Approved) solution.PublishedAt = DateTimeOffset.UtcNow;
            state.Audit.Insert(0, new AuditEntry
            {
                Action = decision == ReviewDecision.Approved ? "SOLUTION_PUBLISHED" : "SOLUTION_REJECTED",
                Resource = solution.Id.ToString(),
                Detail = $"{review.RequiredRole}: {reason}"
            });
            return true;
        });
    }

    private void ProcessTicket(DemoState state, SourceTicket source, PipelineRun run)
    {
        if (state.Tickets.Any(x => x.Source == source.Source && x.ExternalId == source.ExternalId))
        {
            run.IdempotentSkipped++;
            return;
        }

        var clean = text.Clean(source);
        var ticket = new TicketRecord
        {
            Source = source.Source,
            ExternalId = source.ExternalId,
            SourceUrl = source.SourceUrl,
            Title = source.Title,
            RawJson = source.RawJson,
            CleanTitle = clean.Title,
            CleanDescription = clean.Description,
            CleanResolution = clean.Resolution,
            Status = source.Status,
            IssueType = source.IssueType,
            Workspace = source.Workspace,
            Labels = source.Labels,
            Category = clean.Category,
            Subcategory = clean.Subcategory,
            QualityScore = clean.QualityScore,
            Risk = clean.Risk,
            Decision = clean.Decision,
            DecisionReason = clean.Reason,
            ContentHash = text.Hash($"{clean.Title.ToLowerInvariant()}|{clean.Resolution.ToLowerInvariant()}"),
            CreatedAt = source.CreatedAt,
            ResolvedAt = source.ResolvedAt
        };

        state.Tickets.Add(ticket);
        run.Imported++;
        if (ticket.Decision == ProcessingDecision.Blocked) { run.Blocked++; return; }
        if (ticket.Decision == ProcessingDecision.SearchOnly) { run.SearchOnly++; return; }

        var duplicate = state.Tickets.FirstOrDefault(x => x.Id != ticket.Id && x.ContentHash == ticket.ContentHash && x.Workspace == ticket.Workspace);
        if (duplicate is not null)
        {
            ticket.Decision = ProcessingDecision.Duplicate;
            ticket.DecisionReason = $"Exact normalized duplicate of {duplicate.ExternalId}";
            ticket.DuplicateOfTicketId = duplicate.Id;
            ticket.ClusterId = duplicate.ClusterId;
            run.Duplicates++;
            return;
        }

        var publishedMatch = FindPublishedMatch(state, ticket);
        // Jaccard is intentionally conservative because scope/workspace/risk guards are checked separately.
        // On the deterministic evaluation set, 0.30 separates same-resolution cases from unrelated cases.
        if (publishedMatch.Solution is not null && publishedMatch.Score >= 0.30 && ticket.Risk != RiskLevel.High)
        {
            ticket.Decision = ProcessingDecision.AutoLinked;
            ticket.LinkedSolutionId = publishedMatch.Solution.Id;
            ticket.MatchConfidence = publishedMatch.Score;
            ticket.DecisionReason = $"Matched published solution at {publishedMatch.Score:P0}";
            publishedMatch.Solution.UsageCount++;
            run.AutoLinked++;
            return;
        }

        ticket.Decision = ProcessingDecision.CandidateEligible;
        run.CandidateEligible++;
        AddToCluster(state, ticket);
    }

    private (KnowledgeSolution? Solution, double Score) FindPublishedMatch(DemoState state, TicketRecord ticket)
    {
        return state.Solutions
            .Where(x => x.Status == SolutionStatus.Published && x.Workspace == ticket.Workspace && x.ReviewDueAt > DateTimeOffset.UtcNow)
            .Select(x => (Solution: x, Score: text.Similarity($"{ticket.CleanTitle} {ticket.CleanResolution}", $"{x.Title} {x.Problem} {x.Procedure}")))
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();
    }

    private void AddToCluster(DemoState state, TicketRecord ticket)
    {
        var ticketText = $"{ticket.CleanTitle} {ticket.CleanDescription} {ticket.CleanResolution}";
        var candidate = state.Clusters
            .Where(x => x.Workspace == ticket.Workspace && x.Category == ticket.Category)
            .Select(x => (Cluster: x, Score: text.Similarity(ticketText, x.RepresentativeText)))
            .Where(x => x.Score >= 0.34)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        var cluster = candidate.Cluster;
        if (cluster is null)
        {
            cluster = new TicketCluster
            {
                Workspace = ticket.Workspace,
                Category = ticket.Category,
                Name = text.BuildClusterName(ticket),
                RepresentativeText = ticketText,
                Risk = ticket.Risk
            };
            state.Clusters.Add(cluster);
        }

        cluster.TicketIds.Add(ticket.Id);
        ticket.ClusterId = cluster.Id;
        RecalculateCluster(state, cluster);
    }

    private static void RecalculateCluster(DemoState state, TicketCluster cluster)
    {
        var members = state.Tickets.Where(x => cluster.TicketIds.Contains(x.Id)).ToList();
        cluster.AverageQuality = members.Count == 0 ? 0 : members.Average(x => x.QualityScore);
        cluster.Risk = members.Any(x => x.Risk == RiskLevel.High) ? RiskLevel.High : members.Any(x => x.Risk == RiskLevel.Medium) ? RiskLevel.Medium : RiskLevel.Low;
        var frequency = Math.Min(100, members.Count * 8);
        var recency = members.Count == 0 ? 0 : Math.Max(0, 100 - (DateTimeOffset.UtcNow - members.Max(x => x.ResolvedAt)).TotalDays);
        cluster.KnowledgeValue = Math.Round(frequency * .45 + cluster.AverageQuality * .4 + recency * .15, 1);
    }

    private void PromoteBestBulkClusters(DemoState state, PipelineRun run)
    {
        var eligible = state.Clusters
            .Where(x => !x.Promoted && x.AverageQuality >= 55 &&
                        (x.TicketIds.Count >= 3 || (x.Risk == RiskLevel.High && x.TicketIds.Count >= 2)))
            .OrderByDescending(x => x.KnowledgeValue)
            .Take(24)
            .ToList();
        foreach (var cluster in eligible) Promote(state, cluster, run, "Top-ranked cluster from historical import");
    }

    private void PromoteReadyDailyClusters(DemoState state, PipelineRun run)
    {
        var eligible = state.Clusters
            .Where(x => !x.Promoted && x.SolutionId is null && x.TicketIds.Count >= 3 && x.AverageQuality >= 55)
            .OrderByDescending(x => x.Risk)
            .ThenByDescending(x => x.KnowledgeValue)
            .Take(3)
            .ToList();
        foreach (var cluster in eligible) Promote(state, cluster, run, "Daily cluster reached evidence threshold");
    }

    private void Promote(DemoState state, TicketCluster cluster, PipelineRun run, string reason)
    {
        var tickets = state.Tickets.Where(x => cluster.TicketIds.Contains(x.Id)).OrderByDescending(x => x.QualityScore).ToList();
        if (tickets.Count == 0 || cluster.SolutionId is not null) return;
        var best = tickets[0];
        var solution = new KnowledgeSolution
        {
            ClusterId = cluster.Id,
            Workspace = cluster.Workspace,
            Title = cluster.Name,
            Problem = best.CleanDescription.Length > 0 ? best.CleanDescription : best.CleanTitle,
            Procedure = best.CleanResolution,
            Applicability = $"Workspace {cluster.Workspace}; category {cluster.Category}; supported by {tickets.Count} source ticket(s).",
            Warning = cluster.Risk == RiskLevel.High ? "High-risk knowledge: verify policy, authorization and current environment before reuse." : "Verify applicability before reuse.",
            Risk = cluster.Risk,
            Status = SolutionStatus.InReview,
            SourceTicketCount = tickets.Count,
            ReviewDueAt = DateTimeOffset.UtcNow.AddMonths(cluster.Risk == RiskLevel.High ? 3 : 6)
        };
        cluster.Promoted = true;
        cluster.PromotionReason = reason;
        cluster.SolutionId = solution.Id;
        state.Solutions.Add(solution);
        var role = cluster.Risk == RiskLevel.High ? ReviewRole.ManagerSme : ReviewRole.DomainReviewer;
        state.Reviews.Add(new ReviewTask { SolutionId = solution.Id, RequiredRole = role });
        run.DraftsCreated++;
        if (role == ReviewRole.ManagerSme) run.ManagerReviewsCreated++; else run.DomainReviewsCreated++;
    }
}
