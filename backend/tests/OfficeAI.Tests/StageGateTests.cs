using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class StageGateTests(ApiFixture fx)
{
    [Fact]
    public async Task Stages_advance_only_as_recorded_work_meets_each_gate()
    {
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"SG-{Guid.NewGuid():N}"[..14], name = "Gates", status = "Active" });

        var s = await ceo.GetAsync<DeliveryStagesDto>($"/api/projects/{project.Id}/stages");
        Assert.Equal(8, s.Stages.Count);
        Assert.Equal(1, s.CurrentStage);
        Assert.Equal("current", s.Stages[0].Status);
        Assert.Contains(s.Stages[0].Exit, c => c.Code == "hasDescription" && !c.Met);
        Assert.All(s.Stages.Skip(1), x => Assert.Equal("upcoming", x.Status));

        // Intake details, plus reviewed requirements: stages 1 and 2 are done.
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.Projects.FirstAsync(x => x.Id == project.Id);
            p.Description = "Booking system";
            p.EstimatedBudget = 100_000m;
            p.DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2));
            db.Requirements.AddRange(
                new ProjectRequirement { ProjectId = p.Id, Code = "FR-1", Kind = RequirementKind.Functional, Title = "Book a room", AcceptanceCriteria = "Given…", Status = ReviewState.Approved },
                new ProjectRequirement { ProjectId = p.Id, Code = "RK-1", Kind = RequirementKind.Risk, Title = "Calendar sync", Status = ReviewState.Approved });
            await db.SaveChangesAsync();
        }
        s = await ceo.GetAsync<DeliveryStagesDto>($"/api/projects/{project.Id}/stages");
        Assert.Equal(3, s.CurrentStage);
        Assert.Equal(["done", "done", "current"], s.Stages.Take(3).Select(x => x.Status));
        Assert.False(s.Stages[2].Approval.Met); // estimate not approved yet

        // An approved estimate without warnings and the proposal documents finish stage 3.
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = await db.RoleDefinitions.FirstAsync();
            var e = new ProjectEstimate
            {
                ProjectId = project.Id, Revision = 1, Status = EstimateStatus.Approved, Assumptions = "fixed scope", ContingencyPercent = 10m, TargetMarginPercent = 30m, UsdToThb = 36m,
                Items = [new EstimateLineItem { Description = "API", RoleDefinitionId = role.Id, RoleTitle = "Dev", Hours = 10m, RateThb = 1000m, Evidence = "past project" }],
            };
            EstimateCalculator.Compute(e);
            db.Estimates.Add(e);
            db.Documents.AddRange(
                new ProjectDocument { ProjectId = project.Id, Kind = DocumentKind.Proposal, Title = "Proposal", Content = "…", Revision = 1 },
                new ProjectDocument { ProjectId = project.Id, Kind = DocumentKind.StatementOfWork, Title = "SOW", Content = "…", Revision = 1 });
            await db.SaveChangesAsync();
            Assert.Empty(e.Warnings);
        }
        s = await ceo.GetAsync<DeliveryStagesDto>($"/api/projects/{project.Id}/stages");
        Assert.Equal(4, s.CurrentStage);
        Assert.Contains(s.Stages[3].Exit, c => c.Code == "decisionAccepted" && !c.Met);
        // Development may not start before architecture is accepted.
        Assert.Contains(s.Stages[4].Entry, c => c.Code == "decisionAccepted" && !c.Met);
    }
}
