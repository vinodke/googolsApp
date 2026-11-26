namespace Googols.Domain.Common;

/// <summary>Base for every persisted entity: GUID identity plus UTC audit timestamps.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

/// <summary>Entities updated concurrently (progress, XP totals) carry a SQL Server rowversion.</summary>
public interface IConcurrencyTracked
{
    byte[] RowVersion { get; set; }
}

/// <summary>Entities whose rows must not be physically removed (curriculum, content) are archived instead.</summary>
public interface ISoftDeletable
{
    DateTime? ArchivedAtUtc { get; set; }
}

/// <summary>Rows owned by a school are filtered to the caller's school by a global query filter.</summary>
public interface ISchoolScoped
{
    Guid? SchoolId { get; }
}
