using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Code;
using OfficeAI.Infrastructure.Code;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Application.Workflows;
using OfficeAI.Infrastructure.Ai;
using OfficeAI.Infrastructure.Persistence;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// With DataProtection:CertificatePath set, new cookie keys are encrypted with that certificate before they reach the database.
    /// DataProtection:PreviousCertificatePaths keeps keys from a rotated-out certificate readable. Keys written before encryption
    /// was turned on stay readable until they expire (90 days).
    /// </summary>
    public static void ProtectKeysAtRest(IDataProtectionBuilder builder, IConfiguration config)
    {
        var path = config["DataProtection:CertificatePath"];
        if (string.IsNullOrWhiteSpace(path)) return;
        var password = config["DataProtection:CertificatePassword"];
        var current = LoadCertificate(path, password);
        var previous = (config.GetSection("DataProtection:PreviousCertificatePaths").Get<string[]>() ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => LoadCertificate(p, password));
        builder.ProtectKeysWithCertificate(current);
        builder.UnprotectKeysWithAnyCertificate([current, .. previous]);
    }

    private static X509Certificate2 LoadCertificate(string path, string? password)
    {
        if (!File.Exists(path)) throw new InvalidOperationException($"DataProtection certificate '{path}' was not found.");
        var cert = X509CertificateLoader.LoadPkcs12FromFile(path, password);
        if (!cert.HasPrivateKey) throw new InvalidOperationException($"DataProtection certificate '{path}' has no private key.");
        return cert;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        // Auth cookie keys survive restarts and are shared by every API instance.
        var dataProtection = services.AddDataProtection().PersistKeysToDbContext<AppDbContext>().SetApplicationName("OfficeAI");
        ProtectKeysAtRest(dataProtection, config);
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.Configure<SeedOptions>(config.GetSection("Seed"));
        services.AddScoped<Seeder>();

        services.AddScoped<OrganizationService>();
        services.AddScoped<WorkService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<SecurityAdminService>();
        services.AddScoped<SecurityAuditWriter>();

        // AI providers: keys come from configuration/environment only, never from the database.
        services.Configure<AnthropicOptions>(config.GetSection("AiProviders:Anthropic"));
        services.Configure<SimulatedOptions>(config.GetSection("AiProviders:Simulated"));
        services.AddSingleton<IAiProvider, AnthropicProvider>();
        services.AddSingleton<IAiProvider, SimulatedProvider>();
        services.Configure<OpenAiOptions>(config.GetSection("AiProviders:OpenAI"));
        services.Configure<GeminiOptions>(config.GetSection("AiProviders:Gemini"));
        services.AddHttpClient(OpenAiProvider.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan); // per-call timeout in the adapter
        services.AddHttpClient(GeminiProvider.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<IAiCredentials, AiCredentialStore>();
        services.AddHttpClient(AnthropicProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<IAiProvider, OpenAiProvider>();
        services.AddSingleton<IAiProvider, GeminiProvider>();
        services.AddSingleton<AiProviderRegistry>();

        services.AddSingleton<IAgentTool, TaskContextTool>();
        services.AddSingleton<IAgentTool, ProjectTasksTool>();
        services.AddSingleton<IAgentTool, FindEmployeesTool>();
        services.AddSingleton<IAgentTool, SubmitDeliverableTool>();
        services.AddSingleton<IAgentTool, RequestReviewTool>();
        services.AddSingleton<IAgentTool, ReportBlockedTool>();
        services.AddSingleton<IAgentTool, PlanSubmitTool>();
        services.AddSingleton<IAgentTool, ReviewSubmitTool>();
        services.AddSingleton<IAgentTool, DelegateTaskTool>();
        services.AddSingleton<IAgentTool, ReadRequirementsTool>();
        services.AddSingleton<IAgentTool, AddRequirementTool>();
        services.AddSingleton<IAgentTool, ProposeDecisionTool>();
        services.AddSingleton<IAgentTool, SubmitEstimateTool>();

        // Code: local Git repositories and the test sandbox.
        services.Configure<RepositoryOptions>(config.GetSection("Repositories"));
        services.Configure<SandboxOptions>(config.GetSection("Sandbox"));
        services.AddSingleton<GitStore>();
        services.AddSingleton<ICodeRepositoryStore>(sp => sp.GetRequiredService<GitStore>());
        services.AddSingleton<IRepositoryLayout>(sp => sp.GetRequiredService<GitStore>());
        // In containers the API asks the sandbox runner; only a developer machine (or tests) drives Docker directly.
        if (!string.IsNullOrWhiteSpace(config["Sandbox:RunnerUrl"]))
            services.AddHttpClient<ISandbox, HttpSandbox>(c => c.Timeout = TimeSpan.FromMinutes(15));
        else
            services.AddSingleton<ISandbox, DockerSandbox>();
        services.AddSingleton<CodeWorkspace>();
        services.AddSingleton<IAgentTool, ListFilesTool>();
        services.AddSingleton<IAgentTool, ReadFileTool>();
        services.AddSingleton<IAgentTool, SearchCodeTool>();
        services.AddSingleton<IAgentTool, WriteFileTool>();
        services.AddSingleton<IAgentTool, DeleteFileTool>();
        services.AddSingleton<IAgentTool, DiffTool>();
        services.AddSingleton<IAgentTool, CommitTool>();
        services.AddSingleton<IAgentTool, RunTestsTool>();
        services.AddSingleton<IAgentTool, OpenMergeRequestTool>();
        services.AddSingleton<IAgentTool, OfficeAI.Application.Knowledge.SearchKnowledgeTool>();
        services.AddSingleton<IAgentTool, OfficeAI.Application.Knowledge.ProposeKnowledgeTool>();
        services.AddSingleton<IAgentTool, OfficeAI.Application.Knowledge.WriteDocumentTool>();
        services.AddSingleton<IAgentTool, OfficeAI.Application.Communication.ReadChatTool>();
        services.AddSingleton<IAgentTool, OfficeAI.Application.Communication.PostChatTool>();
        services.AddSingleton<IAgentTool, OfficeAI.Application.Communication.ListMeetingsTool>();
        services.AddSingleton<IAgentTool, OfficeAI.Application.Communication.DraftMinutesTool>();
        services.AddSingleton<IAgentTool, OfficeAI.Application.Communication.DraftAnnouncementTool>();
        services.AddSingleton<ToolRegistry>();

        services.AddScoped<AgentAdminService>();
        services.AddScoped<AgentRunService>();
        services.AddScoped<AgentExecutor>();

        services.AddScoped<AgentSelector>();
        services.AddScoped<ApprovalService>();
        services.AddScoped<ObjectiveService>();
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<DeliveryService>();
        services.AddScoped<CodeService>();
        services.AddScoped<OfficeAI.Application.Reports.ReportsService>();
        services.AddScoped<OfficeAI.Application.Knowledge.KnowledgeService>();
        services.AddScoped<OfficeAI.Application.Communication.ChatService>();
        services.AddScoped<OfficeAI.Application.Communication.AnnouncementService>();
        services.AddScoped<OfficeAI.Application.Communication.MeetingService>();
        services.AddScoped<OfficeAI.Application.Communication.NotificationService>();
        services.AddScoped<OfficeAI.Application.Communication.ChatReplyService>();
        services.AddScoped<OfficeAI.Application.Communication.ChatCommandService>();
        services.AddScoped<OfficeAI.Application.Communication.AssistantService>();
        services.Configure<OfficeAI.Application.Integrations.IntegrationSettings>(config.GetSection("Integrations"));
        services.AddHttpClient("integrations", c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<OfficeAI.Application.Integrations.IntegrationService>();
        services.AddScoped<OfficeAI.Application.Integrations.IntegrationDispatcher>();
        return services;
    }
}
