using System.Security.Cryptography;
using System.Text;
using OfficeAI.Application.Code;
using OfficeAI.Infrastructure.Code;

// The only process that talks to Docker. It accepts run requests from the API (shared token), checks the image
// against the allow-list and the workspace against the repositories root, then runs the locked-down container.
var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<SandboxOptions>(builder.Configuration.GetSection("Sandbox"));
builder.Services.Configure<RepositoryOptions>(builder.Configuration.GetSection("Repositories"));
builder.Services.AddSingleton<DockerSandbox>();

OfficeAI.Infrastructure.Security.StrongSecrets.Enforce(builder.Configuration);

var app = builder.Build();
var token = app.Configuration["Sandbox:RunnerToken"];
if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Sandbox:RunnerToken must be configured.");
var expected = Encoding.UTF8.GetBytes(token);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/run", async (HttpRequest http, RunnerRequest req, DockerSandbox sandbox, IConfiguration config, CancellationToken ct) =>
{
    var given = Encoding.UTF8.GetBytes(http.Headers["X-Runner-Token"].ToString());
    if (!CryptographicOperations.FixedTimeEquals(given, expected)) return Results.Unauthorized();

    var root = Path.GetFullPath(config["Repositories:Root"] ?? "/data/repos");
    var workspace = Path.GetFullPath(Path.Combine(root, req.Workspace));
    if (!workspace.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !Directory.Exists(workspace))
        return Results.BadRequest("Workspace is outside the repositories root or does not exist.");
    if (req.TimeoutSeconds is < 1 or > 3600 || req.Cpus is <= 0 or > 4 || req.MemoryMb is < 64 or > 8192)
        return Results.BadRequest("Limits are out of range.");

    var result = await sandbox.RunAsync(new SandboxRequest(req.Image, workspace, req.Command, TimeSpan.FromSeconds(req.TimeoutSeconds), req.Cpus, req.MemoryMb, req.SetupCommand), ct);
    return Results.Ok(result);
});

app.Run();
