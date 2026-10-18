using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Agents;

/// <param name="KeySource">"settings" (saved on the AI settings page), "environment" (server .env) or null.</param>
public record ProviderDto(string Name, bool IsAvailable, bool IsSimulated, string? KeySource = null, string? KeyLast4 = null);
public record SetProviderKeyRequest(string ApiKey);
public record VerifyProviderDto(bool Ok, string? Problem);
public record CreateAiModelRequest(string Provider, string ModelId, string DisplayName, ModelTier Tier, decimal InputUsdPerMTok, decimal OutputUsdPerMTok,
    decimal CacheReadUsdPerMTok = 0, decimal CacheWriteUsdPerMTok = 0);
public record AiModelDto(Guid Id, string Provider, string ModelId, string DisplayName, ModelTier Tier, decimal InputUsdPerMTok,
    decimal OutputUsdPerMTok, decimal CacheReadUsdPerMTok, decimal CacheWriteUsdPerMTok, bool IsEnabled, uint Version);
public record UpdateAiModelRequest(decimal InputUsdPerMTok, decimal OutputUsdPerMTok, decimal CacheReadUsdPerMTok, decimal CacheWriteUsdPerMTok, bool IsEnabled, uint? Version = null);
public record ToolDto(string Id, string Version, string Description, bool RequiresApproval, bool HasSideEffects, int TimeoutSeconds);

public record AgentConfigDto(Guid EmployeeId, string Provider, string ModelId, Effort Effort, int MaxTokensPerCall, int MaxTurnsPerRun,
    int MaxToolCallsPerRun, int MaxTokensPerRun, int TimeoutSeconds, decimal MonthlyBudgetUsd, bool IsEnabled,
    List<string> Tools, decimal SpentThisMonthUsd, uint Version);
public record UpdateAgentConfigRequest(string Provider, string ModelId, Effort Effort, int MaxTokensPerCall, int MaxTurnsPerRun,
    int MaxToolCallsPerRun, int MaxTokensPerRun, int TimeoutSeconds, decimal MonthlyBudgetUsd, bool IsEnabled, uint? Version = null);
public record SetAgentToolsRequest(List<string> Tools);

public record StartRunRequest(Guid? EmployeeId = null);
public record AgentRunDto(Guid Id, Guid TaskId, string TaskTitle, Guid ProjectId, Guid EmployeeId, string EmployeeName, AgentRunStatus Status,
    string Provider, string ModelId, bool IsSimulated, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
    int Turns, int ToolCalls, long InputTokens, long OutputTokens, decimal CostUsd, string? StopReason, string? Error, RunPurpose Purpose, int Attempt);
public record RunStepDto(int Sequence, RunStepKind Kind, string Content, string? ToolCallId, DateTimeOffset CreatedAt);
public record ToolExecutionDto(Guid Id, string ToolId, string ToolVersion, string ToolCallId, ToolExecutionStatus Status, string InputJson,
    string? Output, string? Error, int DurationMs, DateTimeOffset StartedAt);
public record AgentRunDetailDto(AgentRunDto Run, List<RunStepDto> Steps, List<ToolExecutionDto> ToolExecutions);
public record DeliverableDto(Guid Id, Guid TaskId, Guid EmployeeId, string EmployeeName, Guid? RunId, string Title, string Format, string Content,
    DeliverableStatus Status, bool IsSimulated, DateTimeOffset CreatedAt);
public record UsageSummaryDto(decimal ThisMonthUsd, decimal ThisMonthSimulatedUsd, long ThisMonthTokens, int RunsThisMonth, int ActiveRuns,
    List<EmployeeUsageDto> ByEmployee);
public record EmployeeUsageDto(Guid EmployeeId, string DisplayName, decimal CostUsd, long Tokens, int Runs, decimal BudgetUsd);

/// <summary>Configuration of AI employees, models and prices. Callers need agent.manage (human only).</summary>
public class AgentAdminService(IAppDbContext db, AiProviderRegistry providers, ToolRegistry tools, TimeProvider clock, ICurrentUser current, IAiCredentials credentials)
{
    public List<ProviderDto> ListProviders() =>
        providers.All.OrderBy(p => p.IsSimulated).ThenBy(p => p.Name).Select(p =>
        {
            var last4 = p.IsSimulated ? null : credentials.Last4(p.Name);
            var source = p.IsSimulated ? null : last4 is not null ? "settings" : p.IsAvailable ? "environment" : null;
            return new ProviderDto(p.Name, p.IsAvailable, p.IsSimulated, source, last4);
        }).ToList();

    private IAiProvider RealProvider(string name)
    {
        var p = providers.Find(name) ?? throw new DomainException("validation", $"Unknown provider '{name}'.");
        if (p.IsSimulated) throw new DomainException("validation", "The simulated provider has no key.");
        return p;
    }

    /// <summary>Saves a provider key (encrypted). Write-only: it is never shown again, only its last 4 characters.</summary>
    public async Task<ProviderDto> SetKeyAsync(string provider, SetProviderKeyRequest req, CancellationToken ct)
    {
        if (current.IsAgent) throw new ForbiddenException("Only a person can set provider keys.");
        var p = RealProvider(provider);
        var key = (req.ApiKey ?? "").Trim();
        if (key.Length is < 10 or > 500 || key.Any(char.IsWhiteSpace)) throw new DomainException("validation", "The key looks invalid (10–500 characters, no spaces).");
        await credentials.SetAsync(p.Name, key, ct);
        return ListProviders().First(x => x.Name == p.Name);
    }

    /// <summary>Removes a saved key; the server environment key (if any) applies again.</summary>
    public async Task<ProviderDto> RemoveKeyAsync(string provider, CancellationToken ct)
    {
        if (current.IsAgent) throw new ForbiddenException("Only a person can remove provider keys.");
        var p = RealProvider(provider);
        await credentials.SetAsync(p.Name, null, ct);
        return ListProviders().First(x => x.Name == p.Name);
    }

    public async Task<VerifyProviderDto> VerifyAsync(string provider, CancellationToken ct)
    {
        var problem = await RealProvider(provider).VerifyAsync(ct);
        return new VerifyProviderDto(problem is null, problem);
    }

    /// <summary>People add models (e.g. for OpenAI or Gemini) with their list prices; nothing is assumed free.</summary>
    public async Task<AiModelDto> CreateModelAsync(CreateAiModelRequest req, CancellationToken ct)
    {
        RealProvider(req.Provider);
        Validate.Required(req.ModelId, "modelId");
        Validate.Required(req.DisplayName, "displayName");
        var modelId = req.ModelId.Trim();
        if (modelId.Length > 100 || modelId.Any(char.IsWhiteSpace)) throw new DomainException("validation", "modelId is invalid.");
        if (!Enum.IsDefined(req.Tier)) throw new DomainException("validation", "tier is invalid.");
        if (new[] { req.InputUsdPerMTok, req.OutputUsdPerMTok, req.CacheReadUsdPerMTok, req.CacheWriteUsdPerMTok }.Any(x => x < 0))
            throw new DomainException("validation", "Prices cannot be negative.");
        if (req.InputUsdPerMTok == 0 && req.OutputUsdPerMTok == 0) throw new DomainException("validation", "Enter the model's prices; real models are never free.");
        if (await db.AiModels.AnyAsync(m => m.Provider == req.Provider && m.ModelId == modelId, ct))
            throw new DomainException("validation", "This model is already listed.");
        var model = new AiModel
        {
            Provider = req.Provider, ModelId = modelId, DisplayName = req.DisplayName.Trim(), Tier = req.Tier,
            InputUsdPerMTok = req.InputUsdPerMTok, OutputUsdPerMTok = req.OutputUsdPerMTok, CacheReadUsdPerMTok = req.CacheReadUsdPerMTok, CacheWriteUsdPerMTok = req.CacheWriteUsdPerMTok,
        };
        db.AiModels.Add(model);
        await db.SaveChangesAsync(ct);
        return (await ListModelsAsync(ct)).First(m => m.Id == model.Id);
    }

    public List<ToolDto> ListTools() =>
        tools.All.OrderBy(t => t.Descriptor.Id).Select(t => new ToolDto(t.Descriptor.Id, t.Descriptor.Version, t.Descriptor.Description,
            t.Descriptor.RequiresApproval, t.Descriptor.HasSideEffects, (int)t.Descriptor.Timeout.TotalSeconds)).ToList();

    public async Task<List<AiModelDto>> ListModelsAsync(CancellationToken ct) =>
        await db.AiModels.AsNoTracking().OrderBy(m => m.Provider).ThenByDescending(m => m.Tier).ThenBy(m => m.ModelId)
            .Select(m => new AiModelDto(m.Id, m.Provider, m.ModelId, m.DisplayName, m.Tier, m.InputUsdPerMTok, m.OutputUsdPerMTok,
                m.CacheReadUsdPerMTok, m.CacheWriteUsdPerMTok, m.IsEnabled, m.Version))
            .ToListAsync(ct);

    public async Task<AiModelDto> UpdateModelAsync(Guid id, UpdateAiModelRequest req, CancellationToken ct)
    {
        var model = await db.AiModels.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new NotFoundException("AiModel", id);
        if (req.Version is { } v) db.ExpectVersion(model, v);
        if (new[] { req.InputUsdPerMTok, req.OutputUsdPerMTok, req.CacheReadUsdPerMTok, req.CacheWriteUsdPerMTok }.Any(p => p < 0))
            throw new DomainException("validation", "Prices cannot be negative.");
        model.InputUsdPerMTok = req.InputUsdPerMTok;
        model.OutputUsdPerMTok = req.OutputUsdPerMTok;
        model.CacheReadUsdPerMTok = req.CacheReadUsdPerMTok;
        model.CacheWriteUsdPerMTok = req.CacheWriteUsdPerMTok;
        model.IsEnabled = req.IsEnabled;
        await db.SaveChangesAsync(ct);
        return (await ListModelsAsync(ct)).First(m => m.Id == id);
    }

    public async Task<AgentConfigDto> GetConfigAsync(Guid employeeId, CancellationToken ct)
    {
        var config = await db.AgentConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.EmployeeId == employeeId, ct)
            ?? throw new NotFoundException("AgentConfiguration", employeeId);
        var granted = await db.AgentToolPermissions.AsNoTracking().Where(p => p.EmployeeId == employeeId).Select(p => p.ToolId).OrderBy(x => x).ToListAsync(ct);
        var monthStart = MonthStart(clock);
        var spent = await db.CostRecords.Where(c => c.EmployeeId == employeeId && c.Kind == CostKind.Actual && c.OccurredAt >= monthStart).SumAsync(c => c.AmountUsd, ct);
        return new AgentConfigDto(config.EmployeeId, config.Provider, config.ModelId, config.Effort, config.MaxTokensPerCall, config.MaxTurnsPerRun,
            config.MaxToolCallsPerRun, config.MaxTokensPerRun, config.TimeoutSeconds, config.MonthlyBudgetUsd, config.IsEnabled, granted, spent, config.Version);
    }

    public async Task<AgentConfigDto> UpdateConfigAsync(Guid employeeId, UpdateAgentConfigRequest req, CancellationToken ct)
    {
        var config = await db.AgentConfigurations.FirstOrDefaultAsync(c => c.EmployeeId == employeeId, ct)
            ?? throw new NotFoundException("AgentConfiguration", employeeId);
        if (req.Version is { } v) db.ExpectVersion(config, v);
        if (providers.Find(req.Provider) is null) throw new DomainException("validation", $"Unknown provider '{req.Provider}'.");
        if (!await db.AiModels.AnyAsync(m => m.Provider == req.Provider && m.ModelId == req.ModelId && m.IsEnabled, ct))
            throw new DomainException("validation", $"Model '{req.ModelId}' is not enabled for {req.Provider}.");
        if (!Enum.IsDefined(req.Effort)) throw new DomainException("validation", "effort is invalid.");
        Range(req.MaxTokensPerCall, 256, 128_000, "maxTokensPerCall");
        Range(req.MaxTurnsPerRun, 1, 100, "maxTurnsPerRun");
        Range(req.MaxToolCallsPerRun, 0, 500, "maxToolCallsPerRun");
        Range(req.MaxTokensPerRun, 1_000, 10_000_000, "maxTokensPerRun");
        Range(req.TimeoutSeconds, 10, 7_200, "timeoutSeconds");
        if (req.MonthlyBudgetUsd is < 0 or > 100_000) throw new DomainException("validation", "monthlyBudgetUsd must be between 0 and 100000.");

        config.Provider = req.Provider;
        config.ModelId = req.ModelId;
        config.Effort = req.Effort;
        config.MaxTokensPerCall = req.MaxTokensPerCall;
        config.MaxTurnsPerRun = req.MaxTurnsPerRun;
        config.MaxToolCallsPerRun = req.MaxToolCallsPerRun;
        config.MaxTokensPerRun = req.MaxTokensPerRun;
        config.TimeoutSeconds = req.TimeoutSeconds;
        config.MonthlyBudgetUsd = req.MonthlyBudgetUsd;
        config.IsEnabled = req.IsEnabled;
        await db.SaveChangesAsync(ct);
        return await GetConfigAsync(employeeId, ct);
    }

    public async Task<AgentConfigDto> SetToolsAsync(Guid employeeId, SetAgentToolsRequest req, Guid? grantedBy, CancellationToken ct)
    {
        if (!await db.AgentConfigurations.AnyAsync(c => c.EmployeeId == employeeId, ct)) throw new NotFoundException("AgentConfiguration", employeeId);
        var unknown = req.Tools.Where(t => tools.Find(t) is null).ToList();
        if (unknown.Count > 0) throw new DomainException("validation", $"Unknown tools: {string.Join(", ", unknown)}.");

        var existing = await db.AgentToolPermissions.Where(p => p.EmployeeId == employeeId).ToListAsync(ct);
        db.AgentToolPermissions.RemoveRange(existing.Where(p => !req.Tools.Contains(p.ToolId)));
        var now = clock.GetUtcNow();
        foreach (var id in req.Tools.Distinct().Where(id => existing.All(p => p.ToolId != id)))
            db.AgentToolPermissions.Add(new AgentToolPermission { EmployeeId = employeeId, ToolId = id, GrantedAt = now, GrantedBy = grantedBy });
        await db.SaveChangesAsync(ct);
        return await GetConfigAsync(employeeId, ct);
    }

    private static void Range(int value, int min, int max, string field)
    {
        if (value < min || value > max) throw new DomainException("validation", $"{field} must be between {min} and {max}.");
    }

    internal static DateTimeOffset MonthStart(TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }
}

/// <summary>Starting, cancelling and inspecting AI runs.</summary>
public class AgentRunService(IAppDbContext db, AiProviderRegistry providers, IAgentRunQueue queue, IRealtimeNotifier notifier, ICurrentUser current, TimeProvider clock, Reports.ReportsService reports)
{
    /// <summary>A person starts a work run on a task assigned to an AI employee.</summary>
    public async Task<AgentRunDto> StartAsync(Guid taskId, StartRunRequest req, CancellationToken ct)
    {
        var assignees = await db.TaskAssignments.AsNoTracking().Where(a => a.TaskId == taskId && a.UnassignedAt == null).Select(a => a.EmployeeId).ToListAsync(ct);
        var employeeId = req.EmployeeId ?? (assignees.Count == 1 ? assignees[0] : throw new DomainException("agent.not_assigned", "Assign the task to an AI employee first."));
        var run = await CreateRunAsync(taskId, employeeId, RunPurpose.Work, ct);
        return await GetRunAsync(run.Id, ct);
    }

    /// <summary>
    /// Validates and queues a run. Work and Plan runs need the employee to be the assignee of an Assigned/InProgress task;
    /// Review runs need the task in review and the employee to be its (different) reviewer.
    /// </summary>
    public async Task<AgentRun> CreateRunAsync(Guid taskId, Guid employeeId, RunPurpose purpose, CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct) ?? throw new NotFoundException("Task", taskId);
        var assignees = await db.TaskAssignments.AsNoTracking().Where(a => a.TaskId == taskId && a.UnassignedAt == null).Select(a => a.EmployeeId).ToListAsync(ct);
        if (purpose == RunPurpose.Review)
        {
            if (task.Status != WorkTaskStatus.InReview)
                throw new DomainException("agent.task_state", $"Only tasks in review can be reviewed; this one is {task.Status}.");
            if (task.ReviewerEmployeeId != employeeId) throw new DomainException("agent.not_reviewer", "This employee is not the task's reviewer.");
            if (assignees.Contains(employeeId)) throw new DomainException("agent.self_review", "An employee cannot review its own work.");
        }
        else
        {
            if (task.Status is not (WorkTaskStatus.Assigned or WorkTaskStatus.InProgress))
                throw new DomainException("agent.task_state", $"Only Assigned or InProgress tasks can be run; this one is {task.Status}.");
            if (!assignees.Contains(employeeId)) throw new DomainException("agent.not_assigned", "The task is not assigned to this employee.");
        }

        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw new NotFoundException("Employee", employeeId);
        if (employee.Kind != EmployeeKind.AI) throw new DomainException("agent.not_ai", $"{employee.DisplayName} is not an AI employee.");
        if (employee.Status == EmployeeStatus.Inactive) throw new DomainException("agent.inactive", $"{employee.DisplayName} is inactive.");

        var config = await db.AgentConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.EmployeeId == employeeId, ct)
            ?? throw new DomainException("agent.not_configured", $"{employee.DisplayName} has no AI configuration.");
        if (!config.IsEnabled) throw new DomainException("agent.disabled", $"{employee.DisplayName} is disabled.");
        var provider = providers.Find(config.Provider);
        if (provider is null || !provider.IsAvailable)
            throw new DomainException("agent.provider_unavailable", $"AI provider '{config.Provider}' is not configured. Add its API key first.");
        if (!await db.AiModels.AnyAsync(m => m.Provider == config.Provider && m.ModelId == config.ModelId && m.IsEnabled, ct))
            throw new DomainException("agent.model_unavailable", $"Model '{config.ModelId}' is not enabled or has no price.");

        var monthStart = AgentAdminService.MonthStart(clock);
        var spent = await db.CostRecords.Where(c => c.EmployeeId == employeeId && c.Kind == CostKind.Actual && c.OccurredAt >= monthStart).SumAsync(c => c.AmountUsd, ct);
        if (spent >= config.MonthlyBudgetUsd)
            throw new DomainException("agent.budget_exceeded", $"{employee.DisplayName} has used its monthly budget ({config.MonthlyBudgetUsd} USD).");
        if (await reports.BudgetBlockAsync(employeeId, task.ProjectId, ct) is { } blocked)
            throw new DomainException("agent.budget_exceeded", blocked);
        if (await db.AgentRuns.AnyAsync(r => r.TaskId == taskId && (r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running), ct))
            throw new DomainException("agent.already_running", "This task already has a run in progress.");

        var attempt = await db.AgentRuns.CountAsync(r => r.TaskId == taskId && r.Purpose == purpose, ct) + 1;
        var run = new AgentRun
        {
            TaskId = taskId, ProjectId = task.ProjectId, EmployeeId = employeeId, Purpose = purpose, Attempt = attempt, Provider = provider.Name,
            ModelId = config.ModelId, IsSimulated = provider.IsSimulated, TriggeredBy = current.UserId, CorrelationId = current.CorrelationId,
        };
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(run.Id, ct);
        await notifier.AgentRunChangedAsync(run.Id, run.TaskId, run.ProjectId, run.Status.ToString(), ct);
        return run;
    }

    public async Task<AgentRunDto> CancelAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.AgentRuns.FirstOrDefaultAsync(r => r.Id == runId, ct) ?? throw new NotFoundException("AgentRun", runId);
        if (!run.IsActive) return await GetRunAsync(runId, ct);
        run.CancelRequested = true;
        await db.SaveChangesAsync(ct);
        queue.Cancel(runId);
        await notifier.AgentRunChangedAsync(run.Id, run.TaskId, run.ProjectId, run.Status.ToString(), ct);
        return await GetRunAsync(runId, ct);
    }

    private IQueryable<AgentRunDto> RunQuery(IQueryable<AgentRun> runs) =>
        runs.Join(db.Tasks, r => r.TaskId, t => t.Id, (r, t) => new { r, t.Title })
            .Join(db.Employees, x => x.r.EmployeeId, e => e.Id, (x, e) => new AgentRunDto(
                x.r.Id, x.r.TaskId, x.Title, x.r.ProjectId, x.r.EmployeeId, e.DisplayName, x.r.Status, x.r.Provider, x.r.ModelId, x.r.IsSimulated,
                x.r.CreatedAt, x.r.StartedAt, x.r.FinishedAt, x.r.Turns, x.r.ToolCalls, x.r.InputTokens, x.r.OutputTokens, x.r.CostUsd,
                x.r.StopReason, x.r.Error, x.r.Purpose, x.r.Attempt));

    public async Task<AgentRunDto> GetRunAsync(Guid runId, CancellationToken ct) =>
        await RunQuery(db.AgentRuns.AsNoTracking().Where(r => r.Id == runId)).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("AgentRun", runId);

    public async Task<List<AgentRunDto>> ListRunsAsync(Guid? taskId, Guid? employeeId, Guid? projectId, int take, CancellationToken ct)
    {
        var q = db.AgentRuns.AsNoTracking();
        if (taskId is { } t) q = q.Where(r => r.TaskId == t);
        if (employeeId is { } e) q = q.Where(r => r.EmployeeId == e);
        if (projectId is { } p) q = q.Where(r => r.ProjectId == p);
        return await RunQuery(q.OrderByDescending(r => r.CreatedAt).Take(Math.Clamp(take, 1, 200))).ToListAsync(ct);
    }

    public async Task<AgentRunDetailDto> GetRunDetailAsync(Guid runId, CancellationToken ct)
    {
        var run = await GetRunAsync(runId, ct);
        var steps = await db.AgentRunSteps.AsNoTracking().Where(s => s.RunId == runId).OrderBy(s => s.Sequence)
            .Select(s => new RunStepDto(s.Sequence, s.Kind, s.Content, s.ToolCallId, s.CreatedAt)).ToListAsync(ct);
        var execs = await db.ToolExecutions.AsNoTracking().Where(x => x.RunId == runId).OrderBy(x => x.StartedAt)
            .Select(x => new ToolExecutionDto(x.Id, x.ToolId, x.ToolVersion, x.ToolCallId, x.Status, x.InputJson, x.Output, x.Error, x.DurationMs, x.StartedAt))
            .ToListAsync(ct);
        return new AgentRunDetailDto(run, steps, execs);
    }

    public async Task<List<DeliverableDto>> ListDeliverablesAsync(Guid taskId, CancellationToken ct) =>
        await db.Deliverables.AsNoTracking().Where(d => d.TaskId == taskId).OrderByDescending(d => d.CreatedAt)
            .Join(db.Employees, d => d.EmployeeId, e => e.Id, (d, e) => new DeliverableDto(d.Id, d.TaskId, d.EmployeeId, e.DisplayName, d.RunId,
                d.Title, d.Format, d.Content, d.Status, d.IsSimulated, d.CreatedAt))
            .ToListAsync(ct);

    public async Task<UsageSummaryDto> GetUsageSummaryAsync(CancellationToken ct)
    {
        var monthStart = AgentAdminService.MonthStart(clock);
        var costs = await db.CostRecords.AsNoTracking().Where(c => c.Kind == CostKind.Actual && c.OccurredAt >= monthStart)
            .Join(db.AgentRuns, c => c.RunId, r => r.Id, (c, r) => new { c.AmountUsd, c.EmployeeId, r.IsSimulated }).ToListAsync(ct);
        var usage = await db.UsageRecords.AsNoTracking().Where(u => u.OccurredAt >= monthStart)
            .Select(u => new { u.EmployeeId, Tokens = u.InputTokens + u.OutputTokens + u.CacheReadTokens + u.CacheWriteTokens, u.IsSimulated }).ToListAsync(ct);
        var runs = await db.AgentRuns.AsNoTracking().Where(r => r.CreatedAt >= monthStart).Select(r => new { r.EmployeeId, r.Status }).ToListAsync(ct);
        var budgets = await db.AgentConfigurations.AsNoTracking().ToDictionaryAsync(c => c.EmployeeId, c => c.MonthlyBudgetUsd, ct);
        var names = await db.Employees.AsNoTracking().Where(e => e.Kind == EmployeeKind.AI).ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);

        var byEmployee = runs.Select(r => r.EmployeeId).Distinct()
            .Select(id => new EmployeeUsageDto(id, names.GetValueOrDefault(id, "?"),
                costs.Where(c => c.EmployeeId == id).Sum(c => c.AmountUsd),
                usage.Where(u => u.EmployeeId == id).Sum(u => u.Tokens),
                runs.Count(r => r.EmployeeId == id),
                budgets.GetValueOrDefault(id)))
            .OrderByDescending(x => x.CostUsd).ThenByDescending(x => x.Runs).ToList();

        return new UsageSummaryDto(
            costs.Where(c => !c.IsSimulated).Sum(c => c.AmountUsd),
            costs.Where(c => c.IsSimulated).Sum(c => c.AmountUsd),
            usage.Where(u => !u.IsSimulated).Sum(u => u.Tokens),
            runs.Count,
            runs.Count(r => r.Status is AgentRunStatus.Queued or AgentRunStatus.Running),
            byEmployee);
    }
}
