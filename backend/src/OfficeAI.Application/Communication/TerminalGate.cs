using System.Collections.Concurrent;

namespace OfficeAI.Application.Communication;

/// <summary>
/// Step-up check for the dashboard terminal: after the CEO confirms an authenticator code, the terminal stays open for
/// <see cref="IdleTimeout"/> since the last command. Bound to the user's security stamp, so changing the password or
/// turning two-factor sign-in off closes it at once. Held in memory: a restart closes every terminal.
/// </summary>
public sealed class TerminalGate(TimeProvider clock)
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Guid, (string Stamp, DateTimeOffset Until)> _open = new();

    public DateTimeOffset Unlock(Guid userId, string stamp)
    {
        var until = clock.GetUtcNow() + IdleTimeout;
        _open[userId] = (stamp, until);
        return until;
    }

    /// <summary>When the terminal is open for this user, how long for; null when it is closed.</summary>
    public DateTimeOffset? OpenUntil(Guid userId, string stamp)
    {
        if (_open.TryGetValue(userId, out var o) && o.Stamp == stamp && o.Until > clock.GetUtcNow()) return o.Until;
        _open.TryRemove(userId, out _);
        return null;
    }

    /// <summary>Checks the terminal is open and restarts its idle timer; false when it is closed.</summary>
    public bool Use(Guid userId, string stamp)
    {
        if (OpenUntil(userId, stamp) is null) return false;
        _open[userId] = (stamp, clock.GetUtcNow() + IdleTimeout);
        return true;
    }

    public void Lock(Guid userId) => _open.TryRemove(userId, out _);
}
