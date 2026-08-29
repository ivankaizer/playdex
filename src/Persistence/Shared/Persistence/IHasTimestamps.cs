namespace Shared.Persistence;

public interface IHasTimestamps
{
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
