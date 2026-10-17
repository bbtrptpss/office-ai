using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Code;

namespace OfficeAI.Infrastructure.Code;

public class SandboxOptions
{
    /// <summary>Run containers through the local Docker CLI. Only the sandbox runner service (or a developer machine) should enable this.</summary>
    public bool UseLocalDocker { get; set; }

    /// <summary>When set, the API forwards runs to the sandbox runner service instead of touching Docker itself.</summary>
    public string? RunnerUrl { get; set; }
    public string? RunnerToken { get; set; }

    public string[] AllowedImages { get; set; } = ["mcr.microsoft.com/dotnet/sdk:10.0", "node:22-alpine", "alpine:3.20"];
    public int MaxOutputChars { get; set; } = 64_000;
    public int TestTimeoutSeconds { get; set; } = 600;

    /// <summary>How the workspace reaches the container: "bind" (host path) or "volume:&lt;name&gt;:&lt;root inside volume&gt;".</summary>
    public string WorkspaceMount { get; set; } = "bind";
}

/// <summary>
/// One throwaway container per run: no network, read-only root filesystem, all capabilities dropped,
/// no privilege escalation, non-root user, CPU/memory/process limits and a hard timeout.
/// The workspace is mounted read-only and copied into a size-limited tmpfs before the command runs,
/// so tests can build without being able to modify the real workspace.
/// </summary>
public sealed class DockerSandbox(IOptions<SandboxOptions> options, IOptions<RepositoryOptions> repoOptions, ILogger<DockerSandbox> log) : ISandbox
{
    public bool IsAvailable => options.Value.UseLocalDocker;
    public IReadOnlyCollection<string> AllowedImages => options.Value.AllowedImages;
    public TimeSpan TestTimeout => TimeSpan.FromSeconds(Math.Clamp(options.Value.TestTimeoutSeconds, 5, 600));

    public async Task<SandboxResult> RunAsync(SandboxRequest req, CancellationToken ct)
    {
        if (!IsAvailable) return new SandboxResult(null, "", false, 0, "The sandbox is not enabled on this server.");
        if (!AllowedImages.Contains(req.Image)) return new SandboxResult(null, "", false, 0, $"Image '{req.Image}' is not on the sandbox allow-list.");

        var name = $"officeai-sbx-{Guid.NewGuid():N}";
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        string[] args =
        [
            "run", "--rm", "--name", name,
            "--network", "none",
            "--read-only",
            "--tmpfs", "/tmp:rw,exec,size=1g",
            "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges",
            "--user", "1000:1000",
            "--cpus", req.Cpus.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--memory", $"{req.MemoryMb}m", "--memory-swap", $"{req.MemoryMb}m",
            "--pids-limit", "256",
            "-e", "HOME=/tmp", "-e", "DOTNET_CLI_HOME=/tmp", "-e", "NUGET_PACKAGES=/tmp/nuget", "-e", "DOTNET_NOLOGO=1",
            "-e", "npm_config_cache=/tmp/npm",
            .. Mount(req.WorkspacePath),
            "-w", "/tmp",
            req.Image,
            "sh", "-c", $"cp -r /src /tmp/work && cd /tmp/work && {req.Command}",
        ];
        foreach (var a in args) psi.ArgumentList.Add(a);

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(psi);
        if (process is null) return new SandboxResult(null, "", false, 0, "Could not start docker.");
        var stdout = new StringBuilder();
        var max = options.Value.MaxOutputChars;
        void Append(string? line)
        {
            if (line is null) return;
            lock (stdout) if (stdout.Length < max) stdout.AppendLine(line);
        }
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(req.Timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            await KillAsync(name);
            try { process.Kill(entireProcessTree: true); } catch { /* gone */ }
        }
        var output = stdout.ToString();
        if (output.Length >= max) output += "\n…[output truncated]";
        if (ct.IsCancellationRequested) return new SandboxResult(null, output, false, (int)sw.ElapsedMilliseconds, "Cancelled.");
        return new SandboxResult(timedOut ? null : process.ExitCode, output, timedOut, (int)sw.ElapsedMilliseconds, null);
    }

    private IEnumerable<string> Mount(string workspace)
    {
        var mode = options.Value.WorkspaceMount;
        if (mode.StartsWith("volume:", StringComparison.Ordinal))
        {
            // volume:<volume name>:<path of the repos root inside the volume>
            var parts = mode.Split(':', 3);
            var root = Path.GetFullPath(repoOptions.Value.Root);
            var sub = Path.GetRelativePath(root, workspace).Replace('\\', '/');
            var inVolume = parts.Length > 2 && parts[2].Length > 0 ? $"{parts[2].Trim('/')}/{sub}" : sub;
            return ["--mount", $"type=volume,src={parts[1]},dst=/src,readonly,volume-subpath={inVolume}"];
        }
        return ["--mount", $"type=bind,src={workspace},dst=/src,readonly"];
    }

    private async Task KillAsync(string name)
    {
        try
        {
            using var kill = Process.Start(new ProcessStartInfo("docker", ["kill", name]) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
            if (kill is not null) await kill.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not kill sandbox container {Name}", name);
        }
    }
}
