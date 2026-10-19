using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Common;

namespace OfficeAI.Application.Communication;

/// <summary>
/// The dashboard terminal: the CEO types a PowerShell command and it runs in the project folder on the host, through
/// scripts/windows/claude-bridge.mjs, with no restrictions. The endpoint allows the CEO role only; every command is audited.
/// </summary>
public class TerminalService(IAppDbContext db, ICurrentUser current, IClaudeBridge bridge, TimeProvider clock)
{
    public const int MaxCommand = 4000;

    public async Task<TerminalResultDto> RunAsync(TerminalRequest req, CancellationToken ct)
    {
        var command = (req.Command ?? "").Trim();
        if (command.Length == 0) throw new DomainException("validation", "command is required.");
        if (command.Length > MaxCommand) throw new DomainException("validation", $"command must be at most {MaxCommand} characters.");
        if (!bridge.IsConfigured) throw new DomainException("claude.not_configured", "ยังไม่ได้ตั้งค่าตัวเชื่อม (CLAUDE_BRIDGE_TOKEN ใน .env)");

        // Audited before it runs, so a command that takes the machine down is still on record.
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = clock.GetUtcNow(), ActorUserId = current.UserId, ActorName = current.Name, Action = "TerminalCommand",
            EntityType = "Terminal", NewValues = System.Text.Json.JsonSerializer.Serialize(new { command }), CorrelationId = current.CorrelationId,
        });
        await db.SaveChangesAsync(ct);
        try
        {
            return await bridge.RunAsync(command, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new DomainException("claude.unavailable", "ตัวเชื่อมไม่ตอบ — เปิดระบบด้วยไอคอน Office AI หรือรัน node scripts/windows/claude-bridge.mjs");
        }
    }
}
