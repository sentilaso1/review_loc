namespace TSolve.Demo.Models;

public enum ImportMode { Bulk, Daily }
public enum ProcessingDecision { Blocked, SearchOnly, CandidateEligible, AutoLinked, Duplicate }
public enum RiskLevel { Low, Medium, High }
public enum SolutionStatus { Draft, InReview, Published, Rejected, Deprecated }
public enum ReviewRole { DomainReviewer, ManagerSme }
public enum ReviewDecision { Pending, Approved, Rejected }

public sealed class SourceTicket
{
    public required string Source { get; init; }
    public required string ExternalId { get; init; }
    public required string SourceUrl { get; init; }
    public required string Title { get; init; }
    public string Application { get; init; } = "";
    public string Description { get; init; } = "";
    public string Resolution { get; init; } = "";
    public string Status { get; init; } = "Resolved";
    public string IssueType { get; init; } = "Task";
    public string Workspace { get; init; } = "IT";
    public IReadOnlyList<string> Labels { get; init; } = [];
    public IReadOnlyList<string> Comments { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ResolvedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string RawJson { get; init; } = "";
}

public sealed class TicketRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Source { get; init; }
    public required string ExternalId { get; init; }
    public required string SourceUrl { get; init; }
    public required string Title { get; init; }
    public string Application { get; init; } = "";
    public string RawJson { get; init; } = "";
    public required string CleanTitle { get; set; }
    public required string CleanDescription { get; set; }
    public required string CleanResolution { get; set; }
    public string Status { get; init; } = "Resolved";
    public string IssueType { get; init; } = "Task";
    public string Workspace { get; init; } = "IT";
    public string Category { get; set; } = "GENERAL";
    public string Subcategory { get; set; } = "OTHER";
    public IReadOnlyList<string> Labels { get; init; } = [];
    public int QualityScore { get; set; }
    public RiskLevel Risk { get; set; }
    public ProcessingDecision Decision { get; set; }
    public string DecisionReason { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public Guid? DuplicateOfTicketId { get; set; }
    public Guid? ClusterId { get; set; }
    public Guid? LinkedSolutionId { get; set; }
    public double MatchConfidence { get; set; }
    public double SimilarityScore { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ResolvedAt { get; init; }
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class TicketCluster
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Workspace { get; init; }
    public required string Category { get; init; }
    public required string Subcategory { get; init; }
    public required string Name { get; set; }
    public string RepresentativeText { get; set; } = "";
    public List<Guid> TicketIds { get; init; } = [];
    public double AverageQuality { get; set; }
    public double KnowledgeValue { get; set; }
    public RiskLevel Risk { get; set; }
    public string RiskReason { get; set; } = "";
    public bool Promoted { get; set; }
    public string PromotionReason { get; set; } = "Waiting for more evidence";
    public Guid? SolutionId { get; set; }
}

public sealed class KnowledgeSolution
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ClusterId { get; init; }
    public required string Workspace { get; init; }
    public required string Title { get; set; }
    public required string Problem { get; set; }
    public required string Procedure { get; set; }
    public string Applicability { get; set; } = "";
    public string Warning { get; set; } = "";
    public RiskLevel Risk { get; set; }
    public SolutionStatus Status { get; set; } = SolutionStatus.Draft;
    public int Version { get; set; } = 1;
    public int SourceTicketCount { get; set; }
    public List<Guid> SourceTicketIds { get; init; } = [];
    public int UsageCount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset ReviewDueAt { get; set; } = DateTimeOffset.UtcNow.AddMonths(6);
}

public sealed class ReviewTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid SolutionId { get; init; }
    public required ReviewRole RequiredRole { get; init; }
    public ReviewDecision Decision { get; set; } = ReviewDecision.Pending;
    public string DecisionReason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
}

public sealed class PipelineRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ImportMode Mode { get; init; }
    public string Connector { get; init; } = "Mock Jira";
    public string QueueName { get; init; } = "BACKFILL_QUEUE";
    public int Received { get; set; }
    public int Imported { get; set; }
    public int IdempotentSkipped { get; set; }
    public int Blocked { get; set; }
    public int SearchOnly { get; set; }
    public int Duplicates { get; set; }
    public int AutoLinked { get; set; }
    public int CandidateEligible { get; set; }
    public int DraftsCreated { get; set; }
    public int DomainReviewsCreated { get; set; }
    public int ManagerReviewsCreated { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAt { get; set; }
}

public sealed class SimilarityLink
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid SourceTicketId { get; init; }
    public required Guid TargetTicketId { get; init; }
    public required string Method { get; init; }
    public double Score { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AIRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ClusterId { get; init; }
    public List<Guid> TicketIds { get; init; } = [];
    public required string Model { get; init; }
    public required string PromptHash { get; init; }
    public required string Provider { get; init; }
    public int LatencyMs { get; init; }
    public decimal Cost { get; init; }
    public bool InputWasMasked { get; init; }
    public string Outcome { get; init; } = "completed";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AuditEntry
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public required string Action { get; init; }
    public required string Resource { get; init; }
    public required string Detail { get; init; }
}

public sealed class DemoState
{
    public List<TicketRecord> Tickets { get; init; } = [];
    public List<TicketCluster> Clusters { get; init; } = [];
    public List<KnowledgeSolution> Solutions { get; init; } = [];
    public List<ReviewTask> Reviews { get; init; } = [];
    public List<SimilarityLink> SimilarityLinks { get; init; } = [];
    public List<AIRun> AIRuns { get; init; } = [];
    public List<PipelineRun> Runs { get; init; } = [];
    public List<AuditEntry> Audit { get; init; } = [];
    public int DailySyncNumber { get; set; }
}

public sealed class DashboardViewModel
{
    public required DemoState State { get; init; }
    public required JiraConnectionSummary Jira { get; init; }
    public string? Notice { get; init; }
    public string? Error { get; init; }

    public int PendingDomainReviews => State.Reviews.Count(x => x.Decision == ReviewDecision.Pending && x.RequiredRole == ReviewRole.DomainReviewer);
    public int PendingManagerReviews => State.Reviews.Count(x => x.Decision == ReviewDecision.Pending && x.RequiredRole == ReviewRole.ManagerSme);
    public int PublishedSolutions => State.Solutions.Count(x => x.Status == SolutionStatus.Published);
    public double ReviewReduction => State.Tickets.Count == 0 ? 0 : 1d - (double)State.Reviews.Count / State.Tickets.Count;
}

public sealed record JiraConnectionSummary(bool IsConfigured, string Mode, string ProjectKey, string BaseUrl);
