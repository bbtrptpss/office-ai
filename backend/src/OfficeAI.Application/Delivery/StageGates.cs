using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Delivery;

/// <summary>One check; Code is translated by the UI, Detail carries counts such as "3/5".</summary>
public record StageCheckDto(string Code, bool Met, string? Detail = null);
public record StageDto(int Number, string Code, string Status, List<StageCheckDto> Entry, List<StageCheckDto> Exit, List<string> Deliverables, StageCheckDto Approval);
public record DeliveryStagesDto(Guid ProjectId, int CurrentStage, List<StageDto> Stages);

/// <summary>
/// The eight delivery stages of the Master Prompt (§7) as explicit gates, evaluated from recorded data only. A stage is
/// done when its exit criteria and approval are met; the current stage is the first one that is not. Nothing here changes data.
/// </summary>
public class StageGateService(IAppDbContext db)
{
    public async Task<DeliveryStagesDto> GetAsync(Guid projectId, CancellationToken ct)
    {
        var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == projectId, ct) ?? throw new NotFoundException("Project", projectId);
        var reqs = await db.Requirements.AsNoTracking().Where(r => r.ProjectId == projectId).Select(r => new { r.Kind, r.Status, r.AcceptanceCriteria }).ToListAsync(ct);
        var estimate = await db.Estimates.AsNoTracking().Where(e => e.ProjectId == projectId && e.Status == EstimateStatus.Approved)
            .OrderByDescending(e => e.Revision).Select(e => new { e.Warnings }).FirstOrDefaultAsync(ct);
        var docs = await db.Documents.AsNoTracking().Where(d => d.ProjectId == projectId).Select(d => new { d.Kind, d.Status }).ToListAsync(ct);
        var decisions = await db.ArchitectureDecisions.AsNoTracking().Where(d => d.ProjectId == projectId).Select(d => d.Status).ToListAsync(ct);
        var milestones = await db.Milestones.CountAsync(m => m.ProjectId == projectId, ct);
        var tasks = await db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId && t.Status != WorkTaskStatus.Cancelled).Select(t => new { t.Id, t.Status }).ToListAsync(ct);
        var repoIds = await db.CodeRepositories.Where(r => r.ProjectId == projectId).Select(r => r.Id).ToListAsync(ct);
        var mrs = await db.MergeRequests.AsNoTracking().Where(m => m.ProjectId == projectId).Select(m => m.Status).ToListAsync(ct);
        // Latest test run per repository decides whether the build is green.
        var latestTests = await db.TestRuns.AsNoTracking().Where(t => repoIds.Contains(t.RepositoryId))
            .GroupBy(t => t.RepositoryId).Select(g => g.OrderByDescending(t => t.StartedAt).Select(t => t.Status).First()).ToListAsync(ct);
        var taskIds = tasks.Select(t => t.Id).ToList();
        var pendingDeliverables = await db.Deliverables.CountAsync(d => taskIds.Contains(d.TaskId) && d.Status == DeliverableStatus.Submitted, ct);
        // Newest first, so the detail names the latest approved release.
        var releases = await db.Releases.AsNoTracking().Where(r => r.ProjectId == projectId).OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Tag, r.Status, HasNotes = r.Notes != null && r.Notes != "", Prepared = r.DeploymentPlan != null && r.DeploymentPlan != "" && r.RollbackPlan != null && r.RollbackPlan != "" }).ToListAsync(ct);
        var openDefects = await db.Defects.CountAsync(d => d.ProjectId == projectId && d.Status == DefectStatus.Open, ct);
        // Follow-up questions to the customer stay part of intake after the lead became this project.
        var lead = await db.Leads.AsNoTracking().Where(l => l.ProjectId == projectId).Select(l => new { l.Code, Open = l.Questions.Count(q => q.AnsweredAt == null) }).FirstOrDefaultAsync(ct);
        var packages = await db.DeliveryPackages.AsNoTracking().Where(x => x.ProjectId == projectId).Select(x => x.Status).ToListAsync(ct);
        var lessons = await db.KnowledgeEntries.CountAsync(k => k.ProjectId == projectId && k.Kind == KnowledgeKind.Lesson && k.Status == KnowledgeStatus.Validated, ct);

        bool Doc(DocumentKind k) => docs.Any(d => d.Kind == k);
        bool Final(DocumentKind k) => docs.Any(d => d.Kind == k && d.Status == DocumentStatus.Final);
        static StageCheckDto Count(string code, int done, int total) => new(code, total > 0 && done == total, $"{done}/{total}");

        var live = reqs.Where(r => r.Status != ReviewState.Rejected).ToList();
        var functional = live.Where(r => r.Kind == RequirementKind.Functional).ToList();
        var requirementsApproved = Count("requirementsApproved", live.Count(r => r.Status == ReviewState.Approved), live.Count);
        var estimateApproved = new StageCheckDto("estimateApproved", estimate is not null);
        var decisionAccepted = new StageCheckDto("decisionAccepted", decisions.Contains(DecisionStatus.Accepted), $"{decisions.Count(s => s == DecisionStatus.Accepted)}");
        var tasksDone = Count("tasksDone", tasks.Count(t => t.Status == WorkTaskStatus.Done), tasks.Count);
        var openMrs = mrs.Count(s => s is Domain.Code.MergeRequestStatus.Open or Domain.Code.MergeRequestStatus.Conflict);
        var merged = mrs.Count(s => s == Domain.Code.MergeRequestStatus.Merged);
        var mergesApproved = new StageCheckDto("mergesApproved", merged > 0 && openMrs == 0, $"{merged} / {openMrs}");
        var releasePrepared = releases.Any(r => r.Status != ReleaseStatus.Cancelled && r.Prepared);
        var releaseApproved = new StageCheckDto("releaseApproved", releases.Any(r => r.Status is ReleaseStatus.Approved or ReleaseStatus.Deployed),
            releases.FirstOrDefault(r => r.Status is ReleaseStatus.Approved or ReleaseStatus.Deployed)?.Tag);
        var completionFinal = new StageCheckDto("completionReportFinal", Final(DocumentKind.CompletionReport));

        var defs = new (string Code, List<StageCheckDto> Entry, List<StageCheckDto> Exit, List<string> Deliverables, StageCheckDto Approval)[]
        {
            ("intake", [new("projectCreated", true)],
                [new("hasDescription", !string.IsNullOrWhiteSpace(p.Description)), new("hasBudget", p.EstimatedBudget is not null), new("hasDueDate", p.DueDate is not null),
                    new("clarificationsAnswered", lead is not { Open: > 0 }, lead is null ? null : $"{lead.Code}: {lead.Open}")],
                ["projectBrief"], new("noApprovalNeeded", true)),
            ("discovery", [],
                [new("hasRequirements", live.Count > 0, $"{live.Count}"),
                    Count("acceptanceCriteria", functional.Count(r => !string.IsNullOrWhiteSpace(r.AcceptanceCriteria)), functional.Count),
                    new("risksRecorded", live.Any(r => r.Kind is RequirementKind.Risk or RequirementKind.Assumption))],
                ["requirements", "userStories"], requirementsApproved),
            ("proposal", [new("requirementsApproved", requirementsApproved.Met)],
                [estimateApproved, new("estimateEvidence", estimate is { Warnings.Count: 0 }, estimate is null ? null : $"{estimate.Warnings.Count}"),
                    new("proposalDoc", Doc(DocumentKind.Proposal)), new("sowDoc", Doc(DocumentKind.StatementOfWork))],
                ["estimate", "proposal", "statementOfWork"], estimateApproved),
            ("architecture", [estimateApproved],
                [decisionAccepted, new("specDoc", Doc(DocumentKind.Specification)), new("milestonesPlanned", milestones > 0, $"{milestones}"),
                    new("tasksPlanned", tasks.Count > 0, $"{tasks.Count}")],
                ["architectureDecisions", "specification", "plan"], decisionAccepted),
            ("development", [estimateApproved, decisionAccepted],
                [new("repositoryLinked", repoIds.Count > 0), tasksDone, new("noOpenMergeRequests", openMrs == 0, $"{openMrs}")],
                ["code", "mergeRequests"], mergesApproved),
            ("qa", [],
                [new("testsPassing", latestTests.Count > 0 && latestTests.All(s => s == TestRunStatus.Passed), $"{latestTests.Count(s => s == TestRunStatus.Passed)}/{latestTests.Count}"),
                    new("noFailedTasks", tasks.All(t => t.Status != WorkTaskStatus.Failed)), new("deliverablesReviewed", pendingDeliverables == 0, $"{pendingDeliverables}"),
                    new("noOpenDefects", openDefects == 0, $"{openDefects}")],
                ["testReports", "defectLog"], tasksDone),
            ("release", [],
                [new("releaseNotesDoc", Doc(DocumentKind.ReleaseNotes) || releases.Any(r => r.Status != ReleaseStatus.Cancelled && r.HasNotes)),
                    new("releasePlans", releasePrepared), new("noBlockedTasks", tasks.All(t => t.Status is not (WorkTaskStatus.Blocked or WorkTaskStatus.Failed)))],
                ["releaseNotes", "deploymentPlan", "rollbackPlan"], releaseApproved),
            ("delivery", [],
                [new("completionReportDoc", Doc(DocumentKind.CompletionReport)),
                    new("packageDelivered", packages.Any(s => s is DeliveryPackageStatus.Delivered or DeliveryPackageStatus.Accepted), $"{packages.Count}"),
                    new("customerAccepted", packages.Contains(DeliveryPackageStatus.Accepted)),
                    new("lessonValidated", lessons > 0, $"{lessons}"), new("projectCompleted", p.Status == ProjectStatus.Completed)],
                ["deliveryPackage", "completionReport", "lessonsLearned"], completionFinal),
        };

        var stages = new List<StageDto>();
        var current = 0;
        for (var i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            // Every stage after the first also needs the one before it to be finished.
            var entry = i == 0 ? d.Entry : [new("previousStageDone", stages[i - 1].Status == "done"), .. d.Entry];
            var done = d.Exit.All(c => c.Met) && d.Approval.Met && entry.All(c => c.Met);
            if (!done && current == 0) current = i + 1;
            stages.Add(new StageDto(i + 1, d.Code, done ? "done" : current == i + 1 ? "current" : "upcoming", entry, d.Exit, d.Deliverables, d.Approval));
        }
        return new DeliveryStagesDto(projectId, current == 0 ? defs.Length : current, stages);
    }
}
