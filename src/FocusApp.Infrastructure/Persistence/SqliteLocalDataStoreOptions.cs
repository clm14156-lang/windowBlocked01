namespace FocusApp.Infrastructure.Persistence;

public sealed record SqliteLocalDataStoreOptions
{
    public TimeSpan BusyTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaximumBusyRetries { get; init; } = 3;

    public TimeSpan BusyRetryDelay { get; init; } = TimeSpan.FromMilliseconds(75);
}
