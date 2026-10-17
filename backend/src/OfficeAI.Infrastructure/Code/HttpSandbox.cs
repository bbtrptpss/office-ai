using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Code;

namespace OfficeAI.Infrastructure.Code;

/// <summary>Wire format between the API and the sandbox runner. Paths are relative to the shared repositories root.</summary>
public sealed record RunnerRequest(string Image, string Workspace, string Command, int TimeoutSeconds, double Cpus, int MemoryMb);

/// <summary>The API's view of the sandbox when it runs in a container: it asks the runner service, which alone can reach Docker.</summary>
public sealed class HttpSandbox(HttpClient http, IOptions<SandboxOptions> options, IOptions<RepositoryOptions> repos) : ISandbox
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.RunnerUrl);
    public IReadOnlyCollection<string> AllowedImages => options.Value.AllowedImages;
    public TimeSpan TestTimeout => TimeSpan.FromSeconds(Math.Clamp(options.Value.TestTimeoutSeconds, 5, 600));

    public async Task<SandboxResult> RunAsync(SandboxRequest req, CancellationToken ct)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(repos.Value.Root), Path.GetFullPath(req.WorkspacePath)).Replace('\\', '/');
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.RunnerUrl!), "/run"))
        {
            Content = JsonContent.Create(new RunnerRequest(req.Image, relative, req.Command, (int)req.Timeout.TotalSeconds, req.Cpus, req.MemoryMb)),
        };
        message.Headers.Add("X-Runner-Token", options.Value.RunnerToken ?? "");
        try
        {
            using var response = await http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
                return new SandboxResult(null, "", false, 0, $"Sandbox runner answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
            return await response.Content.ReadFromJsonAsync<SandboxResult>(ct) ?? new SandboxResult(null, "", false, 0, "Empty runner response.");
        }
        catch (HttpRequestException ex)
        {
            return new SandboxResult(null, "", false, 0, $"Sandbox runner unreachable: {ex.Message}");
        }
    }
}
