using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TSolve.Demo.Models;

namespace TSolve.Demo.Services;

public sealed partial class TextProcessingService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "to", "for", "of", "in", "on", "is", "was", "with",
        "user", "ticket", "issue", "please", "again", "đã", "bị", "và", "cho", "của", "là", "có", "không"
    };

    public CleanTicketResult Clean(SourceTicket source)
    {
        var title = NormalizeAndMask(source.Title);
        var description = NormalizeAndMask(source.Description);
        var resolution = NormalizeAndMask(string.Join(" ", new[] { source.Resolution }.Concat(source.Comments.TakeLast(3))));
        var (category, subcategory) = Classify($"{title} {description} {resolution}");
        var risk = AssessRisk($"{title} {description} {resolution}");
        var score = ScoreQuality(source, title, description, resolution);

        var isTest = source.Labels.Any(x => x.Equals("test", StringComparison.OrdinalIgnoreCase)) ||
                     source.Title.Contains("test ticket", StringComparison.OrdinalIgnoreCase);
        var weakResolution = WeakResolutionRegex().IsMatch(resolution) || Tokenize(resolution).Count < 4;

        var decision = ProcessingDecision.CandidateEligible;
        var reason = "Resolution and evidence are sufficient for knowledge analysis";
        if (SecretLeakRegex().IsMatch(resolution))
        {
            decision = ProcessingDecision.Blocked;
            reason = "Potential secret remains after masking";
        }
        else if (isTest)
        {
            decision = ProcessingDecision.SearchOnly;
            reason = "Test or synthetic operational ticket";
        }
        else if (weakResolution || score < 45)
        {
            decision = ProcessingDecision.SearchOnly;
            reason = weakResolution ? "Resolution is too short or generic" : "Quality score is below 45";
        }

        return new CleanTicketResult(title, description, resolution, category, subcategory, risk, score, decision, reason);
    }

    public double Similarity(string left, string right)
    {
        var a = Tokenize(left);
        var b = Tokenize(right);
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    public string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public string BuildClusterName(TicketRecord ticket) => $"{ToDisplay(ticket.Category)} · {ToDisplay(ticket.Subcategory)}";

    private static string NormalizeAndMask(string? value)
    {
        var text = WebUtility.HtmlDecode(value ?? "");
        text = HtmlRegex().Replace(text, " ");
        text = JiraMarkupRegex().Replace(text, " ");
        text = EmailRegex().Replace(text, "[EMAIL]");
        text = TokenRegex().Replace(text, "$1[SECRET]");
        text = IpRegex().Replace(text, "[IP_ADDRESS]");
        text = PhoneRegex().Replace(text, "[PHONE]");
        text = EmployeeIdRegex().Replace(text, "[EMPLOYEE_ID]");
        text = SignatureRegex().Replace(text, " ");
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    private static int ScoreQuality(SourceTicket source, string title, string description, string resolution)
    {
        var score = 0;
        if (title.Length >= 12) score += 10;
        if (description.Length >= 30) score += 15;
        if (resolution.Length >= 30) score += 25;
        if (resolution.Length >= 90) score += 10;
        if (StepRegex().IsMatch(resolution)) score += 15;
        if (RootCauseRegex().IsMatch(resolution)) score += 10;
        if (source.Comments.Count > 0) score += 5;
        if (source.Labels.Count > 0) score += 5;
        if (!WeakResolutionRegex().IsMatch(resolution)) score += 5;
        return Math.Clamp(score, 0, 100);
    }

    private static (string Category, string Subcategory) Classify(string text)
    {
        var value = text.ToLowerInvariant();
        if (ContainsAny(value, "login", "403", "access", "permission", "role", "quyền")) return ("ACCESS", value.Contains("vpn") ? "VPN" : "AUTHORIZATION");
        if (ContainsAny(value, "invoice", "payment", "payroll", "finance", "hóa đơn")) return ("FINANCE", value.Contains("payroll") ? "PAYROLL" : "INVOICE");
        if (ContainsAny(value, "maternity", "leave policy", "human resource", "nhân sự")) return ("HR", "POLICY");
        if (ContainsAny(value, "laptop", "device", "warranty", "asset")) return ("ASSET", "DEVICE");
        if (ContainsAny(value, "email", "mailbox", "outlook")) return ("COLLABORATION", "EMAIL");
        if (ContainsAny(value, "printer", "printing")) return ("WORKPLACE", "PRINTER");
        if (ContainsAny(value, "software", "install", "license")) return ("SOFTWARE", "INSTALLATION");
        return ("GENERAL", "OTHER");
    }

    private static RiskLevel AssessRisk(string text)
    {
        var value = text.ToLowerInvariant();
        if (ContainsAny(value, "production", "prod", "payroll", "finance", "maternity", "policy", "admin", "security")) return RiskLevel.High;
        if (ContainsAny(value, "access", "permission", "laptop", "device", "invoice")) return RiskLevel.Medium;
        return RiskLevel.Low;
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);

    private static HashSet<string> Tokenize(string value) =>
        WordRegex().Matches(value.ToLowerInvariant())
            .Select(x => x.Value)
            .Where(x => x.Length > 2 && !StopWords.Contains(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string ToDisplay(string value) => string.Join(' ', value.Split('_').Select(x => char.ToUpperInvariant(x[0]) + x[1..].ToLowerInvariant()));

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlRegex();
    [GeneratedRegex(@"[{}\[\]|*_~^]+")]
    private static partial Regex JiraMarkupRegex();
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
    [GeneratedRegex(@"(?i)(bearer\s+|api[_ -]?key\s*[:=]\s*|token\s*[:=]\s*)[A-Za-z0-9._-]{8,}")]
    private static partial Regex TokenRegex();
    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b")]
    private static partial Regex IpRegex();
    [GeneratedRegex(@"(?<!\d)(?:\+?84|0)\d{9,10}(?!\d)")]
    private static partial Regex PhoneRegex();
    [GeneratedRegex(@"\b(?:EMP|NV)-?\d{4,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmployeeIdRegex();
    [GeneratedRegex(@"(?is)(sent from (?:my|outlook).*)$")]
    private static partial Regex SignatureRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"^(done|fixed|completed|resolved|closed|ok|issue fixed|please try again)[.! ]*$", RegexOptions.IgnoreCase)]
    private static partial Regex WeakResolutionRegex();
    [GeneratedRegex(@"(?i)(step\s*\d|\d+[.)]|first|then|finally|verify|restart|check|add|update|remove)")]
    private static partial Regex StepRegex();
    [GeneratedRegex(@"(?i)(root cause|caused by|because|nguyên nhân)")]
    private static partial Regex RootCauseRegex();
    [GeneratedRegex(@"(?i)(bearer\s+[A-Za-z0-9._-]{12,}|(?:api[_ -]?key|token)\s*[:=]\s*[A-Za-z0-9._-]{12,})")]
    private static partial Regex SecretLeakRegex();
    [GeneratedRegex(@"[\p{L}\p{N}_-]+")]
    private static partial Regex WordRegex();
}

public sealed record CleanTicketResult(
    string Title,
    string Description,
    string Resolution,
    string Category,
    string Subcategory,
    RiskLevel Risk,
    int QualityScore,
    ProcessingDecision Decision,
    string Reason);
