using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Workflows;

public sealed record AgentChoice(Guid EmployeeId, string DisplayName, string Reason);

/// <summary>
/// Picks an AI employee for a task: must be able to run (enabled, provider configured, budget left), then
/// best skill match, then lightest workload, then name for a stable tie-break. Executives are not assigned work.
/// </summary>
public sealed class AgentSelector(IAppDbContext db, AiProviderRegistry providers, TimeProvider clock)
{
    private static readonly string[] ReviewSkills = ["code-review", "testing", "quality-gates", "security-review", "test-automation"];

    private sealed record Candidate(Guid Id, string Name, OrgLevel Level, Guid DepartmentId, string DeptCode, List<string> Skills, int OpenTasks);

    private async Task<List<Candidate>> CandidatesAsync(CancellationToken ct)
    {
        var monthStart = AgentAdminService.MonthStart(clock);
        var rows = await db.Employees.AsNoTracking()
            .Where(e => e.Kind == EmployeeKind.AI && e.Status != EmployeeStatus.Inactive)
            .Join(db.AgentConfigurations.Where(c => c.IsEnabled), e => e.Id, c => c.EmployeeId, (e, c) => new
            {
                e.Id, e.DisplayName, e.RoleDefinition!.Level, e.DepartmentId, DeptCode = e.Department!.Code, e.RoleDefinition.Skills, c.Provider, c.MonthlyBudgetUsd,
                Spent = db.CostRecords.Where(x => x.EmployeeId == e.Id && x.Kind == CostKind.Actual && x.OccurredAt >= monthStart).Sum(x => (decimal?)x.AmountUsd) ?? 0,
                Open = db.TaskAssignments.Count(a => a.EmployeeId == e.Id && a.UnassignedAt == null &&
                    db.Tasks.Any(t => t.Id == a.TaskId && t.Status != WorkTaskStatus.Done && t.Status != WorkTaskStatus.Cancelled)),
            })
            .ToListAsync(ct);
        return rows
            .Where(r => providers.Find(r.Provider) is { IsAvailable: true } && r.Spent < r.MonthlyBudgetUsd)
            .Select(r => new Candidate(r.Id, r.DisplayName, r.Level, r.DepartmentId, r.DeptCode, r.Skills, r.Open))
            .ToList();
    }

    private static int SkillScore(Candidate c, IReadOnlyCollection<string> wanted) =>
        wanted.Count(w => c.Skills.Any(s => s.Equals(w, StringComparison.OrdinalIgnoreCase) || s.Contains(w, StringComparison.OrdinalIgnoreCase) || w.Contains(s, StringComparison.OrdinalIgnoreCase)));

    public async Task<AgentChoice?> ChooseWorkerAsync(WorkTask task, CancellationToken ct)
    {
        var wanted = task.RequiredSkills.Count > 0
            ? task.RequiredSkills
            : $"{task.Title} {task.Description}".ToLowerInvariant().Split([' ', ',', '.', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).Distinct().ToList();

        var best = (await CandidatesAsync(ct))
            .Where(c => c.Level != OrgLevel.Executive)
            .Select(c => new { c, score = SkillScore(c, wanted) })
            .OrderByDescending(x => x.score).ThenBy(x => x.c.OpenTasks).ThenBy(x => x.c.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        return best is null ? null
            : new AgentChoice(best.c.Id, best.c.Name, $"skills matched {best.score}/{Math.Max(1, wanted.Count)}, {best.c.OpenTasks} open tasks");
    }

    /// <summary>A reviewer is never the assignee; QA specialists first, then a senior colleague from the same department.</summary>
    public async Task<AgentChoice?> ChooseReviewerAsync(WorkTask task, IReadOnlyCollection<Guid> assignees, CancellationToken ct)
    {
        var candidates = (await CandidatesAsync(ct)).Where(c => !assignees.Contains(c.Id) && c.Level != OrgLevel.Executive).ToList();
        var assigneeDept = await db.Employees.AsNoTracking().Where(e => assignees.Contains(e.Id)).Select(e => (Guid?)e.DepartmentId).FirstOrDefaultAsync(ct);

        var best = candidates
            .Select(c => new
            {
                c,
                score = SkillScore(c, ReviewSkills) * 10 + SkillScore(c, task.RequiredSkills) * 3 +
                        (c.DepartmentId == assigneeDept && c.Level is OrgLevel.Senior or OrgLevel.Head ? 5 : 0),
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.c.OpenTasks).ThenBy(x => x.c.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        return best is null ? null : new AgentChoice(best.c.Id, best.c.Name, $"review score {best.score}");
    }
}
