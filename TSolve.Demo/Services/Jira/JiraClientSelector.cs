using Microsoft.Extensions.Options;
using TSolve.Demo.Options;

namespace TSolve.Demo.Services.Jira;

public sealed class JiraClientSelector(MockJiraClient mock, RealJiraClient real, IOptions<JiraOptions> options)
{
    public IJiraClient Current => options.Value.IsRealConfigured ? real : mock;
}
