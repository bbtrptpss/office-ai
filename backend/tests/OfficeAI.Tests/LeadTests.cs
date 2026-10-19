using System.Net;
using System.Net.Http.Json;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Knowledge;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Security;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class LeadTests(ApiFixture fx)
{
    private static string Code() => $"LD-{Guid.NewGuid():N}"[..14];

    [Fact]
    public async Task Lead_lists_missing_information_asks_for_it_and_becomes_a_project_with_a_brief()
    {
        var ceo = await fx.CeoAsync();
        var bad = await ceo.PostAsJsonAsync("/api/leads", new { title = "x", customerName = "", need = "y" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var lead = await ceo.PostAsync<LeadDto>("/api/leads", new { title = "Clinic booking", customerName = "Smile Clinic", need = "Booking app", contactEmail = "owner@smile.example" });
        Assert.StartsWith("LEAD-", lead.Code);
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.False(lead.Ready);
        Assert.True(lead.Checks.Single(c => c.Code == "contact").Met);
        Assert.False(lead.Checks.Single(c => c.Code == "needDetail").Met);

        // Not everything is known yet.
        var early = await ceo.PostAsJsonAsync($"/api/leads/{lead.Id}/qualify", new { });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Contains("budget", await early.Content.ReadAsStringAsync());

        // One question per missing item; asking again adds nothing new.
        lead = await ceo.PostAsync<LeadDto>($"/api/leads/{lead.Id}/questions/suggest", new { language = "th" });
        Assert.Equal(LeadStatus.Clarifying, lead.Status);
        var topics = lead.Questions.Select(q => q.Topic).ToList();
        Assert.Equal(["needDetail", "businessGoal", "targetUsers", "budget", "desiredDueDate", "platforms", "constraints"], topics);
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PostAsJsonAsync($"/api/leads/{lead.Id}/questions/suggest", new { })).StatusCode);

        // The customer answers; the person fills in the lead.
        foreach (var q in lead.Questions)
            lead = await ceo.PostAsync<LeadDto>($"/api/leads/{lead.Id}/questions/{q.Id}/answer", new { answer = $"Answer to {q.Topic}" });
        lead = await ceo.PutAsync<LeadDto>($"/api/leads/{lead.Id}", new
        {
            title = "Clinic booking", customerName = "Smile Clinic", contactEmail = "owner@smile.example",
            need = "Patients book, move and cancel dental appointments online; staff see the day's schedule.",
            businessGoal = "Halve phone bookings", targetUsers = "Patients and 6 front-desk staff", budget = 350000m, desiredDueDate = "2027-03-31",
            version = lead.Version,
        });
        Assert.True(lead.Ready);
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PostAsJsonAsync($"/api/leads/{lead.Id}/convert", new { code = Code() })).StatusCode);

        lead = await ceo.PostAsync<LeadDto>($"/api/leads/{lead.Id}/qualify", new { });
        Assert.Equal(LeadStatus.Qualified, lead.Status);

        var code = Code();
        lead = await ceo.PostAsync<LeadDto>($"/api/leads/{lead.Id}/convert", new { code });
        Assert.Equal(LeadStatus.Converted, lead.Status);
        Assert.Equal(code, lead.ProjectCode);

        var project = await ceo.GetAsync<ProjectDto>($"/api/projects/{lead.ProjectId}");
        Assert.Equal("Clinic booking", project.Name);
        Assert.Equal(350000m, project.EstimatedBudget);
        Assert.Equal(new DateOnly(2027, 3, 31), project.DueDate);
        var docs = await ceo.GetAsync<List<DocumentDto>>($"/api/projects/{project.Id}/documents");
        Assert.Contains(docs, d => d.Title.Contains(lead.Code));

        // Intake is met; a follow-up question reopens it until answered.
        var stages = await ceo.GetAsync<DeliveryStagesDto>($"/api/projects/{project.Id}/stages");
        Assert.True(stages.Stages[0].Exit.Single(c => c.Code == "clarificationsAnswered").Met);
        await ceo.PostAsync<LeadDto>($"/api/leads/{lead.Id}/questions", new { question = "Do you need SMS reminders?" });
        stages = await ceo.GetAsync<DeliveryStagesDto>($"/api/projects/{project.Id}/stages");
        Assert.False(stages.Stages[0].Exit.Single(c => c.Code == "clarificationsAnswered").Met);

        // Converted leads are read-only and code reuse is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await ceo.PostAsJsonAsync($"/api/leads/{lead.Id}/convert", new { code = Code() })).StatusCode);
    }

    [Fact]
    public async Task Lost_lead_needs_a_reason_and_is_closed()
    {
        var ceo = await fx.CeoAsync();
        var lead = await ceo.PostAsync<LeadDto>("/api/leads", new { title = "Website", customerName = "Cafe", need = "A website" });
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync($"/api/leads/{lead.Id}/lost", new { })).StatusCode);
        lead = await ceo.PostAsync<LeadDto>($"/api/leads/{lead.Id}/lost", new { reason = "Chose another vendor" });
        Assert.Equal(LeadStatus.Lost, lead.Status);
        var edit = await ceo.PutAsJsonAsync($"/api/leads/{lead.Id}", new { title = "Website", customerName = "Cafe", need = "A website" });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    [Fact]
    public async Task Leads_are_hidden_from_people_limited_to_their_own_projects()
    {
        var (member, _) = await fx.PersonAsync([SystemRoles.Member]);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/leads")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/leads", new { title = "a", customerName = "b", need = "c" })).StatusCode);
    }
}
