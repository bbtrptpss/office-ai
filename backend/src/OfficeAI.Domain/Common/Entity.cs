namespace OfficeAI.Domain.Common;

/// <summary>Base for every persisted business entity.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; set; }
}

/// <summary>Marker: changes to this entity are written to the audit log.</summary>
public interface IAuditable;
