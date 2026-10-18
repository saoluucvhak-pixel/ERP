namespace Hak.Domain.Common;

public static class Ids
{
    /// <summary>Time-ordered GUID (v7) so primary-key indexes stay append-mostly.</summary>
    public static Guid New() => Guid.CreateVersion7();
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
