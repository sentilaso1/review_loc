# T-Solve MVP Demo

T-Solve demonstrates how resolved Jira or Excel tickets can become a trusted knowledge source without sending every ticket to a reviewer.

![T-Solve dashboard after importing 500 tickets](dashboard.png)

## What the demo proves

- Imports 500 historical Jira tickets through a connector abstraction.
- Imports `.xlsx` files whose first worksheet has `summary`, `description`, and `comment` columns.
- Runs cleaning, PII/secret masking, quality gating and idempotency checks.
- Detects normalized duplicates and clusters similar outcomes.
- Creates one solution draft per promoted cluster, not per ticket.
- Routes ordinary solutions to a domain reviewer and high-risk solutions to a manager/SME.
- Syncs 10 incremental tickets and auto-links safe matches to published solutions.
- Only human-approved solutions become `PUBLISHED` knowledge.
- Shows review reduction, queue size, evidence count and pipeline decisions on one dashboard.

The full design rationale is in [T-Solve-MVP-Solution.md](T-Solve-MVP-Solution.md).

## Run locally

Requirements: .NET 10 SDK.

```powershell
dotnet restore TSolve.sln
dotnet run --project TSolve.Demo
```

Open the URL printed by ASP.NET Core. The application starts in deterministic `Mock Jira` mode.

Recommended demo sequence:

1. Select **Import 500 historical tickets**.
2. Explain the cleaning funnel and review-reduction metric.
3. Approve the solution drafts in the review queue.
4. Select **Sync 10 daily tickets**.
5. Show that safe tickets link to published knowledge while new/high-risk knowledge remains controlled.
6. Inspect `GET /api/demo/summary` to show the same metrics as JSON.

Use **Reset demo** to return to a clean in-memory state.

## Import from Excel

Use the dashboard upload form with an `.xlsx` file. The first worksheet must contain these headers (case-insensitive):

| summary | description | comment |
| --- | --- | --- |
| Ticket title | Problem details | Resolution or evidence used to solve it |

Blank rows are ignored and `summary` is required on every ticket row. Re-importing the same file is idempotent. Excel tickets pass through the same masking, quality, duplicate, clustering, promotion, and human-review pipeline as Jira tickets.

## Connect to Jira Cloud

The demo uses Jira Cloud REST API v3 enhanced JQL search. Keep credentials outside source control:

```powershell
$env:Jira__Mode = 'Real'
$env:Jira__BaseUrl = 'https://your-domain.atlassian.net'
$env:Jira__Email = 'user@example.com'
$env:Jira__ApiToken = 'your-api-token'
$env:Jira__ProjectKey = 'SUP'
$env:Jira__DoneJql = 'statusCategory = Done'
dotnet run --project TSolve.Demo
```

The connector requests resolved issues in pages and stores the Jira issue key, URL and raw snapshot in memory for traceability. Jira descriptions are flattened from Atlassian Document Format. Recent comments are included as resolution evidence because Jira's built-in `resolution` field is normally a resolution category, not a full procedure.

Never commit Jira API tokens. For a production integration, prefer OAuth 2.0 and a durable encrypted secret store.

## Important demo boundaries

- State is intentionally in memory so every presentation can be reset. Replace `DemoStore` with PostgreSQL/EF Core for production persistence.
- Similarity uses deterministic token-set similarity so the demo works offline. The service boundary can later be replaced by pgvector or an embedding model.
- Thresholds are evaluation-set defaults, not universal production values. Audit a sample of auto-link/search-only decisions before changing them.
- T-Solve does not modify Jira ticket lifecycle, assignment, SLA or status.
