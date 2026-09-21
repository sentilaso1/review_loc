using System.Text.Json;
using TSolve.Demo.Models;

namespace TSolve.Demo.Services.Jira;

public sealed class MockJiraClient : IJiraClient
{
    public string Name => "Mock Jira REST API";

    private static readonly Scenario[] Scenarios =
    [
        new("IT", "Cannot login to Finance production - 403", "User receives 403 after a role change.", "Root cause: missing Finance application role. 1. Verify approved role. 2. Add user to the authorized group. 3. Wait for directory sync. 4. Verify login.", ["access", "production"]),
        new("IT", "VPN connection fails after password change", "Remote user cannot establish the corporate VPN connection.", "Root cause: cached VPN credentials. 1. Remove saved credentials. 2. Reconnect with the new password. 3. Verify MFA and network access.", ["access", "vpn"]),
        new("FINANCE", "Invoice rejected because supplier tax code differs", "Invoice validation rejects a supplier record.", "Root cause: supplier tax code does not match master data. 1. Compare invoice and supplier master. 2. Correct the approved record. 3. Resubmit and verify status.", ["invoice", "finance"]),
        new("HR", "Maternity leave request missing policy evidence", "Employee requests maternity leave but required documents are incomplete.", "Root cause: incomplete policy evidence. 1. Verify current policy version. 2. Request required documents. 3. Record eligibility decision. 4. Confirm approved leave period.", ["hr", "policy"]),
        new("ASSET", "Laptop replacement request requires warranty check", "A device is slow and the employee requests replacement.", "Root cause: device lifecycle or warranty condition. 1. Check device age. 2. Check warranty. 3. Run hardware diagnostics. 4. Apply replacement decision rule.", ["asset", "laptop"]),
        new("IT", "Mailbox quota exceeded", "User cannot receive new email because mailbox is full.", "Root cause: mailbox quota reached. 1. Verify quota. 2. Archive large messages. 3. Apply approved quota change if required. 4. Test mail delivery.", ["email"]),
        new("IT", "Printer queue is stuck", "Documents remain in the local printer queue.", "1. Clear the print queue. 2. Restart the spooler. 3. Print a test page and verify the result.", ["printer"]),
        new("IT", "Standard software installation request", "User requests an approved desktop application.", "1. Verify the software is approved. 2. Check license availability. 3. Install the package. 4. Launch and verify the installed version.", ["software"]),
        new("IT", "Password reset request", "User forgot the account password.", "Reset the password and ask the user to try again.", ["access", "password-reset"]),
        new("FINANCE", "Payroll bank detail update", "Employee requests a payroll bank account change.", "Root cause: approved bank data change. 1. Verify employee identity. 2. Validate approval evidence. 3. Update payroll master data. 4. Run maker-checker verification.", ["payroll", "finance"])
    ];

    public Task<IReadOnlyList<SourceTicket>> GetResolvedTicketsAsync(ImportMode mode, int limit, int dailySyncNumber, CancellationToken cancellationToken)
    {
        var start = mode == ImportMode.Bulk ? 1 : 501 + (dailySyncNumber * limit);
        var now = DateTimeOffset.UtcNow;
        var tickets = Enumerable.Range(start, limit).Select(number =>
        {
            var scenario = Scenarios[(number * 7) % Scenarios.Length];
            var variant = number % 5;
            var title = scenario.Title + (variant == 0 ? "" : $" · case {variant}");
            var resolution = number % 11 == 0 ? "Done" : scenario.Resolution;
            if (mode == ImportMode.Bulk && resolution != "Done" && number % 8 != 0)
                resolution += $" Evidence reference: historical-{number}.";
            if (mode == ImportMode.Daily && resolution != "Done")
                resolution += $" Verification reference: daily-{dailySyncNumber + 1}-{number}.";
            var labels = scenario.Labels.ToList();
            if (number % 29 == 0) labels.Add("test");
            var description = scenario.Description;
            if (number % 17 == 0) description += $" Contact user{number}@example.com from 10.20.30.{number % 240 + 1}.";
            if (number % 41 == 0) resolution += " Token: abcdefghijklmnopqrstuvwxyz123456";
            var resolvedAt = now.AddDays(-(limit - (number - start)) % 180).AddMinutes(-number);
            var raw = new { key = $"TSOLVE-{number}", fields = new { summary = title, description, resolution, labels } };
            return new SourceTicket
            {
                Source = "Jira",
                ExternalId = $"TSOLVE-{number}",
                SourceUrl = $"https://demo.atlassian.net/browse/TSOLVE-{number}",
                Title = title,
                Description = description,
                Resolution = resolution,
                Status = number % 3 == 0 ? "Closed" : "Resolved",
                IssueType = number % 4 == 0 ? "Service Request" : "Incident",
                Workspace = scenario.Workspace,
                Labels = labels,
                Comments = number % 6 == 0 ? ["Resolution verified with requester."] : [],
                CreatedAt = resolvedAt.AddDays(-2),
                ResolvedAt = resolvedAt,
                UpdatedAt = resolvedAt,
                RawJson = JsonSerializer.Serialize(raw)
            };
        }).ToList();

        return Task.FromResult<IReadOnlyList<SourceTicket>>(tickets);
    }

    private sealed record Scenario(string Workspace, string Title, string Description, string Resolution, string[] Labels);
}
