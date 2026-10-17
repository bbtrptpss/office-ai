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

- The Anthropic adapter has not been run against the real API (no key on this machine). First real run: set `ANTHROPIC_API_KEY` in `.env`, switch one AI employee to `anthropic` and run a small task.
- OpenAI / Gemini adapters not built (waiting on which accounts exist).
- Runs interrupted by a restart are marked Failed, not resumed; durable resume belongs to Phase 3 orchestration.

## Phase 3 — Orchestration (next, branch `phase-3`)

Delegation, dependencies and parallel execution, review workflows, retries/timeouts/cancellation/recovery, human approval inbox.

## Later phases

4 Software house operations · 5 Company management · 6 Production readiness
