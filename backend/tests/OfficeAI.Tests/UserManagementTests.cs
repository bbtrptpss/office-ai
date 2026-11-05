using System.Net;
using System.Net.Http.Json;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class UserManagementTests(ApiFixture fx)
{
    private const string First = "First-Password-1";
    private const string Second = "Second-Password-2";

    private async Task<(HttpClient Ceo, UserDto User, string Email)> NewUserAsync(params string[] roles)
    {
        var ceo = await fx.CeoAsync();
        var email = $"um-{Guid.NewGuid():N}@test.local";
        var user = await ceo.PostAsync<UserDto>("/api/security/users", new { email, displayName = "Managed", password = First, isAgent = false, roles });
        return (ceo, user, email);
    }

    private async Task<HttpStatusCode> LoginStatusAsync(string email, string password) =>
        (await fx.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode;

    [Fact]
    public async Task A_lost_password_is_replaced_by_a_manager_and_the_old_one_stops_working()
    {
        var (ceo, user, email) = await NewUserAsync(SystemRoles.Member);
        var weak = await ceo.PostAsJsonAsync($"/api/security/users/{user.Id}/password", new { newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, await LoginStatusAsync(email, First)); // a refused password leaves the old one in place

        (await ceo.PostAsJsonAsync($"/api/security/users/{user.Id}/password", new { newPassword = Second })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(email, First));
        Assert.Equal(HttpStatusCode.NoContent, await LoginStatusAsync(email, Second));

        var audit = await ceo.GetAsync<List<AuditLogDto>>($"/api/audit?entityType=UserPasswordReset&entityId={user.Id}");
        var entry = Assert.Single(audit);
        Assert.DoesNotContain(Second, (entry.OldValues ?? "") + (entry.NewValues ?? ""));
    }

    [Fact]
    public async Task A_reset_also_clears_a_lockout()
    {
        var (ceo, user, email) = await NewUserAsync(SystemRoles.Member);
        for (var i = 0; i < 5; i++) await LoginStatusAsync(email, "wrong-password-1");
        Assert.Equal((HttpStatusCode)423, await LoginStatusAsync(email, First));
        (await ceo.PostAsJsonAsync($"/api/security/users/{user.Id}/password", new { newPassword = Second })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, await LoginStatusAsync(email, Second));
    }

    [Fact]
    public async Task A_turned_off_person_loses_access_at_once_and_can_be_restored()
    {
        var (ceo, user, email) = await NewUserAsync(SystemRoles.Member);
        var session = await fx.LoginAsync(email, First);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/projects")).StatusCode);

        var off = await ceo.PutAsync<UserDto>($"/api/security/users/{user.Id}/active", new { isActive = false });
        Assert.False(off.IsActive);
        Assert.Equal(HttpStatusCode.Forbidden, (await session.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(email, First));

        Assert.True((await ceo.PutAsync<UserDto>($"/api/security/users/{user.Id}/active", new { isActive = true })).IsActive);
        Assert.Equal(HttpStatusCode.NoContent, await LoginStatusAsync(email, First));
    }

    [Fact]
    public async Task Nobody_can_lock_the_company_out_or_take_over_a_CEO()
    {
        var (ceo, member, memberEmail) = await NewUserAsync(SystemRoles.Member);
        var ceoUser = (await ceo.GetAsync<List<UserDto>>("/api/security/users")).First(u => u.Email == ApiFixture.CeoEmail);

        // Not your own account.
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync($"/api/security/users/{ceoUser.Id}/active", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync($"/api/security/users/{ceoUser.Id}/password", new { newPassword = Second })).StatusCode);

        // Without security.manage: nothing.
        var plain = await fx.LoginAsync(memberEmail, First);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsJsonAsync($"/api/security/users/{ceoUser.Id}/password", new { newPassword = Second })).StatusCode);

        // A role that has security.manage but is not CEO still cannot touch a CEO, yet can manage others.
        var roles = await ceo.GetAsync<List<RoleDto>>("/api/security/roles");
        var auditor = roles.First(r => r.Name == SystemRoles.Auditor);
        (await ceo.PutAsJsonAsync($"/api/security/roles/{auditor.Id}/permissions", new { permissions = auditor.Permissions.Append(Permissions.SecurityManage).ToList() })).EnsureSuccessStatusCode();
        try
        {
            var admin = await fx.UserWithRolesAsync(SystemRoles.Auditor);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/security/users/{ceoUser.Id}/password", new { newPassword = Second })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/security/users/{ceoUser.Id}/active", new { isActive = false })).StatusCode);
            (await admin.PostAsJsonAsync($"/api/security/users/{member.Id}/password", new { newPassword = Second })).EnsureSuccessStatusCode();
        }
        finally
        {
            (await ceo.PutAsJsonAsync($"/api/security/roles/{auditor.Id}/permissions", new { permissions = auditor.Permissions })).EnsureSuccessStatusCode();
        }

        // AI identities have no password to reset.
        var agent = await ceo.PostAsync<UserDto>("/api/security/users", new { email = $"ai-{Guid.NewGuid():N}@test.local", displayName = "Agent", isAgent = true, roles = new[] { SystemRoles.AiAgent } });
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync($"/api/security/users/{agent.Id}/password", new { newPassword = Second })).StatusCode);
    }

    [Fact]
    public async Task Two_CEOs_can_turn_each_other_off_but_the_one_left_cannot_turn_themselves_off()
    {
        var (ceo, second, secondEmail) = await NewUserAsync(SystemRoles.Ceo);
        var secondSession = await fx.LoginAsync(secondEmail, First);
        var first = (await ceo.GetAsync<List<UserDto>>("/api/security/users")).First(u => u.Email == ApiFixture.CeoEmail);

        Assert.False((await ceo.PutAsync<UserDto>($"/api/security/users/{second.Id}/active", new { isActive = false })).IsActive);
        // The one turned off can no longer act, so they cannot turn the remaining CEO off in return.
        Assert.Equal(HttpStatusCode.Forbidden, (await secondSession.PutAsJsonAsync($"/api/security/users/{first.Id}/active", new { isActive = false })).StatusCode);
        // The one left cannot remove themselves.
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync($"/api/security/users/{first.Id}/active", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ceo.GetAsync("/api/projects")).StatusCode);
    }
}
