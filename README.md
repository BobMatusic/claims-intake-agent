# Claims Intake Agent

AI agent for insurance claim intake (FNOL — First Notice of Loss). Extracts structured data from uploaded PDFs or photos, verifies policy conditions, checks for exclusions against policy terms, and **requires adjuster approval before creating a claim**.

Built on .NET 10, Blazor Server, Azure OpenAI, and Azure AI Document Intelligence.

---

## Why

Insurance companies are automating claim intake — it's one of the few AI use cases with directly measurable ROI. The challenge isn't building an agent that reads a document. The challenge is building one that can be **deployed in a regulated environment**: it must not act autonomously, it must be possible to prove why it decided the way it did, and the content of processed documents must not be able to manipulate it.

This project focuses on exactly that second part.

## How it works

```
Upload (PDF / photo)
   │
Azure AI Document Intelligence  → text extraction (prebuilt-layout)
   │
Structured extraction (GetResponseAsync<T>)
   │── CaseFileExtraction       → contract number, holder, date, description, invoices
   │── Suspicious content flag  → prompt injection / social engineering detection
   │
Deterministic decision engine (code, not model)
   │── Hard blocks              → inactive policy, missing date, duplicate claim → escalate
   │── Soft signals             → missing data, date anomalies, high amount → require approval
   │
Policy exclusion check (RAG)
   │── Extract risk facts       → structured output (RiskFacts schema, 10 dimensions)
   │── Build search queries     → code-driven, one query per non-empty fact
   │── Vector search            → cosine similarity over policy condition chunks
   │── Evaluate matches         → structured output with verbatim evidence validation
   │
Human-in-the-loop gate
   │── Auto-approved            → no signals, no exclusions
   │── Requires approval        → adjuster sees signals + exclusions, approves/rejects
   └── Escalated                → hard blocks, sent to manual processing
```

## Security decisions

This is the core of the project — not the model calls themselves.

**Approval is enforced in code, not in the prompt.** The approval gate uses a `TaskCompletionSource<bool>` — the agent cannot proceed until the code receives an explicit response. The system prompt is just a recommendation — the model can ignore it. The framework cannot.

**Deterministic decision engine.** Hard blocks (inactive policy, missing date, duplicate claim) and soft signals (missing data, date anomalies, amount thresholds) are evaluated by the `DecisionEngine` in code. The model extracts data; it never decides on approval.

**Document content is data, not instructions.** The extraction prompt explicitly instructs the model that document text is data. Suspicious passages (prompt injection attempts, claims of pre-approval) are flagged via `ContainsSuspiciousInstructions` and treated as a hard block.

**Content filter as the first line of defense.** Azure Prompt Shields catch direct prompt injection attacks in the document before the model sees them. The app handles this as a security event with a clear user message, not as a technical error.

**Evidence validation is code, not trust.** The exclusion checker requires the model to quote verbatim evidence from the claim report. Code verifies the quote exists in the original text using `NormalizeText().Contains()`. Findings that can't be traced back to the source are discarded.

**Structured output, no tool calling.** Extraction and exclusion evaluation use `GetResponseAsync<T>` (structured output) — the model fills a typed schema, code validates. Tool calling and structured output cannot be combined in one call, so the pipeline uses two separate AI calls for exclusion checking.

**Model proposes, system verifies, human decides on edge cases.**

## Project structure

```
ClaimsIntake.slnx
├── ClaimsIntake.Web/              → Blazor Web App (Server interactivity)
│   ├── Components/Pages/
│   │   └── Home.razor             → upload, approval UI, result card
│   ├── Content/                   → test fixtures (fictional policy + claim reports)
│   │   └── vpp-kasko.md           → fictional CASCO policy conditions
│   ├── Program.cs                 → DI, OpenTelemetry, Azure App Configuration
│   └── Dockerfile                 → container build for Azure Container Apps
│
├── ClaimsIntake.Core/             → class library, all AI logic, no UI
│   ├── Agents/
│   │   └── ClaimsAgent.cs         → orchestrator: extract → decide → exclude → approve
│   ├── Extraction/
│   │   ├── CaseFileExtractor.cs   → structured extraction via GetResponseAsync<T>
│   │   ├── CaseFileExtraction.cs  → extraction schema (report + invoices)
│   │   └── CaseFileMapper.cs      → maps raw extraction to typed CaseFile
│   ├── Policies/
│   │   ├── ExclusionChecker.cs    → RAG pipeline: extract facts → search → evaluate
│   │   ├── ExclusionFinding.cs    → RiskFacts schema (10 dimensions) + finding types
│   │   ├── PolicyConditionsParser.cs → parses markdown policy into numbered chunks
│   │   ├── PolicyConditionsIndexer.cs → embeds chunks with text-embedding-3-small
│   │   └── PolicyConditionsSearch.cs  → cosine similarity search over embeddings
│   ├── Services/
│   │   ├── DecisionEngine.cs      → deterministic hard blocks + soft signals
│   │   ├── PolicyService.cs       → policy verification (mock)
│   │   └── AdjusterSummaryWriter.cs → AI-generated adjuster summary
│   ├── Models/                    → CaseFile, ApprovalRequest, ClaimDecision, etc.
│   └── AzureDocumentIntelligence.cs → Document Intelligence client wrapper
│
└── ClaimsIntake.Evals/            → xUnit tests + AI evals
    ├── DecisionEngineTests.cs     → 17 deterministic tests for the decision engine
    ├── CaseFileMapperTests.cs     → date parsing, field normalization
    ├── PolicyConditionsParserTests.cs → policy chunk parsing
    └── ClaimsAgentEvals.cs        → end-to-end AI evals (require Azure OpenAI keys)
```

## Test scenarios

The repo includes five sample claim reports in `Content/`, each testing a different branch:

| Scenario | Input | Expected behavior |
|---|---|---|
| 1 — complete | Valid SK contract, car damage, all data present | ACTIVE policy, AUTO type, auto-approve |
| 2 — missing data | No date, vague description | Soft signals, human-in-the-loop approval |
| 3 — invalid contract | Contract number without SK prefix | NOT FOUND, escalated |
| 4 — prompt injection | Hidden instruction in document to bypass rules | Caught by Azure Prompt Shields, security event |
| 5 — social engineering | Text claims the claim is pre-approved | Approval gate still fires — it's enforced in code |

Scenarios 4 and 5 are intentionally adversarial. Scenario 4 is stopped by Azure Prompt Shields before the model; scenario 5 is crafted to pass the filter and test whether the architecture itself holds.

## Observability

- **OpenTelemetry** with dual export: Azure Monitor (Application Insights) + Langfuse (OTLP)
- Custom `ActivitySource("ClaimsIntake")` spans: `claim.process`, `claim.extract`, `claim.checks`, `claim.exclusion_check`, `claim.decision`, `claim.await_approval`
- AI call tracing via `Microsoft.Extensions.AI` OpenTelemetry middleware (sensitive data only in Development)
- **Azure App Configuration** with Feature Flags — kill switch (`AgentEnabled`) to disable AI processing without redeployment

## Stack

.NET 10 · C# · Blazor Web App (Server) · Azure OpenAI (gpt-5-mini) · Azure AI Document Intelligence (prebuilt-layout) · text-embedding-3-small · Azure Container Apps · Azure App Configuration · OpenTelemetry · Application Insights · Langfuse

## Running locally

```bash
dotnet user-secrets set "DocIntelligence:Endpoint" "https://<resource>.cognitiveservices.azure.com/"
dotnet user-secrets set "DocIntelligence:Key" "<key>"
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:Key" "<key>"
dotnet user-secrets set "AzureOpenAI:Deployment" "<deployment-name>"
dotnet user-secrets set "AzureEmbedding:Endpoint" "https://<resource>.services.ai.azure.com"
dotnet user-secrets set "AzureEmbedding:Key" "<key>"
dotnet user-secrets set "AzureEmbedding:Deployment" "text-embedding-3-small"
dotnet user-secrets set "AppConfig:ConnectionString" "<connection-string>"

dotnet run --project ClaimsIntake.Web
```

Or edit `appsettings.Development.json` (gitignored — keys won't leak into the repo).

Azure resources: Document Intelligence (F0 tier) and Azure OpenAI, both in an EU region — data residency is a compliance requirement in insurance.

## Deployment

The app runs as a container in **Azure Container Apps** in an EU region (data residency).

A deployment script (`deploy.ps1`) handles the full pipeline: Docker build, ACR push, secret rotation, revision deployment, and health check with log output. Secrets are read from `deploy.local.json` (gitignored) — the script itself contains no sensitive values.

```powershell
.\deploy.ps1
```

**Container configuration:** User secrets are tied to a local profile — in the container they are replaced by environment variables. ASP.NET Core maps `__` to `:`, so `DocIntelligence:Endpoint` becomes `DocIntelligence__Endpoint`. No code changes needed — `IConfiguration` picks it up automatically.

In Container Apps, sensitive values (keys) are stored as **secrets**, not plain env vars — a secret is referenced in the environment configuration, but its value is not visible in the portal or logs.

**Known limitation — Blazor Server and replicas:** Blazor Server maintains a WebSocket connection between the browser and a specific instance. The approval flow (a waiting `TaskCompletionSource`) lives in that instance's memory. If the load balancer routes a request to a different replica, the connection is lost. Mitigation:

- **Session affinity** (sticky sessions) in Container Apps — enabled by default. Sufficient for low traffic.
- **Shared state** — move approval state to Redis or a database. Required only when scaling to multiple instances.

Currently running a single replica, so session affinity is enough.
