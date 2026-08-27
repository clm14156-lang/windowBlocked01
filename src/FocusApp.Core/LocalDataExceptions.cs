namespace FocusApp.Core;

public class LocalDataException : Exception
{
    public LocalDataException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class LocalDataMigrationException : LocalDataException
{
    public LocalDataMigrationException(string message, string? backupPath, Exception? innerException = null)
        : base(message, innerException)
    {
        BackupPath = backupPath;
    }

    public string? BackupPath { get; }
}

public sealed class LocalDataCorruptedException : LocalDataException
{
    public LocalDataCorruptedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class LocalDataBusyException : LocalDataException
{
    public LocalDataBusyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
