using TSolve.Demo.Models;
using TSolve.Demo.Services;

namespace TSolve.Demo.Tests;

public sealed class TextProcessingServiceTests
{
    private readonly TextProcessingService _service = new();

    [Fact]
    public void Clean_MasksSensitiveData_AndClassifiesAccessTicket()
    {
        var result = _service.Clean(Ticket(
            "Cannot login to production",
            "Contact alice@example.com from 10.20.30.40",
            "Root cause: missing role. 1. Verify approval. 2. Add access group. 3. Test login."));

        Assert.Contains("[EMAIL]", result.Description);
        Assert.Contains("[IP_ADDRESS]", result.Description);
        Assert.Equal("ACCESS", result.Category);
        Assert.Equal(RiskLevel.High, result.Risk);
        Assert.Equal(ProcessingDecision.CandidateEligible, result.Decision);
    }

    [Fact]
    public void Clean_RoutesGenericResolutionToSearchOnly()
    {
        var result = _service.Clean(Ticket("Printer issue", "The printer queue is stuck for the user.", "Done"));
        Assert.Equal(ProcessingDecision.SearchOnly, result.Decision);
    }

    private static SourceTicket Ticket(string title, string description, string resolution) => new()
    {
        Source = "Jira",
        ExternalId = "TEST-1",
        SourceUrl = "https://example.atlassian.net/browse/TEST-1",
        Title = title,
        Description = description,
        Resolution = resolution,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        ResolvedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
