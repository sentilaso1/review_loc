using TSolve.Demo.Models;

namespace TSolve.Demo.Services.Jira;

public interface IJiraClient
{
    string Name { get; }
    Task<IReadOnlyList<SourceTicket>> GetResolvedTicketsAsync(ImportMode mode, int limit, int dailySyncNumber, CancellationToken cancellationToken);
}
