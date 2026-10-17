using System.Net;
using System.Net.Http.Json;
using OfficeAI.Application.Organization;
using OfficeAI.Domain.Organization;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class OrganizationTests(ApiFixture fx)
{
    [Fact]
    public async Task Seed_creates_six_departments_and_the_positions_from_the_master_prompt()
    {
        var ceo = await fx.CeoAsync();
        var depts = await ceo.GetAsync<List<DepartmentDto>>("/api/departments");
        var roles = await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions");
        var employees = await ceo.GetAsync<List<EmployeeDto>>("/api/employees");

        foreach (var code in new[] { "EXEC", "SALES", "PMO", "ENG", "QA", "OPS" })
            Assert.Contains(depts, d => d.Code == code);
        Assert.True(roles.Count(r => r.Code is not null) >= 31);
        Assert.Single(roles, r => r.Code == "CEO" && r.RequiresHuman);
        Assert.Contains(employees, e => e.JobTitle == "CEO" && e.Kind == EmployeeKind.Human);
        Assert.True(employees.Count(e => e.Kind == EmployeeKind.AI) >= 30);
    }

    [Fact]
    public async Task Org_chart_has_the_CEO_at_the_root()
    {
        var ceo = await fx.CeoAsync();
        var chart = await ceo.GetAsync<List<OrgChartNode>>("/api/org-chart");
        var root = Assert.Single(chart, n => n.JobTitle == "CEO");
        Assert.Contains(root.Reports, r => r.JobTitle == "CTO");
        var cto = root.Reports.Single(r => r.JobTitle == "CTO");
        Assert.Contains(cto.Reports, r => r.JobTitle == "Software Architect Agent");
    }

    [Fact]
    public async Task CEO_can_create_update_and_deactivate_department_position_and_AI_employee()
    {
        var ceo = await fx.CeoAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var dept = await ceo.PostAsync<DepartmentDto>("/api/departments", new { code = $"RND{suffix}", name = "วิจัยและพัฒนา", sortOrder = 9 });
        dept = await ceo.PutAsync<DepartmentDto>($"/api/departments/{dept.Id}",
            new { code = dept.Code, name = "ฝ่ายวิจัยและพัฒนา", sortOrder = 9, isActive = true, version = dept.Version });
        Assert.Equal("ฝ่ายวิจัยและพัฒนา", dept.Name);

        var role = await ceo.PostAsync<RoleDefinitionDto>("/api/role-definitions",
            new { code = $"RESEARCHER{suffix}", title = "Research Agent", departmentId = dept.Id, level = "Staff", skills = new[] { "research" } });

        var emp = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = "Research AI", kind = "AI", departmentId = dept.Id, roleDefinitionId = role.Id });
        Assert.Equal(EmployeeStatus.Available, emp.Status);

        emp = await ceo.PutAsync<EmployeeDto>($"/api/employees/{emp.Id}",
            new { displayName = emp.DisplayName, kind = "AI", departmentId = dept.Id, roleDefinitionId = role.Id, status = "Inactive", version = emp.Version });
        Assert.Equal(EmployeeStatus.Inactive, emp.Status);
    }

    [Fact]
    public async Task Manager_loop_is_rejected()
    {
        var ceo = await fx.CeoAsync();
        var employees = await ceo.GetAsync<List<EmployeeDto>>("/api/employees");
        var cto = employees.Single(e => e.DisplayName == "CTO AI");
        var architect = employees.Single(e => e.DisplayName == "Software Architect AI");
        Assert.Equal(cto.Id, architect.ManagerId);

        // Make the CTO report to the architect, who reports to the CTO.
        var res = await ceo.PutAsJsonAsync($"/api/employees/{cto.Id}",
            new { displayName = cto.DisplayName, kind = "AI", departmentId = cto.DepartmentId, roleDefinitionId = cto.RoleDefinitionId, managerId = architect.Id });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task AI_cannot_hold_a_human_only_position()
    {
        var ceo = await fx.CeoAsync();
        var roles = await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions");
        var ceoRole = roles.Single(r => r.Code == "CEO");
        var res = await ceo.PostAsJsonAsync("/api/employees",
            new { displayName = "AI CEO", kind = "AI", departmentId = ceoRole.DepartmentId, roleDefinitionId = ceoRole.Id });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Duplicate_department_code_returns_409()
    {
        var ceo = await fx.CeoAsync();
        var res = await ceo.PostAsJsonAsync("/api/departments", new { code = "ENG", name = "Duplicate" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }
}
