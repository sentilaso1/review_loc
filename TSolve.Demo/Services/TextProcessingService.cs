using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TSolve.Demo.Models;
using TSolve.Demo.Options;

namespace TSolve.Demo.Services;

public sealed partial class TextProcessingService(IOptions<PipelineOptions>? configuredOptions = null)
{
    private readonly PipelineOptions _options = configuredOptions?.Value ?? new PipelineOptions();

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "to", "for", "of", "in", "on", "is", "was", "with",
        "user", "ticket", "issue", "please", "again", "đã", "bị", "và", "cho", "của", "là", "có", "không",
        "comment", "team", "ams", "xin", "cảm", "ơn", "hi", "dear", "anh", "chị", "em", "mình", "hỗ", "trợ", "request", "resolved"
    };

    public CleanTicketResult Clean(SourceTicket source)
    {
        var title = NormalizeAndMask(source.Title);
        var description = NormalizeAndMask(source.Description);
        var resolution = NormalizeAndMask(string.Join(" ", new[] { source.Resolution }.Concat(source.Comments.TakeLast(3))));
        var (category, subcategory) = Classify(source.Application, $"{title} {description}", resolution);
        var risk = AssessRisk($"{title} {description} {resolution}");
        var score = ScoreQuality(source, title, description, resolution);

        var isTest = source.Labels.Any(x => x.Equals("test", StringComparison.OrdinalIgnoreCase)) ||
                     source.Title.Contains("test ticket", StringComparison.OrdinalIgnoreCase);
        var weakResolution = WeakResolutionRegex().IsMatch(resolution) || Tokenize(resolution).Count < _options.WeakResolutionMinTokens;

        var decision = ProcessingDecision.CandidateEligible;
        var reason = $"Quality {score} meets configured threshold {_options.CandidateQualityThreshold}";
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
        else if (weakResolution || score < _options.CandidateQualityThreshold)
        {
            decision = ProcessingDecision.SearchOnly;
            reason = weakResolution
                ? $"Resolution has fewer than {_options.WeakResolutionMinTokens} meaningful tokens or is generic"
                : $"Quality {score} is below configured threshold {_options.CandidateQualityThreshold}";
        }

        return new CleanTicketResult(title, description, resolution, category, subcategory, risk, score, decision, reason);
    }

    // Deterministic multilingual semantic feature embedding. Domain synonyms are canonicalized before
    // word/bigram/subword projection, while support boilerplate is excluded from the signal.
    public double Similarity(string left, string right)
    {
        return Cosine(BuildEmbedding(left), BuildEmbedding(right));
    }

    public double TitleSimilarity(string left, string right) => Cosine(BuildEmbedding(left, includeCharacterTrigrams: true), BuildEmbedding(right, includeCharacterTrigrams: true));

    public double[] Embedding(string value)
    {
        const int dimensions = 384;
        var dense = new double[dimensions];
        foreach (var (feature, weight) in BuildEmbedding(value))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(feature));
            var index = (hash[0] << 8 | hash[1]) % dimensions;
            dense[index] += (hash[2] & 1) == 0 ? weight : -weight;
        }

        var norm = Math.Sqrt(dense.Sum(component => component * component));
        if (norm > 0)
            for (var index = 0; index < dense.Length; index++) dense[index] /= norm;
        return dense;
    }

    public string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public string MaskSensitive(string? value) => NormalizeAndMask(value);

    public string BuildClusterName(TicketRecord ticket) => $"{ToDisplay(ticket.Category)} · {ToDisplay(ticket.Subcategory)}";

    public SynthesisResult Synthesize(IReadOnlyList<TicketRecord> tickets)
    {
        if (tickets.Count < 2)
            return new("Insufficient evidence: fewer than two related tickets.", "Insufficient evidence to synthesize a reliable procedure.", "Add corroborating resolved tickets before review.");

        var commonTerms = tickets
            .SelectMany(ticket => Tokenize($"{ticket.CleanTitle} {ticket.CleanDescription}"))
            .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Term = group.Key, Count = group.Count() })
            .Where(item => item.Count >= 2)
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Term, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(item => item.Term)
            .ToArray();

        var best = tickets
            .Select(ticket => new
            {
                Ticket = ticket,
                Support = tickets.Count(other => Similarity(ticket.CleanResolution, other.CleanResolution) >= 0.30)
            })
            .OrderByDescending(item => item.Support)
            .ThenByDescending(item => item.Ticket.CleanResolution.Length)
            .First();

        var commonSteps = best.Support < 2
            ? []
            : SentenceRegex().Split(best.Ticket.CleanResolution)
                .Select(CleanSentence)
                .Where(sentence => Tokenize(sentence).Count >= 2)
                .Where(sentence => !IsBoilerplateSentence(sentence))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

        var problem = commonTerms.Length == 0
            ? $"Recurring {ToDisplay(tickets[0].Category)} issue supported by {tickets.Count} related tickets."
            : $"Recurring {ToDisplay(tickets[0].Category)} issue involving {string.Join(", ", commonTerms)}; supported by {tickets.Count} related tickets.";
        var procedure = commonSteps.Count == 0
            ? "Insufficient evidence to synthesize a reliable procedure."
            : $"Common procedure supported by {best.Support}/{tickets.Count} tickets: {string.Join(" ", commonSteps.Select((step, index) => $"{index + 1}. {step}."))}";
        var warning = commonSteps.Count == 0
            ? "The resolutions do not contain a repeated procedure; reviewer must add evidence or reject the draft."
            : best.Support < tickets.Count
                ? "Resolution variants exist outside the common steps; verify application-specific exceptions during review."
                : "Verify applicability and current system policy during review.";

        return new(problem, procedure, warning);
    }

    private string NormalizeAndMask(string? value)
    {
        var text = WebUtility.HtmlDecode(value ?? "");
        text = MentionElementRegex().Replace(text, " [PERSON] ");
        text = HtmlRegex().Replace(text, " ");
        text = CommentLabelRegex().Replace(text, " ");
        text = JiraMarkupRegex().Replace(text, " ");
        text = EmailRegex().Replace(text, "[EMAIL]");
        text = UrlRegex().Replace(text, "[LINK]");
        text = InternalHostRegex().Replace(text, "[INTERNAL_LINK]");
        text = MentionRegex().Replace(text, "[PERSON]");
        text = HandleRegex().Replace(text, "[PERSON]");
        text = AddressedPersonRegex().Replace(text, "$1 [PERSON]");
        text = TokenRegex().Replace(text, "$1[SECRET]");
        text = IpRegex().Replace(text, "[IP_ADDRESS]");
        text = PhoneRegex().Replace(text, "[PHONE]");
        text = EmployeeIdRegex().Replace(text, "[EMPLOYEE_ID]");
        text = SignatureRegex().Replace(text, " ");
        return WhitespaceRegex().Replace(text, " ").Trim(' ', '-');
    }

    private int ScoreQuality(SourceTicket source, string title, string description, string resolution)
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

    private static (string Category, string Subcategory) Classify(string application, string primaryText, string resolution)
    {
        var app = application.ToLowerInvariant();
        var primary = primaryText.ToLowerInvariant();
        var value = $"{primaryText} {resolution}".ToLowerInvariant();
        var subcategory = ClassifySubcategory(primary);
        if (subcategory == "OTHER") subcategory = ClassifySubcategory(resolution.ToLowerInvariant());

        if (ContainsAny(app, "hrms", "tms", "timesheet")) return ("HRMS", subcategory == "OTHER" ? "HR_OPERATIONS" : subcategory);
        if (app.Contains("dashboard")) return ("DASHBOARD", subcategory);
        if (app.Contains("crm")) return ("CRM", subcategory);
        if (app.Contains("poa")) return ("POA", subcategory);
        if (app.Contains("gams")) return ("GAMS", subcategory);
        if (app.Contains("c-ticket")) return ("C_TICKET", subcategory);
        if (app.Contains("c-hub")) return ("C_HUB", subcategory);
        if (app.Contains("oms")) return ("OMS", subcategory);
        if (ContainsAny(app, "mms", "resource planning")) return ("RESOURCE", subcategory);
        if (ContainsAny(app, "sale", "sales")) return ("SALES", subcategory);
        if (ContainsAny(app, "ams", "business application")) return ("BUSINESS_APP", subcategory);
        if (ContainsAny(app, "finance", "billing")) return ("FINANCE", subcategory);
        if (ContainsAny(app, "identity", "access")) return ("ACCESS", subcategory == "OTHER" ? "AUTHORIZATION" : subcategory);
        if (ContainsAny(app, "reporting", "analytics")) return ("REPORTING", subcategory);
        if (app.Contains("collaboration")) return ("COLLABORATION", subcategory);
        if (app.Contains("document")) return ("DOCUMENT", subcategory);
        if (app.Contains("procurement")) return ("PROCUREMENT", subcategory);
        if (app.Contains("asset")) return ("ASSET", subcategory);
        if (app.Contains("api")) return ("INTEGRATION", "API");
        if (ContainsAny(app, "jira", "wiki")) return ("JIRA_WIKI", subcategory);
        if (app.Equals("ec", StringComparison.OrdinalIgnoreCase)) return ("EC", subcategory);
        if (subcategory is "ACCESS" or "BADGE") return ("ACCESS", subcategory == "BADGE" ? "PHYSICAL_BADGE" : "AUTHORIZATION");
        if (ContainsAny(value, "dashboard", "biểu đồ", "chart", "báo cáo")) return ("DASHBOARD", subcategory);
        if (ContainsAny(value, "crm", "pipeline", "opportunity", "khách hàng")) return ("CRM", subcategory == "OTHER" ? "PIPELINE" : subcategory);
        if (ContainsAny(value, "hrms", "timesheet", "chấm công", "ngày công")) return ("HRMS", subcategory == "OTHER" ? "HR_OPERATIONS" : subcategory);
        if (ContainsAny(value, "poa", "approval", "phê duyệt", "luồng duyệt")) return ("POA", subcategory == "OTHER" ? "WORKFLOW" : subcategory);
        if (ContainsAny(value, "invoice", "payment", "payroll", "finance", "hóa đơn")) return ("FINANCE", subcategory);
        if (ContainsAny(value, "maternity", "leave policy", "human resource", "nhân sự")) return ("HR", subcategory);
        if (ContainsAny(value, "laptop", "device", "warranty", "asset")) return ("ASSET", subcategory);
        if (ContainsAny(value, "email", "mailbox", "outlook")) return ("COLLABORATION", subcategory);
        if (ContainsAny(value, "printer", "printing")) return ("WORKPLACE", "PRINTER");
        if (ContainsAny(value, "software", "install", "license")) return ("SOFTWARE", "INSTALLATION");
        return ("GENERAL", "OTHER");
    }

    private static string ClassifySubcategory(string value) =>
        ContainsAny(value, "dây thẻ", "thẻ nhân viên", "access card", "badge", "thẻ ra vào") ? "BADGE"
        : ContainsAny(value, "chấm công", "checkout", "check out", "check-in", "checkin", "timesheet", "ngày công", "giờ công", "giờ out", "giờ vào", "tính công") ? "TIMESHEET"
        : ContainsAny(value, "ca làm việc", "work shift", "working shift") ? "WORK_SHIFT"
        : ContainsAny(value, "explanation request", "giải trình") ? "EXPLANATION"
        : ContainsAny(value, "nghỉ phép", "annual leave", "leave balance") ? "LEAVE"
        : ContainsAny(value, "ldap", "unlock account", "mở khóa tài khoản") ? "ACCESS"
        : ContainsAny(value, "invoice", "payment", "hóa đơn", " inv ") ? "INVOICE"
        : ContainsAny(value, "login", "403", "access", "permission", "role", "quyền", "phân quyền") ? "ACCESS"
        : ContainsAny(value, "pipeline", "workflow", "approve", "approver", "submit", "luồng duyệt") ? "WORKFLOW"
        : ContainsAny(value, "notification", "noti", "mail", "email") ? "NOTIFICATION"
        : "OTHER";


    private RiskLevel AssessRisk(string text)
    {
        var value = text.ToLowerInvariant();
        if (_options.HighRiskKeywords.Any(value.Contains)) return RiskLevel.High;
        if (_options.MediumRiskKeywords.Any(value.Contains)) return RiskLevel.Medium;
        return RiskLevel.Low;
    }

    private static Dictionary<string, double> BuildEmbedding(string value, bool includeCharacterTrigrams = true)
    {
        var tokens = WordRegex().Matches(Canonicalize(value)).Select(match => match.Value).Where(token => token.Length > 2 && !StopWords.Contains(token)).ToArray();
        var vector = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var token in tokens) Add(vector, $"w:{token}", 2);
        for (var index = 0; index + 1 < tokens.Length; index++) Add(vector, $"b:{tokens[index]}_{tokens[index + 1]}", 3);
        if (includeCharacterTrigrams)
        {
            var normalized = string.Join(' ', tokens);
            for (var index = 0; index + 2 < normalized.Length; index++) Add(vector, $"c:{normalized.Substring(index, 3)}", 0.35);
        }
        return vector;
    }

    private static double Cosine(IReadOnlyDictionary<string, double> left, IReadOnlyDictionary<string, double> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0;
        var dot = left.Sum(item => item.Value * (right.TryGetValue(item.Key, out var value) ? value : 0));
        var leftNorm = Math.Sqrt(left.Sum(item => item.Value * item.Value));
        var rightNorm = Math.Sqrt(right.Sum(item => item.Value * item.Value));
        return leftNorm == 0 || rightNorm == 0 ? 0 : dot / (leftNorm * rightNorm);
    }

    private static void Add(IDictionary<string, double> vector, string feature, double weight) =>
        vector[feature] = vector.TryGetValue(feature, out var current) ? current + weight : weight;

    private static HashSet<string> Tokenize(string value) =>
        WordRegex().Matches(Canonicalize(value))
            .Select(match => match.Value)
            .Where(token => token.Length > 2 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Canonicalize(string value)
    {
        var normalized = value.ToLowerInvariant();
        foreach (var (pattern, replacement) in SemanticPhrases)
            normalized = Regex.Replace(normalized, pattern, replacement, RegexOptions.CultureInvariant);
        return normalized;
    }

    private static readonly (string Pattern, string Replacement)[] SemanticPhrases =
    [
        (@"\b(check[ -]?in|check[ -]?out|ngày công|giờ công|chấm công|timesheet)\b", " attendance "),
        (@"\b(dây thẻ|thẻ nhân viên|access card|badge|thẻ ra vào)\b", " physical_badge "),
        (@"\b(log[ -]?in|đăng nhập|sign[ -]?in)\b", " authentication "),
        (@"\b(phân quyền|cấp quyền|permission|authorization|role)\b", " authorization "),
        (@"\b(pineline|pipeline|sales funnel)\b", " sales_pipeline "),
        (@"\b(hóa đơn|invoice|billing)\b", " invoice "),
        (@"\b(phê duyệt|approve|approval|approver)\b", " approval_workflow "),
        (@"\b(end date|ngày kết thúc|deadline)\b", " end_date ")
    ];

    private static string CleanSentence(string value) => WhitespaceRegex().Replace(value, " ").Trim(' ', '-', '.', ':');
    private static bool IsBoilerplateSentence(string sentence)
    {
        var value = sentence.ToLowerInvariant();
        return value.StartsWith("hi ") || value.StartsWith("dear ") || value.StartsWith("pending ") ||
               ContainsAny(value, "cảm ơn", "xin tiếp nhận", "liên hệ với team ams", "team đã tiếp nhận", "ams đã hỗ trợ", "xin resolve", "xin phép đóng", "thank");
    }
    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);
    private static string ToDisplay(string value) => string.Join(' ', value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlRegex();
    [GeneratedRegex("(?is)<span[^>]*class\\s*=\\s*['\"]mention['\"][^>]*>.*?</span>")]
    private static partial Regex MentionElementRegex();
    [GeneratedRegex(@"\[\s*Comment\s+\d+\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex CommentLabelRegex();
    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
    [GeneratedRegex("(?i)\\b(?:pms|c-ticket-api|jira|wiki)\\.cmcglobal\\.com\\.vn(?:/[^\\s<>\"']*)?")]
    private static partial Regex InternalHostRegex();
    [GeneratedRegex(@"[{}\[\]|*_~^]+")]
    private static partial Regex JiraMarkupRegex();
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
    [GeneratedRegex(@"@[\p{L}\p{N} ._'’-]{2,80}\s+-\s+CMC(?:\s+[\p{L}\p{N}._-]+){0,4}", RegexOptions.IgnoreCase)]
    private static partial Regex MentionRegex();
    [GeneratedRegex(@"(?<![\p{L}\p{N}])@[A-Z0-9._-]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex HandleRegex();
    [GeneratedRegex(@"(?i)\b(bạn|anh|chị|mr\.?|ms\.?)\s+(?:[A-ZĐ][\p{L}0-9._-]+(?:\s+|-)){1,5}[A-ZĐ][\p{L}0-9._-]+")]
    private static partial Regex AddressedPersonRegex();
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
    [GeneratedRegex(@"(?i)(step\s*\d|\d+[.)]|first|then|finally|verify|restart|check|add|update|remove|truy cập|chạy|kiểm tra)")]
    private static partial Regex StepRegex();
    [GeneratedRegex(@"(?i)(root cause|caused by|because|nguyên nhân)")]
    private static partial Regex RootCauseRegex();
    [GeneratedRegex(@"(?i)(bearer\s+[A-Za-z0-9._-]{12,}|(?:api[_ -]?key|token)\s*[:=]\s*[A-Za-z0-9._-]{12,})")]
    private static partial Regex SecretLeakRegex();
    [GeneratedRegex(@"[\p{L}\p{N}_-]+")]
    private static partial Regex WordRegex();
    [GeneratedRegex(@"(?<=[.!?])\s+|\s+-{3,}\s+|(?=\d+[.)]\s*)")]
    private static partial Regex SentenceRegex();
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

public sealed record SynthesisResult(string Problem, string Procedure, string Warning);
