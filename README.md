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

Requirements: .NET 9 SDK, PostgreSQL 17 client/server, and the `pgvector` extension.

PostgreSQL is the authoritative runtime store. Copy `.env.example` to your local environment setup and set `DATABASE_URL`; the application fails fast when the database is missing or unreachable and never silently falls back to memory.

```powershell
$env:DATABASE_URL = 'postgresql://postgres:your-password@localhost:5432/tsolve_dev'
dotnet restore TSolve.sln
dotnet run --project TSolve.Demo
```

On first startup the application creates `tsolve_dev` when necessary and applies the versioned SQL migration in `TSolve.Demo/Database/Migrations`. It uses the installed `psql` client (override with `PSQL_PATH`) so the data layer can run in restricted/offline environments without downloading a database driver. Writes are serialized, wrapped in a PostgreSQL transaction, and protected by an advisory lock. Every ticket stores a normalized 384-dimensional embedding in pgvector; nearest-neighbor search uses cosine distance through an HNSW index. Startup fails if pgvector is unavailable.

Use `GET /api/demo/tickets/{ticketId}/similar?limit=10` to retrieve nearest tickets from pgvector.

When PostgreSQL runs in Docker and no Windows `psql.exe` is installed, set `PSQL_CONTAINER` to the running container name (for example `postgres-vector`).

Open the URL printed by ASP.NET Core. The application starts in deterministic `Mock Jira` mode.

Recommended demo sequence:

1. Select **Import 500 historical tickets**.
2. Explain the cleaning funnel and review-reduction metric.
3. Approve the solution drafts in the review queue.
4. Select **Sync 10 daily tickets**.
5. Show that safe tickets link to published knowledge while new/high-risk knowledge remains controlled.
6. Inspect `GET /api/demo/summary` to show the same metrics as JSON.

Use **Reset demo** to clear the persistent T-Solve tables transactionally.

## Import from Excel

Use the dashboard upload form with an `.xlsx` file. The first worksheet must contain these headers (case-insensitive):

| summary | description | comment |
| --- | --- | --- |
| Ticket title | Problem details | Resolution or evidence used to solve it |

Blank rows are ignored and `summary` is required on every ticket row. Re-importing the same file is idempotent. Excel tickets pass through the same masking, quality, duplicate, clustering, promotion, and human-review pipeline as Jira tickets.

The reader also recognizes the existing four-sheet test-result workbook (`Ticket Results`, with `original comment`) so the 500-row baseline can be rerun directly. Generate the comparable four-sheet report with:

```powershell
dotnet run --project TSolve.Demo -- --backfill-report input.xlsx output.xlsx
```

Append `reverse` or `shuffle` to audit order independence. Bulk input is canonically sorted and clustered globally with complete-link, so all three orders produce the same member partitions.

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

- `DemoStore` remains only as a fast unit-test fixture. The web application uses `PostgresStateStore` exclusively.
- Local semantic features canonicalize Vietnamese/English domain concepts and remove support boilerplate. The schema already stores nullable pgvector embeddings so a self-hosted multilingual sentence-transformer can replace the deterministic offline encoder without another table redesign.
- Thresholds are evaluation-set defaults, not universal production values. Audit a sample of auto-link/search-only decisions before changing them.
- T-Solve does not modify Jira ticket lifecycle, assignment, SLA or status.



## Pipeline scoring and thresholds

Pipeline thresholds are configured under `Pipeline` in `appsettings.json`.

- Quality score (0-100): title length 10; description length 15; resolution length 25 plus 10 for a detailed resolution; numbered/action steps 15; root-cause wording 10; comments 5; labels 5; and a non-generic resolution 5. Tickets below `CandidateQualityThreshold` or with fewer than `WeakResolutionMinTokens` meaningful resolution tokens remain searchable but do not become knowledge candidates.
- Risk: configured high-risk keywords take precedence over medium-risk keywords. Cluster risk uses the maximum member risk and records `RiskReason`, so a cluster can never be lower risk than one of its tickets.
- Near duplicates: a configurable weighted score combines resolution semantic similarity (60%), title similarity (25%), and full-content similarity (15%). Each detected pair creates a `SimilarityLink` before clustering.
- Clustering: tickets must share workspace/category/subcategory. Backfill performs deterministic global complete-link merging: the least-similar cross-pair must meet `ClusterSimilarityThreshold`, so single-link chaining cannot occur. Daily tickets are checked against every existing cluster member.
- Published matching: only non-expired `PUBLISHED` solutions in the same workspace qualify, using `PublishedMatchThreshold`. AI/synthesis output remains `IN_REVIEW`; the pipeline never auto-publishes.

## Two-stage synthesis and data egress

Stage A selects the resolution nearest the cluster consensus, isolates resolution outliers as exceptions, shortens the evidence, and runs the same PII masker again. Stage B receives only that masked extract and rewrites it into `problem`, `procedure`, `applicability`, and `warning`. Conflicting evidence returns `insufficient evidence` and does not create a draft. Every Stage-B attempt records an `AIRun` with model/provider, prompt hash, referenced cluster/tickets, latency, cost, masking assertion, and outcome.

The default Stage-B provider is `Internal`, so no data leaves company infrastructure. If an external provider adapter is introduced, that adapter is the explicit data-egress boundary; `AllowExternalForSensitiveContent` defaults to `false`, and High-risk/HR/Finance clusters must remain on an internal model. External output may only create an `IN_REVIEW` draft.

Bulk imports use `BACKFILL_QUEUE`; incremental imports use `DAILY_QUEUE`. The demo stores both queue names for audit while retaining its in-memory execution model.
