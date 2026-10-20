using System.Collections.Concurrent;
using OfficeAI.Application.Abstractions;

namespace OfficeAI.Tests;

/// <summary>Records what would have been mailed. Starts configured; tests can switch it off or make sends fail.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public bool Configured { get; set; } = true;
    public Exception? FailWith { get; set; }
    public ConcurrentQueue<OutgoingEmail> Sent { get; } = new();

    public bool IsConfigured => Configured;
    public string? FromAddress => Configured ? "office@test.local" : null;

    public Task SendAsync(OutgoingEmail email, CancellationToken ct)
    {
        if (FailWith is { } ex) throw ex;
        Sent.Enqueue(email);
        return Task.CompletedTask;
    }

    public List<OutgoingEmail> To(string address) => Sent.Where(e => e.To == address).ToList();
}
