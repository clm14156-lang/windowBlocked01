using System.IO;
using Microsoft.Data.Sqlite;

namespace FocusApp.Desktop.Services;

public enum IconCacheType
{
    Website,
    Application
}

public enum IconCacheStatus
{
    Success,
    Failed
}

public sealed record IconCacheEntry(
    string CacheKey,
    IconCacheType Type,
    string FilePath,
    string Source,
    IconCacheStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAccessAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? SourceLastWriteTime,
    long? SourceFileSize,
    DateTimeOffset? RetryAfter);

public sealed class IconCacheRepository
{
    private readonly string _databasePath;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;

    public IconCacheRepository(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath => _databasePath;

    public async Task<IconCacheEntry?> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CacheKey, Type, FilePath, Source, Status, CreatedAt, LastAccessAt,
                   ExpiresAt, SourceLastWriteTime, SourceFileSize, RetryAfter
            FROM IconCache WHERE CacheKey = $cacheKey;
            """;
        command.Parameters.AddWithValue("$cacheKey", cacheKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new IconCacheEntry(
            reader.GetString(0),
            Enum.Parse<IconCacheType>(reader.GetString(1), true),
            reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<IconCacheStatus>(reader.GetString(4), true),
            ParseDate(reader.GetString(5)),
            ParseDate(reader.GetString(6)),
            ParseNullableDate(reader, 7),
            ParseNullableDate(reader, 8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            ParseNullableDate(reader, 10));
    }

    public async Task SaveAsync(IconCacheEntry entry, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO IconCache
                (CacheKey, Type, FilePath, Source, Status, CreatedAt, LastAccessAt,
                 ExpiresAt, SourceLastWriteTime, SourceFileSize, RetryAfter)
            VALUES
                ($cacheKey, $type, $filePath, $source, $status, $createdAt, $lastAccessAt,
                 $expiresAt, $sourceLastWriteTime, $sourceFileSize, $retryAfter)
            ON CONFLICT(CacheKey) DO UPDATE SET
                Type = excluded.Type, FilePath = excluded.FilePath, Source = excluded.Source,
                Status = excluded.Status, CreatedAt = excluded.CreatedAt,
                LastAccessAt = excluded.LastAccessAt, ExpiresAt = excluded.ExpiresAt,
                SourceLastWriteTime = excluded.SourceLastWriteTime,
                SourceFileSize = excluded.SourceFileSize, RetryAfter = excluded.RetryAfter;
            """;
        command.Parameters.AddWithValue("$cacheKey", entry.CacheKey);
        command.Parameters.AddWithValue("$type", entry.Type.ToString());
        command.Parameters.AddWithValue("$filePath", entry.FilePath);
        command.Parameters.AddWithValue("$source", entry.Source);
        command.Parameters.AddWithValue("$status", entry.Status.ToString());
        command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$lastAccessAt", entry.LastAccessAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$expiresAt", (object?)entry.ExpiresAt?.UtcDateTime.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceLastWriteTime", (object?)entry.SourceLastWriteTime?.UtcDateTime.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceFileSize", (object?)entry.SourceFileSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$retryAfter", (object?)entry.RetryAfter?.UtcDateTime.ToString("O") ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task TouchAsync(string cacheKey, DateTimeOffset accessedAt, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE IconCache SET LastAccessAt = $lastAccessAt WHERE CacheKey = $cacheKey;";
        command.Parameters.AddWithValue("$lastAccessAt", accessedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$cacheKey", cacheKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS IconCache (
                    CacheKey TEXT NOT NULL PRIMARY KEY,
                    Type TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    Source TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    LastAccessAt TEXT NOT NULL,
                    ExpiresAt TEXT NULL,
                    SourceLastWriteTime TEXT NULL,
                    SourceFileSize INTEGER NULL,
                    RetryAfter TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_IconCache_LastAccessAt ON IconCache (LastAccessAt);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static DateTimeOffset ParseDate(string value)
        => DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? ParseNullableDate(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));
}
