using Microsoft.Extensions.Options;
using TSolve.Demo.Models;
using TSolve.Demo.Options;
using TSolve.Demo.Services.Jira;

namespace TSolve.Demo.Services;

public sealed class KnowledgePipelineService(
    IStateStore store,
    TextProcessingService text,
    JiraClientSelector jiraSelector,
    IOptions<JiraOptions> jiraOptions,
    ILogger<KnowledgePipelineService> logger,
    IOptions<PipelineOptions>? configuredPipelineOptions = null,
    TwoStageSynthesisService? configuredSynthesis = null)
{
    private readonly JiraOptions _jiraOptions = jiraOptions.Value;
    private readonly PipelineOptions _pipelineOptions = configuredPipelineOptions?.Value ?? new PipelineOptions();
    private readonly TwoStageSynthesisService _synthesis = configuredSynthesis ??
        new TwoStageSynthesisService(text, new InternalSafeRewriter(), configuredPipelineOptions);

    public async Task<PipelineRun> ImportAsync(ImportMode mode, CancellationToken cancellationToken)
    {
        var dailySyncNumber = await store.ReadAsync(state => state.DailySyncNumber);
        var limit = mode == ImportMode.Bulk ? _jiraOptions.BulkLimit : _jiraOptions.DailyLimit;
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
            var currentRun = new PipelineRun
            {
                Mode = mode,
                Connector = connector,
                QueueName = mode == ImportMode.Bulk ? "BACKFILL_QUEUE" : "DAILY_QUEUE",
                Received = sourceTickets.Count
            };
            IEnumerable<SourceTicket> processingOrder = mode == ImportMode.Bulk
                ? sourceTickets.OrderBy(source => source.Source, StringComparer.Ordinal).ThenBy(source => source.ExternalId, StringComparer.Ordinal)
                : sourceTickets;
            foreach (var source in processingOrder) ProcessTicket(state, source, currentRun, mode == ImportMode.Daily);

            if (mode == ImportMode.Bulk)
            {
                ClusterBulkCandidates(state);
                AttachBulkDuplicates(state);
                PromoteBestBulkClusters(state, currentRun);
            }
            else PromoteReadyDailyClusters(state, currentRun);

            currentRun.CompletedAt = DateTimeOffset.UtcNow;
            state.Runs.Insert(0, currentRun);
            if (mode == ImportMode.Daily) state.DailySyncNumber++;
            state.Audit.Insert(0, new AuditEntry
            {
                Action = connector.StartsWith("Excel:", StringComparison.Ordinal)
                    ? "EXCEL_IMPORT_COMPLETED"
                    : mode == ImportMode.Bulk ? "BULK_IMPORT_COMPLETED" : "DAILY_SYNC_COMPLETED",
                Resource = currentRun.Id.ToString(),
                Detail = $"Queue {currentRun.QueueName}; received {currentRun.Received}, imported {currentRun.Imported}, created {currentRun.DraftsCreated} drafts"
            });
            return currentRun;
        });

        logger.LogInformation("{Queue} import completed: {Imported}/{Received}, {Drafts} drafts", run.QueueName, run.Imported, run.Received, run.DraftsCreated);
        return run;
    }

    public Task<bool> DecideReviewAsync(Guid reviewId, ReviewDecision decision, string reason)
    {
        return store.WriteAsync(state =>
        {
            var review = state.Reviews.FirstOrDefault(item => item.Id == reviewId && item.Decision == ReviewDecision.Pending);
            if (review is null) return false;
            var solution = state.Solutions.First(item => item.Id == review.SolutionId);
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

    private void ProcessTicket(DemoState state, SourceTicket source, PipelineRun run, bool clusterImmediately)
    {
        if (state.Tickets.Any(ticket => ticket.Source == source.Source && ticket.ExternalId == source.ExternalId))
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
            Application = source.Application,
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

        var exactDuplicate = state.Tickets.FirstOrDefault(other =>
            other.Id != ticket.Id && other.ContentHash == ticket.ContentHash && other.Workspace == ticket.Workspace);
        if (exactDuplicate is not null)
        {
            MarkDuplicate(state, ticket, exactDuplicate, 1, "Exact normalized duplicate", run);
            return;
        }

        var nearDuplicate = FindNearDuplicate(state, ticket);
        if (nearDuplicate.Ticket is not null)
        {
            MarkDuplicate(state, ticket, nearDuplicate.Ticket, nearDuplicate.Score, "Near duplicate from local text embeddings", run);
            return;
        }

        var publishedMatch = FindPublishedMatch(state, ticket);
        if (publishedMatch.Solution is not null && publishedMatch.Score >= _pipelineOptions.PublishedMatchThreshold && ticket.Risk != RiskLevel.High)
        {
            ticket.Decision = ProcessingDecision.AutoLinked;
            ticket.LinkedSolutionId = publishedMatch.Solution.Id;
            ticket.MatchConfidence = publishedMatch.Score;
            ticket.DecisionReason = $"Matched published solution at {publishedMatch.Score:P0}; threshold {_pipelineOptions.PublishedMatchThreshold:P0}";
            publishedMatch.Solution.UsageCount++;
            run.AutoLinked++;
            return;
        }

        ticket.Decision = ProcessingDecision.CandidateEligible;
        run.CandidateEligible++;
        if (clusterImmediately) AddToCluster(state, ticket);
    }

    private void MarkDuplicate(DemoState state, TicketRecord ticket, TicketRecord duplicate, double score, string method, PipelineRun run)
    {
        ticket.Decision = ProcessingDecision.Duplicate;
        ticket.DecisionReason = $"{method} of {duplicate.ExternalId} at {score:P0}";
        ticket.DuplicateOfTicketId = duplicate.Id;
        ticket.ClusterId = duplicate.ClusterId;
        ticket.SimilarityScore = score;
        if (method.StartsWith("Near duplicate", StringComparison.Ordinal) && duplicate.ClusterId is Guid clusterId)
        {
            var cluster = state.Clusters.First(item => item.Id == clusterId);
            if (!cluster.TicketIds.Contains(ticket.Id)) cluster.TicketIds.Add(ticket.Id);
            RecalculateCluster(state, cluster);
        }
        state.SimilarityLinks.Add(new SimilarityLink
        {
            SourceTicketId = ticket.Id,
            TargetTicketId = duplicate.Id,
            Method = method,
            Score = score
        });
        run.Duplicates++;
    }

    private (TicketRecord? Ticket, double Score) FindNearDuplicate(DemoState state, TicketRecord ticket)
    {
        var ticketText = TicketText(ticket);
        return state.Tickets
            .Where(other => other.Id != ticket.Id && other.Workspace == ticket.Workspace && other.Category == ticket.Category && other.Subcategory == ticket.Subcategory)
            .Select(other =>
            {
                var titleScore = text.TitleSimilarity(ticket.CleanTitle, other.CleanTitle);
                var resolutionScore = text.Similarity(ticket.CleanResolution, other.CleanResolution);
                var contentScore = text.Similarity(ticketText, TicketText(other));
                var score = resolutionScore * 0.60 + titleScore * 0.25 + contentScore * 0.15;
                return (Ticket: other, Score: score, TitleScore: titleScore, ResolutionScore: resolutionScore);
            })
            .Where(match => match.Score >= _pipelineOptions.NearDuplicateThreshold &&
                            (match.TitleScore >= _pipelineOptions.NearDuplicateTitleThreshold ||
                             match.ResolutionScore >= _pipelineOptions.NearDuplicateThreshold))
            .OrderByDescending(match => match.Score)
            .Select(match => (match.Ticket, match.Score))
            .FirstOrDefault();
    }

    private (KnowledgeSolution? Solution, double Score) FindPublishedMatch(DemoState state, TicketRecord ticket)
    {
        return state.Solutions
            .Where(solution => solution.Status == SolutionStatus.Published && solution.Workspace == ticket.Workspace && solution.ReviewDueAt > DateTimeOffset.UtcNow)
            .Select(solution => (Solution: solution, Score: text.Similarity(TicketText(ticket), $"{solution.Title} {solution.Problem} {solution.Procedure}")))
            .OrderByDescending(match => match.Score)
            .FirstOrDefault();
    }

    private void AddToCluster(DemoState state, TicketRecord ticket)
    {
        var ticketText = TicketText(ticket);
        var candidate = state.Clusters
            .Where(cluster => cluster.Workspace == ticket.Workspace && cluster.Category == ticket.Category && cluster.Subcategory == ticket.Subcategory && !cluster.Promoted)
            .Select(cluster =>
            {
                var scores = state.Tickets
                    .Where(member => cluster.TicketIds.Contains(member.Id))
                    .Select(member => ClusterSimilarity(ticket, member))
                    .ToArray();
                return (Cluster: cluster, Score: scores.Length == 0 ? 0 : scores.Min());
            })
            .Where(match => match.Score >= _pipelineOptions.ClusterSimilarityThreshold)
            .OrderByDescending(match => match.Score)
            .FirstOrDefault();

        var cluster = candidate.Cluster;
        if (cluster is null)
        {
            cluster = new TicketCluster
            {
                Workspace = ticket.Workspace,
                Category = ticket.Category,
                Subcategory = ticket.Subcategory,
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

    private void ClusterBulkCandidates(DemoState state)
    {
        var candidates = state.Tickets
            .Where(ticket => ticket.Decision == ProcessingDecision.CandidateEligible && ticket.ClusterId is null)
            .OrderBy(ticket => ticket.ExternalId, StringComparer.Ordinal)
            .GroupBy(ticket => (ticket.Workspace, ticket.Category, ticket.Subcategory));

        foreach (var taxonomy in candidates)
        {
            var similarities = new Dictionary<(Guid Left, Guid Right), double>();
            double Similarity(TicketRecord left, TicketRecord right)
            {
                var key = left.Id.CompareTo(right.Id) <= 0 ? (left.Id, right.Id) : (right.Id, left.Id);
                if (!similarities.TryGetValue(key, out var score))
                    similarities[key] = score = ClusterSimilarity(left, right);
                return score;
            }

            var partitions = taxonomy.Select(ticket => new List<TicketRecord> { ticket }).ToList();
            while (partitions.Count > 1)
            {
                var bestLeft = -1;
                var bestRight = -1;
                var bestScore = _pipelineOptions.ClusterSimilarityThreshold;
                for (var left = 0; left < partitions.Count; left++)
                for (var right = left + 1; right < partitions.Count; right++)
                {
                    // True complete-link: the least-similar cross-pair controls whether two groups may merge.
                    var score = partitions[left].SelectMany(a => partitions[right].Select(b => Similarity(a, b))).Min();
                    if (score > bestScore + 1e-12)
                    {
                        bestScore = score;
                        bestLeft = left;
                        bestRight = right;
                    }
                }
                if (bestLeft < 0) break;
                partitions[bestLeft].AddRange(partitions[bestRight]);
                partitions[bestLeft] = partitions[bestLeft].OrderBy(ticket => ticket.ExternalId, StringComparer.Ordinal).ToList();
                partitions.RemoveAt(bestRight);
            }

            foreach (var members in partitions)
            {
                var representative = members
                    .OrderByDescending(candidate => members.Average(other => Similarity(candidate, other)))
                    .ThenBy(candidate => candidate.ExternalId, StringComparer.Ordinal).First();
                var cluster = new TicketCluster
                {
                    Workspace = taxonomy.Key.Workspace,
                    Category = taxonomy.Key.Category,
                    Subcategory = taxonomy.Key.Subcategory,
                    Name = text.BuildClusterName(representative),
                    RepresentativeText = TicketText(representative),
                    Risk = representative.Risk
                };
                foreach (var member in members)
                {
                    cluster.TicketIds.Add(member.Id);
                    member.ClusterId = cluster.Id;
                }
                state.Clusters.Add(cluster);
                RecalculateCluster(state, cluster);
            }
        }
    }

    private static void AttachBulkDuplicates(DemoState state)
    {
        var pending = state.Tickets.Where(ticket => ticket.Decision == ProcessingDecision.Duplicate && ticket.ClusterId is null).ToList();
        for (var pass = 0; pass < pending.Count && pending.Count > 0; pass++)
        {
            var attached = 0;
            foreach (var duplicate in pending.ToList())
            {
                var original = state.Tickets.FirstOrDefault(ticket => ticket.Id == duplicate.DuplicateOfTicketId);
                if (original?.ClusterId is not Guid clusterId) continue;
                var cluster = state.Clusters.First(item => item.Id == clusterId);
                duplicate.ClusterId = clusterId;
                if (!cluster.TicketIds.Contains(duplicate.Id)) cluster.TicketIds.Add(duplicate.Id);
                pending.Remove(duplicate);
                attached++;
            }
            if (attached == 0) break;
        }
        foreach (var cluster in state.Clusters) RecalculateCluster(state, cluster);
    }

    private static void RecalculateCluster(DemoState state, TicketCluster cluster)
    {
        var members = state.Tickets.Where(ticket => cluster.TicketIds.Contains(ticket.Id)).ToList();
        cluster.AverageQuality = members.Count == 0 ? 0 : members.Average(ticket => ticket.QualityScore);
        cluster.Risk = members.Count == 0 ? RiskLevel.Low : members.Max(ticket => ticket.Risk);
        var maxRiskCount = members.Count(ticket => ticket.Risk == cluster.Risk);
        cluster.RiskReason = $"MAX strategy: {cluster.Risk} is the highest member risk ({maxRiskCount}/{members.Count} ticket(s)).";
        var frequency = Math.Min(100, members.Count * 8);
        var recency = members.Count == 0 ? 0 : Math.Max(0, 100 - (DateTimeOffset.UtcNow - members.Max(ticket => ticket.ResolvedAt)).TotalDays);
        cluster.KnowledgeValue = Math.Round(frequency * .45 + cluster.AverageQuality * .4 + recency * .15, 1);
    }

    private void PromoteBestBulkClusters(DemoState state, PipelineRun run)
    {
        var eligible = state.Clusters
            .Where(cluster => !cluster.Promoted && cluster.AverageQuality >= _pipelineOptions.PromotionMinAverageQuality &&
                (cluster.TicketIds.Count >= _pipelineOptions.PromotionMinEvidence ||
                 cluster.Risk == RiskLevel.High && cluster.TicketIds.Count >= _pipelineOptions.HighRiskPromotionMinEvidence))
            .OrderByDescending(cluster => cluster.KnowledgeValue)
            .Take(_pipelineOptions.BulkPromotionLimit)
            .ToList();
        foreach (var cluster in eligible) Promote(state, cluster, run, "Top-ranked content-similar cluster from historical import");
    }

    private void PromoteReadyDailyClusters(DemoState state, PipelineRun run)
    {
        var eligible = state.Clusters
            .Where(cluster => !cluster.Promoted && cluster.SolutionId is null &&
                              cluster.TicketIds.Count >= _pipelineOptions.PromotionMinEvidence &&
                              cluster.AverageQuality >= _pipelineOptions.PromotionMinAverageQuality)
            .OrderByDescending(cluster => cluster.Risk)
            .ThenByDescending(cluster => cluster.KnowledgeValue)
            .Take(_pipelineOptions.DailyPromotionLimit)
            .ToList();
        foreach (var cluster in eligible) Promote(state, cluster, run, "Daily content-similar cluster reached evidence threshold");
    }

    private void Promote(DemoState state, TicketCluster cluster, PipelineRun run, string reason)
    {
        var tickets = state.Tickets.Where(ticket => cluster.TicketIds.Contains(ticket.Id)).OrderByDescending(ticket => ticket.QualityScore).ToList();
        if (tickets.Count == 0 || cluster.SolutionId is not null) return;

        var (synthesis, aiRun) = _synthesis.Synthesize(cluster, tickets);
        state.AIRuns.Add(aiRun);
        if (synthesis.InsufficientEvidence)
        {
            cluster.PromotionReason = "Insufficient evidence: conflicting or unsupported resolutions";
            return;
        }
        var solution = new KnowledgeSolution
        {
            ClusterId = cluster.Id,
            Workspace = cluster.Workspace,
            Title = cluster.Name,
            Problem = synthesis.Problem,
            Procedure = synthesis.Procedure,
            Applicability = synthesis.Applicability,
            Warning = cluster.Risk == RiskLevel.High
                ? $"High-risk knowledge: verify policy, authorization and current environment before reuse. {synthesis.Warning}"
                : synthesis.Warning,
            Risk = cluster.Risk,
            Status = SolutionStatus.InReview,
            SourceTicketCount = tickets.Count,
            SourceTicketIds = tickets.Select(ticket => ticket.Id).ToList(),
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

    private double ClusterSimilarity(TicketRecord left, TicketRecord right) =>
        text.TitleSimilarity(left.CleanTitle, right.CleanTitle) * 0.45 +
        text.Similarity(left.CleanDescription, right.CleanDescription) * 0.20 +
        text.Similarity(left.CleanResolution, right.CleanResolution) * 0.35;

    private static string TicketText(TicketRecord ticket) => $"{ticket.CleanTitle} {ticket.CleanDescription} {ticket.CleanResolution}";
}
