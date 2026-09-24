using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TSolve.Demo.Models;
using TSolve.Demo.Options;

namespace TSolve.Demo.Services;

public sealed record ExtractedEvidence(
    string ProblemEvidence,
    string ProcedureEvidence,
    string ApplicabilityEvidence,
    IReadOnlyList<string> Exceptions,
    int SupportingTickets,
    int TotalTickets,
    bool InsufficientEvidence);

public sealed record RewriteResult(string Problem, string Procedure, string Applicability, string Warning, bool InsufficientEvidence);

public interface ISynthesisRewriter
{
    string Provider { get; }
    string Model { get; }
    RewriteResult Rewrite(ExtractedEvidence maskedEvidence);
}

// Default stage-B implementation is deliberately local. It can be replaced by an internal LLM adapter
// without changing stage A. No raw ticket is ever exposed through this interface.
public sealed class InternalSafeRewriter(IOptions<SynthesisOptions>? configuredOptions = null) : ISynthesisRewriter
{
    private readonly SynthesisOptions _options = configuredOptions?.Value ?? new SynthesisOptions();
    public string Provider => "Internal";
    public string Model => _options.Model;

    public RewriteResult Rewrite(ExtractedEvidence evidence)
    {
        if (evidence.InsufficientEvidence)
            return new("Insufficient evidence.", "Insufficient evidence to synthesize a reliable procedure.",
                evidence.ApplicabilityEvidence, "Conflicting or unsupported resolutions; retain this cluster in the candidate pool.", true);

        var warning = evidence.Exceptions.Count == 0
            ? "Verify applicability and current policy during human review."
            : $"Exceptions requiring reviewer attention: {string.Join(" | ", evidence.Exceptions.Take(3))}";
        return new(evidence.ProblemEvidence, evidence.ProcedureEvidence, evidence.ApplicabilityEvidence, warning, false);
    }
}

public sealed class TwoStageSynthesisService(
    TextProcessingService text,
    ISynthesisRewriter rewriter,
    IOptions<PipelineOptions>? configuredOptions = null)
{
    private readonly PipelineOptions _options = configuredOptions?.Value ?? new PipelineOptions();

    public (RewriteResult Result, AIRun AIRun) Synthesize(TicketCluster cluster, IReadOnlyList<TicketRecord> tickets)
    {
        var extracted = ExtractAndMask(cluster, tickets);
        var serialized = JsonSerializer.Serialize(extracted);
        if (ContainsRawPii(serialized)) throw new InvalidOperationException("Stage-B synthesis input contains unmasked PII.");

        var timer = Stopwatch.StartNew();
        var result = rewriter.Rewrite(extracted);
        timer.Stop();
        var run = new AIRun
        {
            ClusterId = cluster.Id,
            TicketIds = tickets.Select(ticket => ticket.Id).ToList(),
            Provider = rewriter.Provider,
            Model = rewriter.Model,
            PromptHash = text.Hash(serialized),
            LatencyMs = checked((int)timer.ElapsedMilliseconds),
            Cost = 0,
            InputWasMasked = true,
            Outcome = result.InsufficientEvidence ? "insufficient_evidence" : "completed"
        };
        return (result, run);
    }

    private ExtractedEvidence ExtractAndMask(TicketCluster cluster, IReadOnlyList<TicketRecord> tickets)
    {
        if (tickets.Count < 2)
            return new("Insufficient evidence.", "", Applicability(cluster, tickets.Count), [], 0, tickets.Count, true);

        var ranked = tickets.Select(ticket => new
        {
            Ticket = ticket,
            Similarities = tickets.Where(other => other.Id != ticket.Id)
                .Select(other => text.Similarity(ticket.CleanResolution, other.CleanResolution)).ToArray()
        }).Select(item => new
        {
            item.Ticket,
            Average = item.Similarities.Length == 0 ? 0 : item.Similarities.Average(),
            Support = item.Similarities.Count(score => score >= _options.SynthesisConsensusThreshold)
        }).OrderByDescending(item => item.Support).ThenByDescending(item => item.Average)
          .ThenBy(item => item.Ticket.ExternalId, StringComparer.Ordinal).ToList();

        var representative = ranked[0];
        var support = representative.Support + 1;
        var requiredSupport = Math.Max(2, (int)Math.Ceiling(tickets.Count * 0.5));
        var outliers = tickets.Where(ticket => ticket.Id != representative.Ticket.Id &&
                text.Similarity(representative.Ticket.CleanResolution, ticket.CleanResolution) < _options.SynthesisOutlierThreshold)
            .OrderBy(ticket => ticket.ExternalId, StringComparer.Ordinal)
            .Select(ticket => text.MaskSensitive(ticket.CleanResolution))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Take(5).ToList();

        var commonProblem = text.MaskSensitive(representative.Ticket.CleanTitle + ". " + representative.Ticket.CleanDescription);
        var commonProcedure = text.MaskSensitive(representative.Ticket.CleanResolution);
        var insufficient = support < requiredSupport || string.IsNullOrWhiteSpace(commonProcedure);
        return new(
            insufficient ? "Insufficient evidence." : $"Recurring issue: {commonProblem}",
            insufficient ? "" : $"Procedure supported by {support}/{tickets.Count} related tickets: {commonProcedure}",
            Applicability(cluster, tickets.Count),
            outliers,
            support,
            tickets.Count,
            insufficient);
    }

    private static string Applicability(TicketCluster cluster, int count) =>
        $"Workspace {cluster.Workspace}; category {cluster.Category}/{cluster.Subcategory}; supported by {count} source ticket(s).";

    private static bool ContainsRawPii(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value,
            @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b|https?://|(?<![\p{L}\p{N}])@[A-Z0-9._-]{2,}");
}
