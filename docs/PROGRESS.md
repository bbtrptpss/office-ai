# Progress

Resume here. Each phase is built on its own branch and committed per milestone.

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

## Phase 4 — Software house operations (next, branch `phase-4`)

Requirements and estimation, project planning, repository integration, controlled coding/testing/code review in a sandbox, deliverable management.

## Later phases

5 Company management · 6 Production readiness
