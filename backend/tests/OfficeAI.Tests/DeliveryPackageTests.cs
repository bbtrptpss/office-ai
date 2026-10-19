using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Code;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class DeliveryPackageTests(ApiFixture fx)
{
    /// <summary>A project with a release in the given state, its test run, a final completion report and a draft manual.</summary>
    private async Task<(HttpClient Ceo, ProjectDto Project, Guid ReleaseId, Guid ReportId, Guid ManualId)> SetupAsync(ReleaseStatus releaseStatus, TestRunStatus testStatus)
    {
        var ceo = await fx.CeoAsync();
        var project = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"DP-{Guid.NewGuid():N}"[..14], name = "Delivery project", status = "Active" });
        var repo = await ceo.PostAsync<RepositoryDto>($"/api/projects/{project.Id}/repositories", new { name = "app", sandboxImage = "alpine:3.20", testCommand = "true" });
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = new TestRun { RepositoryId = repo.Id, Branch = "main", CommitSha = new string('a', 40), Image = "alpine:3.20", Command = "dotnet test", Status = testStatus, StartedAt = DateTimeOffset.UtcNow };
        db.TestRuns.Add(run);
        var release = new Release
        {
            ProjectId = project.Id, Tag = "1.0.0", Notes = "First version", DeploymentPlan = "Deploy the image", RollbackPlan = "Redeploy 0.9",
            Status = releaseStatus, CommitSha = run.CommitSha, TestRunId = run.Id,
        };
        db.Releases.Add(release);
        var report = new ProjectDocument { ProjectId = project.Id, Kind = DocumentKind.CompletionReport, Title = "Completion report", Revision = 1, Content = "All done.", Status = DocumentStatus.Final };
        var manual = new ProjectDocument { ProjectId = project.Id, Kind = DocumentKind.Other, Title = "User manual", Revision = 1, Content = "How to book." };
        db.Documents.AddRange(report, manual);
        await db.SaveChangesAsync();
        return (ceo, project, release.Id, report.Id, manual.Id);
    }

    [Fact]
    public async Task Package_is_delivered_only_when_complete_and_accepted_by_the_customer()
    {
        var (ceo, project, releaseId, reportId, manualId) = await SetupAsync(ReleaseStatus.Deployed, TestRunStatus.Passed);

        var pkg = await ceo.PostAsync<DeliveryPackageDto>($"/api/projects/{project.Id}/delivery-packages", new { releaseId, documentIds = new[] { manualId } });
        Assert.Equal("PKG-001", pkg.Code);
        Assert.Equal("Delivery of 1.0.0", pkg.Title);
        Assert.False(pkg.Ready);
        Assert.False(pkg.Checks.Single(c => c.Code == "completionReportIncluded").Met);
        Assert.False(pkg.Checks.Single(c => c.Code == "documentsFinal").Met);
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PostAsJsonAsync($"/api/delivery-packages/{pkg.Id}/delivered", new { })).StatusCode);
        // Recording the answer comes after delivery.
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PostAsJsonAsync($"/api/delivery-packages/{pkg.Id}/response", new { accepted = true })).StatusCode);

        pkg = await ceo.PutAsync<DeliveryPackageDto>($"/api/delivery-packages/{pkg.Id}", new
        {
            releaseId, documentIds = new[] { reportId, manualId }, handoverNotes = "Admin login is sent separately.", version = pkg.Version,
        });
        Assert.True(pkg.Ready);
        Assert.Equal(TestRunStatus.Passed, pkg.TestRunStatus);
        Assert.Equal(2, pkg.Documents.Count);

        var export = await ceo.GetAsync($"/api/delivery-packages/{pkg.Id}/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var md = await export.Content.ReadAsStringAsync();
        Assert.Contains("Delivery of 1.0.0", md);
        Assert.Contains("Admin login is sent separately.", md);
        Assert.Contains("Redeploy 0.9", md);
        Assert.Contains("How to book.", md);
        Assert.Contains("Passed", md);

        pkg = await ceo.PostAsync<DeliveryPackageDto>($"/api/delivery-packages/{pkg.Id}/delivered", new { note = "Emailed to the owner" });
        Assert.Equal(DeliveryPackageStatus.Delivered, pkg.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PutAsJsonAsync($"/api/delivery-packages/{pkg.Id}", new { releaseId })).StatusCode);

        var stages = await ceo.GetAsync<DeliveryStagesDto>($"/api/projects/{project.Id}/stages");
        Assert.True(stages.Stages[7].Exit.Single(c => c.Code == "packageDelivered").Met);
        Assert.False(stages.Stages[7].Exit.Single(c => c.Code == "customerAccepted").Met);

        pkg = await ceo.PostAsync<DeliveryPackageDto>($"/api/delivery-packages/{pkg.Id}/response", new { accepted = true, feedback = "Looks good" });
        Assert.Equal(DeliveryPackageStatus.Accepted, pkg.Status);
        Assert.Null(pkg.ChangeTaskId);
        stages = await ceo.GetAsync<DeliveryStagesDto>($"/api/projects/{project.Id}/stages");
        Assert.True(stages.Stages[7].Exit.Single(c => c.Code == "customerAccepted").Met);
    }

    [Fact]
    public async Task Change_request_becomes_a_task_and_needs_feedback()
    {
        var (ceo, project, releaseId, reportId, _) = await SetupAsync(ReleaseStatus.Deployed, TestRunStatus.Passed);
        var pkg = await ceo.PostAsync<DeliveryPackageDto>($"/api/projects/{project.Id}/delivery-packages", new { releaseId, documentIds = new[] { reportId }, handoverNotes = "Hand over" });
        await ceo.PostAsync<DeliveryPackageDto>($"/api/delivery-packages/{pkg.Id}/delivered", new { });

        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync($"/api/delivery-packages/{pkg.Id}/response", new { accepted = false })).StatusCode);
        pkg = await ceo.PostAsync<DeliveryPackageDto>($"/api/delivery-packages/{pkg.Id}/response", new { accepted = false, feedback = "Add SMS reminders" });
        Assert.Equal(DeliveryPackageStatus.ChangesRequested, pkg.Status);
        var task = await ceo.GetAsync<TaskDto>($"/api/tasks/{pkg.ChangeTaskId}");
        Assert.Contains("PKG-001", task.Title);
        Assert.Contains("Add SMS reminders", task.Description);
    }

    [Fact]
    public async Task Package_needs_a_deployed_release_with_passing_tests()
    {
        var (ceo, project, releaseId, reportId, _) = await SetupAsync(ReleaseStatus.Approved, TestRunStatus.Failed);
        var pkg = await ceo.PostAsync<DeliveryPackageDto>($"/api/projects/{project.Id}/delivery-packages", new { releaseId, documentIds = new[] { reportId }, handoverNotes = "Hand over" });
        Assert.False(pkg.Checks.Single(c => c.Code == "releaseDeployed").Met);
        Assert.False(pkg.Checks.Single(c => c.Code == "testEvidence").Met);
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PostAsJsonAsync($"/api/delivery-packages/{pkg.Id}/delivered", new { })).StatusCode);

        // Documents and releases of other projects are refused.
        var (_, other, _, otherDoc, _) = await SetupAsync(ReleaseStatus.Deployed, TestRunStatus.Passed);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync($"/api/projects/{project.Id}/delivery-packages", new { releaseId, documentIds = new[] { otherDoc } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync($"/api/projects/{other.Id}/delivery-packages", new { releaseId })).StatusCode);
    }
}
