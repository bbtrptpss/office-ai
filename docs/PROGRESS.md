# Progress

Resume here. Each phase is built on its own branch and committed per milestone.

**Update log rule:** every change adds an entry (and updates the remaining-work list) in `frontend/src/lib/changelog.ts`, in the same commit. It is shown in the app at `/updates` (เมนู “บันทึกอัปเดต”).

## Phase 1 — Company foundation ✅ (master, 1fbcb9f)

## Phase 2 — AI employee execution ✅ (merged to master)

Goal: an AI employee can be configured (provider, model, tools, budget) and run an assigned task for real,
using only authorized tools, with every call, tool execution, token and cost recorded.

| # | Milestone | Status |
| --- | --- | --- |
| M1 | Domain + DB: AiModels (pricing), AgentConfigurations, AgentToolPermissions, AgentRuns, AgentRunSteps, ToolExecutions, UsageRecords, CostRecords, Deliverables | done |
| M2 | Provider layer: neutral `IAiProvider` + Anthropic adapter (official SDK) + Simulated provider (dev/test only, always labelled) | done (Anthropic adapter compiles against SDK 12.58.0; not yet exercised with a real key) |
| M3 | Tool registry: declared id/version/schema/permission/timeout/limits/approval; Phase 2 tools are internal-data tools only | done |
| M4 | Agent runner: queued background execution, limits (turns, tool calls, tokens, time, monthly budget), cancellation, restart recovery | done |
| M5 | API + SignalR + tests | done (65 tests) |
| M6 | UI: Agent Activity, employee profile (model/tools/budget), run a task with AI, AI settings (models + pricing) | done (verified in browser with the simulated provider) |

### Decisions taken without the CEO (change any time)

- **Providers:** Anthropic (Claude) adapter built first. It stays *disabled* until an API key is configured
  (`AiProviders__Anthropic__ApiKey` or `ANTHROPIC_API_KEY`). OpenAI/Gemini adapters can be added behind the same interface later.
- **Simulated provider:** deterministic stand-in for development and tests. Every run it produces is marked
  `IsSimulated` and shown as such in the UI; simulated runs never count as real completed work.
- **Default models by position level:** Executive/Head/Senior → `claude-opus-5-5`, Staff → `claude-sonnet-5-5`.
  Routine models (`claude-haiku-5-5`) are available for the CEO to assign.
- **Budgets:** default USD 5 per AI employee per month, 200k tokens per run, 12 model turns, 20 tool calls, 10 minutes.
- **Prices:** seeded from Anthropic's published list (cached 2026-10-06) into `AiModels`; editable by the CEO. Costs are
  computed with decimal arithmetic and stored as Actual; estimates stay separate.

### Open questions for the CEO

1. Which AI provider accounts exist? (OpenAI / Google / others to add)
2. Monthly AI budget for the trial period (company-wide cap).
3. Deployment target after Phase 5.

### Known gaps carried forward

- Phase 3: a budget escalation does not raise the budget by itself; there is no budget-edit endpoint yet (cancel and restart with a higher budget for now).
- Phase 3: the engine is single-region; one tick at a time across instances via a PostgreSQL advisory lock.

- The Anthropic adapter has not been run against the real API (no key on this machine). First real run: set `ANTHROPIC_API_KEY` in `.env`, switch one AI employee to `anthropic` and run a small task.
- OpenAI / Gemini adapters not built (waiting on which accounts exist).
- Runs interrupted by a restart are marked Failed, not resumed; durable resume belongs to Phase 3 orchestration.

## Phase 3 — Orchestration ✅ (merged to master)

Goal: the CEO gives an objective; an AI planner proposes tasks; the CEO approves the plan; a deterministic
workflow engine assigns agents, runs ready tasks in parallel, routes deliverables to AI reviewers, retries
recoverable failures, escalates the rest, and asks a human for every final approval.

Design: the engine is ordinary code, not an LLM. Its state lives in the database and is re-derived on every
tick, so a restart simply continues. Agents coordinate only through tasks, deliverables, reviews and
approval requests — never free-form chat.

| # | Milestone | Status |
| --- | --- | --- |
| M1 | Data: WorkflowDefinitions, WorkflowExecutions, WorkflowEvents, ApprovalRequests, Reviews; task delegation fields; run purpose + attempt | done |
| M2 | Approval inbox: request/decide/expire, payload hash binding, no self-approval, agents never decide; approval-required tools queue an action that runs once after approval | done |
| M3 | Planner: plan.submit tool, plan validation (keys, deps, no cycles, size), plan approval creates tasks | done |
| M4 | Engine: agent selection (skills, workload, budget), ready-task scheduling with parallel limit, retries, escalation, workflow budget cap, completion, cancellation, advisory lock | done |
| M5 | Review + delegation tools: review.submit, task.delegate with depth limit | done |
| M6 | Tests incl. the end-to-end objective → plan → work → review → CEO approval | done (79 tests, incl. end-to-end with the simulated provider) |
| M7 | UI: approvals inbox, objectives/workflows on the project page, reviews in the task dialog | done (verified in browser: objective → plan approval → assignment by skill → work → AI review → approval inbox) |

Defaults: max 3 parallel runs per workflow, 2 retries per task, 5 runs per task, delegation depth 2,
workflow budget USD 20, approvals expire after 7 days.

## Phase 4 — Software house operations ✅ (merged to master)

Goal: AI employees do real software work — requirements, estimates, code in a Git repository, tests in a
locked-down sandbox, code review and merge — and nothing merges without a person approving it.

| # | Milestone | Status |
| --- | --- | --- |
| M1 | Requirements, architecture decisions (ADRs), estimates with deterministic pricing (rate card, contingency, margin, evidence flags); agent tools to record them | done (backend + tests; UI in M5) |
| M2 | Git repositories per project (local, git CLI, no shell), one branch + worktree per task; agent tools: list/read/search/write files, diff, commit — confined to the task workspace | done |
| M3 | Sandbox: tests run in a throwaway container — no network, read-only image, CPU/memory/pids/time limits, non-root; a separate runner service owns the Docker socket, the API never does | done (verified in compose: API → runner → container with no network, uid 1000, repo via volume subpath) |
| M4 | Merge requests: diff + test evidence, AI code review, merge only after human approval | done (backend in M2; AI review via the Phase 3 engine with read/diff/test tools) |
| M5 | Tests + UI: project workspace (requirements, ADRs, estimates, repository, merge requests, test runs) | done (104 tests; project tabs: requirements, ADRs, estimates, repository/MRs/test runs; approvals for estimates and merges; company settings page) |

Decisions: Git hosting starts local (bare repos in a volume); GitHub/GitLab integration later behind the same
interface. Customer prices in THB, AI costs in USD converted with a configurable rate. The sandbox image is a
generic toolchain image (.NET SDK + Node); per-project test commands are configured on the repository.

## Phase 5 — Company management ✅ (merged to master)

Finance and usage reporting (planned vs actual), internal knowledge with validation before promotion, document generation, HR/performance reports, external integrations.

| # | Milestone | Status |
| --- | --- | --- |
| M1 | Budgets (company / department / project, per month, USD) enforced at run start with 80% / 100% alerts; finance report: planned (approved estimate) vs actual AI cost per project, spend by department and employee | done (backend + tests) |
| M2 | Performance report per AI employee: runs, success rate, cost per run, review approvals vs changes requested, tasks done, tool denials | done (backend + tests) |
| M3 | Knowledge base: agents propose, people validate before it becomes authoritative; permission/project-scoped search with source references | done (backend + tests) |
| M4 | Documents: agents write versioned project documents (proposal, SOW, release notes, completion report) from templates | done (backend + tests; markdown download) |
| M5 | Integrations: outbound webhook notifications configured by people (secrets from environment only) | done (backend + tests) |
| M6 | UI for all of the above | done (finance/performance, knowledge, integrations pages; documents tab; verified in browser) |

## Phase 6 — Production readiness ✅ (merged to master)

Security testing, reliability/concurrency testing, observability, backup/restore, deployment docs; Quality & Security overview page; company chat/announcements still open.

| # | Milestone | Status |
| --- | --- | --- |
| M1 | Security hardening + tests: security headers, CSRF guard on state-changing API calls, secure cookies outside development, cross-project isolation of agent tools, dependency vulnerability scan | done (126 tests; dotnet + npm audits clean on 2026-10-18) |
| M2 | Reliability: concurrent requests (double start, racing edits, parallel engine ticks) resolve to clean 409s, never 500s or duplicates | done (5 race tests, stable over repeated runs) |
| M3 | Observability: OpenTelemetry traces/metrics (OTLP when configured), JSON logs in production, readiness check, domain metrics (runs, tool calls, cost) | done (133 tests) |
| M4 | Backup & restore scripts for PostgreSQL + repositories, with a real restore drill | done (drill 2026-10-18: restored copy matched live counts — 5 tasks, 593 audit rows, 4 runs, 9 migrations, 53 repo files; append-only trigger intact) |
| M5 | Deployment guide + production compose override (HTTPS proxy, no simulated AI, secrets) | done (docs/DEPLOYMENT.md, docker-compose.prod.yml + Caddy; config validated; change-password endpoint + UI added) |
| M6 | Quality & Security overview page (tests, merge requests, failed runs, security events) | done (135 tests; verified in browser) |

## Phase 7 — Company communication ✅ (merged to master)

Company chat, internal announcements, meeting agendas/minutes (Master Prompt §8 and the "Company Chat" navigation item).

| # | Milestone | Status |
| --- | --- | --- |
| M1 | Backend: channels (company / department / project, one per department/project, race-safe), messages with unread counts, author/moderator deletion (text kept, hidden); announcements (draft → published by a person, department audience, pin, read acknowledgement); meetings (agenda, participants + attendance, minutes approved by a person and filed as a final project document, action items → tasks exactly once); agent tools `chat.read`, `chat.post`, `meeting.list`, `meeting.draft_minutes`, `announcement.draft` — all scoped to the task's project or draft-only | done (144 tests) |
| M2 | UI: Company Chat (live, unread badges, load earlier), Announcements (acknowledge, manager tabs), Meetings list + detail (agenda, attendance, minutes, action items), dashboard card, "Project chat" button on projects | done (verified in browser) |

Decisions taken without the CEO: new permissions `comms.post` (Member, Manager, AI agents) and `comms.manage` (CEO, Manager; human-only). Every signed-in person can read every channel (there is no per-project membership model yet). Chat messages are not written to the audit log row by row; they keep author, AI and simulated flags themselves. Only executives' AI employees may draft announcements; writers, heads and executives may draft minutes.

## Phase 8 — Remaining planned work ✅ (merged to master)

The planned items left open after Phase 7, each with its own commit and Update log entry (v0.7.2–v0.7.7).

| Item | Status |
| --- | --- |
| Approving a budget escalation raises the workflow budget (default ×2, must exceed spend; asks again at the new limit) | done |
| DataProtection keys encrypted at rest with a certificate (required in production; rotation supported; `scripts/new-dataprotection-cert.sh`) | done (verified in the Linux image) |
| Project channel membership (owner, task assignees, people added by hand; managers see all), @mentions, notification bell (mentions, meeting invites, action items) | done (also fixed: editing meeting participants/agenda failed with 409) |
| OpenAI and Google Gemini adapters over HTTP (tools, cached-token accounting, error mapping); disabled until a key is set; models/prices added in AI settings | done (adapter tests against a fake endpoint; no real call yet) |
| GitHub / GitLab remotes: push the default branch after approved merges or on demand (never forced), import an existing repository; tokens only from `GIT_REMOTE_*`; `security.manage` chooses the destination | done (tested against local bare repositories) |
| Interrupted AI runs resume from a checkpoint after a restart (max 2 resumes; tools are not repeated) | done |

159 tests; `dotnet list package --vulnerable` and `npm audit` clean on 2026-10-18.

## Open items (not built yet)

- First real run with any provider needs its key (ANTHROPIC_API_KEY / OPENAI_API_KEY / GEMINI_API_KEY); OpenAI/Gemini models and prices are added by the CEO in AI settings.

## Phase 9 — Master Prompt gaps ✅ (merged to master, 2026-10-19)

Items from the Master Prompt that were not built yet, each with an Update log entry (v0.14.0–v0.14.3); v0.14.4 hides the month picker on the profitability tab after a browser walkthrough; v0.14.5 adds the task board to the top bar (icon-only pills below 1280px).

| Item | Status |
| --- | --- |
| §9 Profitability per project (planned vs forecast margin, AI overrun, at-risk flag) + read-only what-if scenario planning on the approved estimate | done (Finance → “Profitability & scenarios”) |
| §15 Model selection by task complexity: task `Complexity` (Routine/Standard/Complex) routes runs to the cheapest routine-tier / advanced-tier model of the same provider; planner can set it | done (default Standard = no change) |
| §11 Company-wide Task Board (/tasks) with project / assignee / priority / overdue filters | done |
| §15 Caching where safe: automatic prompt caching on Anthropic calls | done (not yet exercised against the real API) |

187 tests. Not changed on purpose: the dashboard keeps its one-screen layout; profitability and quality live on the Finance and Quality pages.

## Phase 10 — Delivery stage 8: project closeout ✅ (2026-10-19)

| Item | Status |
| --- | --- |
| §7 Stage 8 “compare planned and actual effort and costs”: `GET /projects/{id}/closeout` (finance.read) — tasks/milestones, AI runs and hours, planned vs actual AI cost and forecast margin (same formula as Profitability), tests/MRs/reviews/deliverables, lessons, warning codes | done (project page → “Project closeout” tab) |
| §7 Stage 8 “generate a project completion report”: `POST /projects/{id}/closeout/report` (finance.read + knowledge.manage) writes a deterministic Markdown report as a new draft `CompletionReport` version, Thai or English | done |

Human working hours are not recorded anywhere, so the report shows them as “not recorded” rather than inventing an actual. 189 tests.

## Phase 11 — Per-project access control ✅ (2026-10-19)

| Item | Status |
| --- | --- |
| §14 “Department, project, and resource-level access control” / “protection against cross-project data leakage”: new permission `project.all` (CEO, Manager, Auditor by default). Everyone else sees only projects they belong to (owner, task assignee, added member) | done |
| Enforcement: `ProjectScopeMiddleware` loads the caller's visible project ids once per request into `ProjectScope`; EF global query filters on every project-owned table (and children via task/run/repository/workflow/estimate) hide the rest, so lists omit them and ids return 404. Background work (AI runs, workflow engine) is unrestricted; agent tools stay scoped to their task's project | done |
| Project page “Members” dialog (shared with chat); chat “sees all project channels” now follows `project.all` (or comms.manage) instead of project.manage | done |

Not filtered on purpose: chat channels and meetings keep their own membership rules; realtime events carry ids only and clients re-read through the filtered API. 191 tests.

## Phase 12 — Data retention ✅ (2026-10-19)

| Item | Status |
| --- | --- |
| §14 “Data retention and deletion policies”: `RetentionPolicy` (one row) with per-kind periods — AI run transcripts (steps, saved conversation, tool output/input), test output text, notifications, webhook delivery log, chat message text. Null = keep forever (default) | done |
| `RetentionWorker` applies it daily (`Retention:Enabled`, `Retention:IntervalHours`); `GET/PUT /security/retention`, `GET /security/retention/preview`, `POST /security/retention/apply` (security.manage); every clean-up writes a `RetentionApplied` audit entry with counts | done |
| Security page “Data retention” section with preview + confirmation before removing | done |

Never removed: costs/usage, documents, knowledge, estimates, the audit log (append-only by trigger). 193 tests.

## Phase 13 — Delivery stage gates ✅ (2026-10-19)

| Item | Status |
| --- | --- |
| §7 “Each stage must have explicit entry criteria, exit criteria, deliverables, and approval requirements”: `StageGateService` evaluates the eight stages from recorded data; `GET /projects/{id}/stages` (project.read). Stage done = entry + exit + approval met; current = first not done; later stages need the previous one done | done |
| Approvals map to existing human gates: requirements approved, estimate approved, ADR accepted, merge requests approved and merged, tasks Done (approver only), release notes / completion report made final | done |
| Project page “Delivery stages” tab | done |

Read-only: gates report, they don't block actions (blocking stays where it already is: estimate/merge/plan approvals, Done needs task.approve). Production deployment is still outside the system. 194 tests.

## Phase 14 — Employee profile + priority ordering fix ✅ (2026-10-19)

| Item | Status |
| --- | --- |
| §11 AI Employee Profile “role and manager, skills, current assignments, work history, deliverables, quality metrics”: `GET /employees/{id}/overview` (agent.read) + profile cards; respects the project scope | done |
| Bug: `TaskPriority` is stored as text, so `OrderBy(t => t.Priority)` sorted alphabetically. Fixed in the workflow engine dispatch, task board and assistant with `TaskOrdering.PriorityRank`; regression test on the board | done |

196 tests.


## Phase 15 — Defect tracking (QA) ✅ (2026-10-19)

| Item | Status |
| --- | --- |
| §7 Stage 6 “Record reproducible defects”: `Defect` (DEF-001…, severity, steps to reproduce required, expected/actual, environment, affected task, failing test run). `GET/POST /projects/{id}/defects` (task.read / task.manage) | done |
| §7 Stage 6 “Send failed tasks back to the appropriate developer”: each defect creates a fix task (priority from severity, same milestone/skills) assigned to the last assignee of the affected task; reopen creates a new fix task for the same developer | done |
| §7 Stage 6 “Prevent failed quality gates from being marked as passed”: a task with an open defect cannot move to Done (board and approval inbox); `verify` (needs the fix task Done), `reject` (cancels an unstarted fix task), `reopen` — task.approve, human-only | done |
| QA stage gate adds “no open defects”; AI tool `defect.report` for quality positions (granted once to existing employees) | done |
| Project page “Defects” tab | done (typecheck/lint clean; browser walkthrough not done — Chrome extension was not connected; API smoke-tested on the running stack) |

Fix tasks are ordinary tasks, not part of a workflow, so the engine does not start them by itself; run them with AI from the task dialog. 200 tests.

## Phase 16 — Release preparation ✅ (2026-10-19)

| Item | Status |
| --- | --- |
| §7 Stage 7 “Prepare release notes, deployment instructions, and rollback plans”: `Release` per project (tag unique, notes, deployment plan, rollback plan, known issues); `GET/POST /projects/{id}/releases`, `PUT /releases/{id}` (drafts only) | done |
| §7 Stage 7 “Verify build artifacts and test evidence” / “Identify unresolved issues”: `GET /releases/{id}/readiness` — blocking: plans written, latest test run on the current default-branch commit passed, no critical defects; warnings: open defects, blocked/failed tasks, open merge requests (listed as unresolved issues) | done |
| §7 Stage 7 / §14 “Require human approval for production deployment”: `POST /releases/{id}/submit` freezes commit + test run + issues into a `ProductionDeployment` approval (project.manage, requester cannot approve). Approval is refused if the default branch moved since; rejection returns the release to Draft | done |
| Deployment stays outside the system: people record `deployed` / `rolled-back` (note required) / `cancel` (cancels a pending approval); agents cannot | done |
| AI tool `release.prepare` (RELEASE, DEVOPS) drafts releases only; stage 7 approval = an approved/deployed release, plus “deployment and rollback plans written” | done |
| Project page “Releases” tab; approval inbox shows tag, commit, plans and unresolved issues | done (typecheck/lint clean; browser walkthrough not done — Chrome extension not connected; API smoke-tested on the running stack) |

208 tests.

## Phase 17 — Customer intake ✅ (2026-10-19)

| Item | Status |
| --- | --- |
| §7 Stage 1 “Receive customer requirements”: `Lead` (company-wide code LEAD-001…, customer, contact, source, need, goal, users, platforms, constraints, budget, desired date) with status New → Clarifying → Qualified → Converted / Lost; `GET/POST /leads`, `GET/PUT /leads/{id}` | done |
| §7 Stage 1 “Detect missing information”: deterministic checks (contact, need detail ≥ 40 chars, business goal, target users, budget, desired date — blocking; platforms, constraints — optional; every question answered — blocking) | done |
| §7 Stage 1 “Ask clarifying questions”: `POST /leads/{id}/questions` and `/questions/suggest` (one templated question per gap, th/en, no duplicates of open questions); answers recorded by people (`/questions/{qid}/answer`). Nothing is sent to the customer by the system | done |
| `POST /leads/{id}/qualify` (refused while blocking checks fail), `/lost` (reason required), `/convert` (qualified only; creates a Draft project with budget and due date plus a “Project brief” document holding the request and every answer) | done |
| Access: read needs `project.all`, write `project.manage`; restricted people get 403 | done |
| Stage 1 gate adds “clarificationsAnswered” (follow-up questions on a converted lead hold the stage) | done |
| `/leads` page (nav “Customer intake”) | done (typecheck/lint clean; browser walkthrough not done — Chrome extension not connected) |

207 tests.

## Phase 18 — Customer delivery package ✅ (2026-10-19)

| Item | Status |
| --- | --- |
| §7 Stage 8 “Deliver to customer / provide documentation”: `DeliveryPackage` per project (PKG-001…, release, pinned document-version ids, handover notes); `GET/POST /projects/{id}/delivery-packages`, `PUT /delivery-packages/{id}` (drafts only) | done |
| Readiness (blocking): release Deployed, release test run Passed, completion report included, handover notes; warnings: documents not Final, open defects | done |
| `GET /delivery-packages/{id}/export`: one Markdown file (manifest, handover, release notes, deployment/rollback, known issues, test evidence, every document's content); `Content-Disposition` exposed via CORS | done |
| People only: `POST /delivery-packages/{id}/delivered` (needs readiness), `/response` (accepted, or changes requested with required feedback → High-priority change-request task linked as `ChangeTaskId`) | done |
| Stage 8 exit adds “packageDelivered” and “customerAccepted”; deliverable “deliveryPackage” | done |
| Project page “Customer delivery” tab | done (typecheck/lint clean; browser walkthrough not done — Chrome extension not connected) |

210 tests.

## Phase 19 — Lint, static analysis and secret scanning ✅ (2026-10-19)

| Item | Status |
| --- | --- |
| §6 “lint/format/static analysis” tools: `CodeRepository.LintCommand` (people only, via `PUT /repositories/{id}` or create); `TestRun.Kind` Test/Lint (existing rows default Test) | done |
| AI tool `sandbox.run_lint` (same sandbox and evidence as `sandbox.run_tests`; refused when no lint command); people run it with `POST /repositories/{id}/test-runs {kind: "Lint"}` | done |
| AI tool `repo.scan_secrets`: deterministic scan of added lines (committed + uncommitted + new files) for private keys, AWS/Anthropic/OpenAI/GitHub/GitLab/Google/Slack tokens and hard-coded credentials; placeholders and env lookups skipped; reports file:line (rule), never the value | done |
| `repo.open_merge_request` also requires a passing lint run on the head commit when a lint command is set, and no credentials in the committed diff | done |
| Stage gates, release readiness, closeout and quality reports count `Kind == Test` only | done |
| Both tools granted with the code-read tool set (seed marker `v0.23.0-lint-tools`); Repository tab: lint command chip, “Edit checks” dialog, “Run lint”, kind chip on runs | done (typecheck/lint clean; browser walkthrough not done) |

213 tests. `Sandbox_has_no_network_and_cannot_modify_the_workspace` failed once in a full run and passed on 3 reruns (timing-sensitive network check); its assertion now prints the sandbox output.


## Retry a failed AI run (2026-10-20)

| Item | Status |
| --- | --- |
| `POST /agent-runs/{id}/retry {note?}` (agent.run, people only): the task's latest Failed / LimitReached / Cancelled run of that purpose; same employee and purpose, new attempt number. Refused for succeeded, active or older runs, and for tasks marked Failed (move to Todo and reassign) | done |
| A pending escalation on the task is answered as “try again” through `ApprovalService.DecideAsync` (needs task.manage); inside an executing workflow the engine then starts the run and counts attempts from that decision. Otherwise a Blocked task is unblocked (needs task.update_status), the note is saved as a human review, and the run starts at once | done |
| “Try again” button with an optional note in the run drawer and next to “Run with AI” on the task's latest run | done (verified in browser 2026-10-20: failed run → note saved, attempt #2 on Gemini succeeded, task moved to InReview) |

215 tests.

## Phase 20 — Email notifications and calendar ✅ (2026-10-20)

| Item | Status |
| --- | --- |
| `UserPreference` (per user: `EmailNotificationsSince`, calendar token hash) and `EmailDelivery` (one per notification, Sent/Failed, attempts) | done |
| `IEmailSender` (MailKit SMTP; `Email:Host/Port/Security/Username/Password/FromAddress`, compose `EMAIL_*`); not configured = nothing is sent | done |
| `EmailDispatcher` + `EmailWorker` (every 20 s): emails each opted-in person their notifications created after opting in and within the last day; meeting invites attach an iCalendar REQUEST; failures retried up to 3 times | done |
| `/api/me/delivery-settings`, `PUT /me/email-notifications`, `POST /me/email-test`, `POST/DELETE /me/calendar-feed` (own settings only) | done |
| `GET /api/calendar/feed/{token}.ics` (anonymous, rate-limited, token = 32 random bytes, SHA-256 stored): my meetings −60 d…+365 d and due dates of open tasks assigned to me; `GET /meetings/{id}/ics` | done |
| “My settings” page (nav, all users), “Add to calendar (.ics)” on a meeting | done (verified in browser: page, link creation, feed and meeting .ics content) |
| Fixed: API process now runs with invariant culture (a Thai-culture host wrote Buddhist-era years into server text); a workflow test only passed under Thai collation and now parses the JSON | done |

Open: an SMTP account (waiting on the CEO). On localhost the calendar link only works for calendar apps on the same computer; online Google/Outlook need the deployed address (`Integrations:ApiBaseUrl`).

220 tests.

Browser check 2026-10-20 (CEO, real data): Defects, Release, Customer delivery and Repository tabs of AI-DEMO open with their empty states and no console errors; Update log shows v0.24.0. Not exercised with new records (that would add test data to the live company); their API paths are covered by the automated tests.

## Phase 21 — Free AI providers and the daily report ✅ (2026-10-22)

| Item | Status |
| --- | --- |
| `OllamaProvider` (OpenAI-compatible API, `AiProviders:Ollama:BaseUrl`, compose `OLLAMA_URL`, default `http://host.docker.internal:11434/v1`, 900 s timeout) | done |
| `ClaudeCodeProvider`: AI employees on the Claude Code CLI through `claude-bridge.mjs` (company tools described in the prompt, `<tool_call>` blocks, whole conversation per turn, validated `--model`, token usage returned) | done |
| `IAiProvider.IsFlatRate`: models of Ollama / Claude Code may have zero prices | done |
| `DailyReportService` + `DailyReportWorker`: yesterday's tasks done, deliverables, runs/failures, pending approvals and cost, built from records only (no model call), posted to the company channel after midnight `Asia/Bangkok`; `GET /api/reports/daily?date=` | done |
| Chat replies get up to 3 validated knowledge entries matching the question; knowledge search matches tags contained in the query (Thai has no word spaces); excerpts up to 900 chars | done |
| A run ends once the task is handed on (delegate, request review, report blocked, plan/review submit); request review fails while delegated sub-tasks are open | done |
| `TrainingPromptDumpTests`: writes the agents' real requests as OpenAI-style JSON when `OFFICEAI_DUMP_DIR` is set (fine-tuning data, `D:\FreeAi\train`) | done |

Open: fine-tuned Qwen3-8B (training in `D:\FreeAi\train`) → GGUF → Ollama → evaluate → add in AI settings. Not yet checked in the browser.

231 tests.

## Phase 22 — Fallback models ✅ (2026-10-23)

| Item | Status |
| --- | --- |
| §15 “Graceful handling of provider failures”: `AiModel.FallbackRank` (company-wide order, max 10); `PUT /api/ai/fallback {modelIds}` (agent.manage, people only; enabled real models only, no duplicates); disabling a model removes it from the order | done |
| `AiFallback.ShouldSwitch`: retryable errors once retries are used up, rate limits (a daily quota switches at once), and account problems (`AiProviderException.AccountProblem`: not configured, 401/402/403, no credit). Rejected requests (400 etc.) never switch | done |
| `AgentExecutor`: a failing call moves the run to the next usable fallback (same provider kind: never simulated ↔ real), continuing the saved conversation; a Notice step records from/to/reason; each turn's usage and cost use the model that answered. A run whose provider is not configured, or whose model is cooling down, starts on the fallback | done |
| Cooldown (in memory, per model): 1 h after a daily quota or account problem, 2 min (or the provider's Retry-After) after an outage. Cooling models go last in the order | done |
| `AgentRunService.CreateRunAsync` and `AgentSelector`: an employee whose provider is not set up runs on the first fallback; `ChatReplyService` falls back the same way | done |
| Gemini: tool calls made by another model are sent with the documented placeholder thought signature | done (not yet exercised against the real API) |
| AI settings: “Fallback models” card (add, reorder, remove, “provider not ready” warning) | done (typecheck/lint clean; browser walkthrough not done) |

237 tests.

## Phase 23 — Two-factor sign-in and the terminal step-up ✅ (2026-10-23)

| Item | Status |
| --- | --- |
| §14 security: TOTP two-factor sign-in (ASP.NET Identity `AuthenticatorTokenProvider`; key and recovery codes in `UserTokens`, no migration). `GET /auth/2fa`, `POST /auth/2fa/setup` (new key each time until confirmed; refused while on), `/enable {code}` (10 recovery codes, shown once), `/disable {password, code or recovery code}` (resets the key), `/recovery-codes {code}` | done |
| Login: a right password with 2FA on answers `200 {requiresTwoFactor:true}` and signs nothing in; `POST /auth/login/2fa {code | recoveryCode}` finishes it (rate-limited, failures count towards lockout, audited) | done |
| Terminal step-up: `/assistant/terminal` needs the CEO role, 2FA on (`terminal.2fa_required`) and an open terminal (`terminal.locked`, 403). `POST /assistant/terminal/unlock {code}` opens it for 15 idle minutes (`TerminalGate`, in memory, bound to the security stamp); `/lock`, `/status`; sign-out locks it | done |
| UI: login code step (authenticator or recovery code), “Two-factor sign-in” card on My settings (QR via `qrcode`, key to type, recovery codes, turn off), terminal asks for the code inline and then runs the waiting command | done (typecheck/lint clean) |

The authenticator accepts codes from the neighbouring 30-second steps. Since v0.32.1 (`OneTimeAuthenticatorTokenProvider`) each step is accepted once per user (last step kept in UserTokens), so codes cannot be replayed. 239 tests.

## Phase 24 — Operating costs ✅ (2026-10-23)

| Item | Status |
| --- | --- |
| §9 “Planned versus actual AI and infrastructure costs”: `OperatingCost` (name, category, monthly amount THB/USD, estimate flag, first/last month, allocation, project or provider, notes). Kept out of `CostRecords`, so AI budgets (which stop runs) never count it | done |
| `OperatingCostAllocator` (pure): Project → one project; AiUsage → split by that month's non-simulated tokens per project, optionally of one provider, rounding remainder to the largest user so each month adds up exactly, no usage = unallocated; Overhead → never charged to a project. USD uses the current company rate | done |
| `GET/POST /operating-costs`, `PUT/DELETE /operating-costs/{id}` (finance.read / finance.manage, people only) | done |
| Finance report: operating total, estimated part, AI + operating total, per-project month/all-time columns, per-cost split. Profitability forecast cost and closeout (and the completion report) add allocated operating costs | done |
| Finance page: operating cards, list with splits, manage table and dialog; profitability column | done (typecheck/lint clean; stack rebuild waits for the r2 training run, which needs the RAM) |

242 tests.

## Phase 25 — Knowledge search by meaning ✅ (2026-10-23)

| Item | Status |
| --- | --- |
| §10 “Support search and retrieval with source references”: `IEmbeddingProvider` + `OllamaEmbeddingProvider` (`/api/embed` at the Ollama root, `AiProviders:Ollama:EmbeddingModel`, compose `OLLAMA_EMBED_MODEL`; empty = off) | done |
| `KnowledgeEmbedding` table (real[] vector, model, content hash; cascade with the entry, not audited). `KnowledgeEmbeddingIndexer` + `KnowledgeEmbeddingWorker` (every 30 s): new/edited entries, model change, archived entries dropped | done |
| `KnowledgeSearch`: keyword ranking + cosine ranking fused by reciprocal rank; meaning-only hits need cosine ≥ 0.55; scope and status filters as before; any embedding failure → keywords only. Used by `knowledge.search`, chat replies and `GET /knowledge?q=` | done |
| Tests with deterministic fake embeddings (no shared words, unrelated question, failure fallback, model change, archive) and the Ollama adapter against a fake endpoint | done |

No pgvector: the knowledge base is small, so vectors are compared in memory. The 0.55 floor is set for bge-m3 and not yet tuned on the company's real entries. 245 tests.

## Phase 26 — Quality by model and training data from real work ✅ (2026-10-23)

| Item | Status |
| --- | --- |
| `GET /reports/models?month=` (finance.read): per provider/model — runs (on the model that finished them), success rate, deliverables accepted/rejected by AI reviewers, tasks Done (human approval), average turns and seconds, tokens, cost per run, fallback switches away/in (from the run's “Switched from … to the fallback model …” notices) | done |
| `TrainingExportService`: Succeeded, non-simulated runs with a saved conversation — Work runs whose task is Done and whose deliverable was not rejected, Plan runs whose plan was approved. One JSONL line each: `tag` (`real-<purpose>-<role>`), `tools` (OpenAI functions from the employee's current grants), `messages` (exact system prompt, conversation via `OpenAiProvider.BuildRequest`, trailing tool results trimmed) | done |
| `GET /training/summary`, `GET /training/export?since=` (agent.manage, people only; export audited as `TrainingDataExported`) | done |
| UI: “Quality by model” table on Finance → Performance; “Training data from real work” card on AI settings | done (typecheck/lint clean) |

Tools are rebuilt from today's grants, not stored per run, so a grant changed since the run shows its current tools. Runs whose transcript the retention policy removed cannot be exported.

## Phase 27 — Timesheet ✅ (2026-10-23)

| Item | Status |
| --- | --- |
| `TimeEntry` (employee, project, optional task, date, hours 0.25–24 in quarter hours, note), project-scoped query filter | done |
| `TimesheetService`: people with a human employee profile log their own hours (no future days, 90 days back, ≤ 24 h a day, task must belong to the project, not on cancelled projects); edit/delete own only. `GET/POST /timesheet`, `PUT/DELETE /timesheet/{id}` (task.read), `GET /projects/{id}/hours` (project.read) | done |
| Closeout: actual hours, people, actual labour at position rates (null when any position has no rate), planned labour; completion report rows. Profitability forecast adds labour above plan (`LabourOverrunThb`) | done |
| UI: `/timesheet` (nav “Timesheet”), closeout effort table shows actual hours and labour | done (typecheck/lint clean) |

AI employees never log time: their effort is their runs.
