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

    /// <summary>The Docker network of a repository's setup step. Empty: setup commands are refused.</summary>
    public string? SetupNetwork { get; set; }

    /// <summary>The allow-list proxy on that network, handed to the setup step as HTTP(S)_PROXY.</summary>
    public string? SetupProxy { get; set; }

    /// <summary>How the workspace reaches the container: "bind" (host path) or "volume:&lt;name&gt;:&lt;root inside volume&gt;".</summary>
    public string WorkspaceMount { get; set; } = "bind";
}

/// <summary>
/// One throwaway container per run: no network, read-only root filesystem, all capabilities dropped,
/// no privilege escalation, non-root user, CPU/memory/process limits and a hard timeout.
/// The workspace is mounted read-only and copied into a size-limited tmpfs before the command runs,
/// so tests can build without being able to modify the real workspace.
///
/// A repository with a setup command runs in two containers sharing a throwaway tmpfs volume. The setup container can only reach
/// the allow-list proxy (see deploy/sandbox-proxy), to download dependencies. The command then runs in a second one with no network at all.
/// </summary>
public sealed class DockerSandbox(IOptions<SandboxOptions> options, IOptions<RepositoryOptions> repoOptions, ILogger<DockerSandbox> log) : ISandbox
{
    private const string WorkVolumeSize = "4g";
    private const int MaxGeneratedFileBytes = 8 * 1024 * 1024;

    /// <summary>Files a setup step may hand back: fixed names at the top of the project, because agents cannot run a package manager themselves.</summary>
    private static readonly string[] Lockfiles = ["package-lock.json", "packages.lock.json"];

    public bool IsAvailable => options.Value.UseLocalDocker;
    public IReadOnlyCollection<string> AllowedImages => options.Value.AllowedImages;
    public TimeSpan TestTimeout => TimeSpan.FromSeconds(Math.Clamp(options.Value.TestTimeoutSeconds, 5, 600));

    public async Task<SandboxResult> RunAsync(SandboxRequest req, CancellationToken ct)
    {
        if (!IsAvailable) return new SandboxResult(null, "", false, 0, "The sandbox is not enabled on this server.");
        if (!AllowedImages.Contains(req.Image)) return new SandboxResult(null, "", false, 0, $"Image '{req.Image}' is not on the sandbox allow-list.");
        return string.IsNullOrWhiteSpace(req.SetupCommand) ? await RunOneStepAsync(req, ct) : await RunWithSetupAsync(req, ct);
    }

    private async Task<SandboxResult> RunOneStepAsync(SandboxRequest req, CancellationToken ct)
    {
        string[] args =
        [
            .. Hardening(req), "--network", "none", "--tmpfs", "/tmp:rw,exec,size=1g",
            "-e", "HOME=/tmp", "-e", "DOTNET_CLI_HOME=/tmp", "-e", "NUGET_PACKAGES=/tmp/nuget", "-e", "DOTNET_NOLOGO=1", "-e", "npm_config_cache=/tmp/npm",
            .. Mount(req.WorkspacePath),
            "-w", "/tmp", req.Image, "sh", "-c", $"cp -r /src /tmp/work && cd /tmp/work && {req.Command}",
        ];
        var step = await RunContainerAsync(args, req.Timeout, ct);
        if (ct.IsCancellationRequested) return new SandboxResult(null, step.Output, false, step.Ms, "Cancelled.");
        return new SandboxResult(step.TimedOut ? null : step.ExitCode, step.Output, step.TimedOut, step.Ms, null);
    }

    private async Task<SandboxResult> RunWithSetupAsync(SandboxRequest req, CancellationToken ct)
    {
        var network = options.Value.SetupNetwork;
        if (string.IsNullOrWhiteSpace(network)) return new SandboxResult(null, "", false, 0, "This server has no network for setup commands (Sandbox:SetupNetwork).");

        var volume = $"officeai-sbx-work-{Guid.NewGuid():N}";
        var sw = Stopwatch.StartNew();
        var created = await DockerAsync("volume", "create", "--driver", "local", "--opt", "type=tmpfs", "--opt", "device=tmpfs",
            "--opt", $"o=size={WorkVolumeSize},uid=1000,gid=1000", volume);
        if (!created) return new SandboxResult(null, "", false, 0, "Could not create the sandbox work volume.");
        // A tmpfs volume is emptied when its last container stops, which would lose the dependencies between the two steps: this idle container holds it.
        var holder = $"officeai-sbx-hold-{Guid.NewGuid():N}";
        try
        {
            var holding = await DockerAsync(["run", "-d", "--rm", "--name", holder, .. Hardening(req), "--network", "none", "--mount", $"type=volume,src={volume},dst=/work,volume-nocopy",
                req.Image, "sleep", ((int)req.Timeout.TotalSeconds + 60).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            if (!holding) return new SandboxResult(null, "", false, 0, "Could not start the sandbox work volume.");

            string[] workEnv = ["-e", "HOME=/work/.home", "-e", "DOTNET_CLI_HOME=/work/.home", "-e", "NUGET_PACKAGES=/work/.nuget", "-e", "DOTNET_NOLOGO=1", "-e", "npm_config_cache=/work/.npm"];
            string[] workMount = ["--tmpfs", "/tmp:rw,exec,size=1g", "--mount", $"type=volume,src={volume},dst=/work,volume-nocopy", "-w", "/work"];

            List<string> proxy = [];
            if (options.Value.SetupProxy is { Length: > 0 } url)
                foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "npm_config_proxy", "npm_config_https_proxy" })
                    proxy.AddRange(["-e", $"{name}={url}"]);
            // An unreachable DNS server: names outside the network do not resolve here, so DNS is no way out either. The proxy resolves for itself.
            string[] dns = network == "none" ? [] : ["--dns", "127.0.0.1"];

            string[] setupArgs =
            [
                .. Hardening(req), "--network", network, .. dns, .. workEnv, .. proxy, .. workMount, .. Mount(req.WorkspacePath),
                req.Image, "sh", "-c", $"mkdir -p /work/.home && cp -r /src/. /work/ && {req.SetupCommand}",
            ];
            var setup = await RunContainerAsync(setupArgs, req.Timeout, ct);
            if (ct.IsCancellationRequested) return new SandboxResult(null, setup.Output, false, (int)sw.ElapsedMilliseconds, "Cancelled.");
            var setupOutput = $"--- setup: {req.SetupCommand}\n{setup.Output}";
            if (setup.TimedOut) return new SandboxResult(null, setupOutput, true, (int)sw.ElapsedMilliseconds, null);
            if (setup.ExitCode != 0) return new SandboxResult(setup.ExitCode, setupOutput + "\n--- setup failed: the command did not run", false, (int)sw.ElapsedMilliseconds, null);

            var generated = new Dictionary<string, string>();
            foreach (var file in Lockfiles)
                if (await ReadFromContainerAsync(holder, $"/work/{file}") is { } content) generated[file] = content;

            var left = req.Timeout - sw.Elapsed;
            if (left <= TimeSpan.Zero) return new SandboxResult(null, setupOutput, true, (int)sw.ElapsedMilliseconds, null, generated);
            string[] runArgs = [.. Hardening(req), "--network", "none", .. workEnv, .. workMount, req.Image, "sh", "-c", req.Command];
            var run = await RunContainerAsync(runArgs, left, ct);
            var output = $"{setupOutput}\n--- run: {req.Command}\n{run.Output}";
            if (ct.IsCancellationRequested) return new SandboxResult(null, output, false, (int)sw.ElapsedMilliseconds, "Cancelled.");
            return new SandboxResult(run.TimedOut ? null : run.ExitCode, output, run.TimedOut, (int)sw.ElapsedMilliseconds, null, generated);
        }
        finally
        {
            await DockerAsync("rm", "-f", holder);
            await DockerAsync("volume", "rm", "-f", volume);
        }
    }

    private static string[] Hardening(SandboxRequest req) =>
    [
        "--read-only",
        "--cap-drop", "ALL",
        "--security-opt", "no-new-privileges",
        "--user", "1000:1000",
        "--cpus", req.Cpus.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--memory", $"{req.MemoryMb}m", "--memory-swap", $"{req.MemoryMb}m",
        "--pids-limit", "256",
    ];

    private sealed record StepResult(int? ExitCode, string Output, bool TimedOut, int Ms);

    private async Task<StepResult> RunContainerAsync(IReadOnlyList<string> containerArgs, TimeSpan limit, CancellationToken ct)
    {
        var name = $"officeai-sbx-{Guid.NewGuid():N}";
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "run", "--rm", "--name", name }.Concat(containerArgs)) psi.ArgumentList.Add(a);

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(psi);
        if (process is null) return new StepResult(null, "Could not start docker.", false, 0);
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
        timeout.CancelAfter(limit);
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
        return new StepResult(timedOut || ct.IsCancellationRequested ? null : process.ExitCode, output, timedOut, (int)sw.ElapsedMilliseconds);
    }

    /// <summary>A regular file's text, or null when it is missing, not a regular file or too big.</summary>
    private async Task<string?> ReadFromContainerAsync(string container, string path)
    {
        try
        {
            var psi = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
            foreach (var a in new[] { "exec", container, "sh", "-c", $"test -f {path} && ! test -L {path} && test $(wc -c < {path}) -le {MaxGeneratedFileBytes} && cat {path}" }) psi.ArgumentList.Add(a);
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await errors;
            return process.ExitCode == 0 ? await output : null;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not read {Path} from the sandbox", path);
            return null;
        }
    }

    private async Task<bool> DockerAsync(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var process = Process.Start(psi);
            if (process is null) return false;
            var drainOut = process.StandardOutput.ReadToEndAsync();
            var drainErr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(drainOut, drainErr);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "docker {Command} failed", args[0]);
            return false;
        }
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
