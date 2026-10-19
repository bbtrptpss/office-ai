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
