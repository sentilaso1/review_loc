using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TSolve.Demo.Models;
using TSolve.Demo.Options;

namespace TSolve.Demo.Services.Jira;

public sealed class RealJiraClient(HttpClient httpClient, IOptions<JiraOptions> options) : IJiraClient
{
    private readonly JiraOptions _options = options.Value;
    public string Name => "Jira Cloud REST API";

    public async Task<IReadOnlyList<SourceTicket>> GetResolvedTicketsAsync(ImportMode mode, int limit, int dailySyncNumber, CancellationToken cancellationToken)
    {
        if (!_options.IsRealConfigured)
            throw new InvalidOperationException("Real Jira mode requires BaseUrl, Email, ApiToken and ProjectKey.");

        var results = new List<SourceTicket>();
        string? nextPageToken = null;
        var jql = $"project = \"{EscapeJql(_options.ProjectKey)}\" AND {_options.DoneJql}";
        if (mode == ImportMode.Daily)
            jql += " AND updated >= -7d";
        jql += " ORDER BY updated ASC";

        do
        {
            var remaining = limit - results.Count;
            var payload = new
            {
                jql,
                maxResults = Math.Min(100, remaining),
                nextPageToken,
                fields = new[] { "summary", "description", "status", "resolution", "comment", "labels", "issuetype", "components", "created", "updated", "resolutiondate" }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), "rest/api/3/search/jql"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Email}:{_options.ApiToken}")));
            request.Content = JsonContent.Create(payload);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Jira returned {(int)response.StatusCode}: {SafeMessage(body)}");

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("issues", out var issues)) break;

            foreach (var issue in issues.EnumerateArray())
            {
                var ticket = ParseIssue(issue);
                if (ticket is not null) results.Add(ticket);
                if (results.Count >= limit) break;
            }

            nextPageToken = root.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;
        } while (results.Count < limit && !string.IsNullOrWhiteSpace(nextPageToken));

        return results;
    }

    private SourceTicket? ParseIssue(JsonElement issue)
    {
        if (!issue.TryGetProperty("key", out var keyElement) || !issue.TryGetProperty("fields", out var fields)) return null;
        var key = keyElement.GetString();
        if (string.IsNullOrWhiteSpace(key)) return null;

        var comments = new List<string>();
        if (fields.TryGetProperty("comment", out var commentPage) && commentPage.ValueKind == JsonValueKind.Object &&
            commentPage.TryGetProperty("comments", out var commentItems))
        {
            foreach (var comment in commentItems.EnumerateArray().TakeLast(5))
                if (comment.TryGetProperty("body", out var commentBody)) comments.Add(FlattenAdf(commentBody));
        }

        var labels = ReadStringArray(fields, "labels");
        if (fields.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
            labels.AddRange(components.EnumerateArray().Select(x => ReadNestedName(x)).Where(x => x.Length > 0));

        var updated = ReadDate(fields, "updated") ?? DateTimeOffset.UtcNow;
        var resolved = ReadDate(fields, "resolutiondate") ?? updated;
        return new SourceTicket
        {
            Source = "Jira",
            ExternalId = key,
            SourceUrl = $"{_options.BaseUrl.TrimEnd('/')}/browse/{key}",
            Title = ReadString(fields, "summary"),
            Description = fields.TryGetProperty("description", out var description) ? FlattenAdf(description) : "",
            Resolution = ReadNestedName(fields, "resolution"),
            Status = ReadNestedName(fields, "status"),
            IssueType = ReadNestedName(fields, "issuetype"),
            Workspace = _options.ProjectKey,
            Labels = labels,
            Comments = comments,
            CreatedAt = ReadDate(fields, "created") ?? resolved,
            ResolvedAt = resolved,
            UpdatedAt = updated,
            RawJson = issue.GetRawText()
        };
    }

    private static string FlattenAdf(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) return element.GetString() ?? "";
        if (element.ValueKind == JsonValueKind.Object)
        {
            var pieces = new List<string>();
            if (element.TryGetProperty("text", out var text)) pieces.Add(text.GetString() ?? "");
            if (element.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                pieces.AddRange(content.EnumerateArray().Select(FlattenAdf));
            return string.Join(' ', pieces.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        if (element.ValueKind == JsonValueKind.Array)
            return string.Join(' ', element.EnumerateArray().Select(FlattenAdf));
        return "";
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string ReadNestedName(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? ReadNestedName(value) : "";

    private static string ReadNestedName(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";

    private static List<string> ReadStringArray(JsonElement element, string property) =>
        element.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : [];

    private static DateTimeOffset? ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;

    private static string EscapeJql(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string SafeMessage(string body) => body.Length <= 400 ? body : body[..400] + "…";
}
