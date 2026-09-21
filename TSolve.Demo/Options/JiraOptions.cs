namespace TSolve.Demo.Options;

public sealed class JiraOptions
{
    public const string Section = "Jira";
    public string Mode { get; set; } = "Mock";
    public string BaseUrl { get; set; } = "";
    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string ProjectKey { get; set; } = "TSOLVE";
    public string DoneJql { get; set; } = "statusCategory = Done";
    public int BulkLimit { get; set; } = 500;
    public int DailyLimit { get; set; } = 10;

    public bool IsRealConfigured =>
        Mode.Equals("Real", StringComparison.OrdinalIgnoreCase) &&
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(ApiToken) &&
        !string.IsNullOrWhiteSpace(ProjectKey);
}
