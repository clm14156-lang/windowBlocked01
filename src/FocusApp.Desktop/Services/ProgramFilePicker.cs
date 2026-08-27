using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FocusApp.Desktop.Services;

public sealed record ProgramFileSelection(
    string ExePath,
    string ProcessName,
    string DisplayName);

public interface IProgramFilePicker
{
    ProgramFileSelection? PickProgram();
}

public sealed class ProgramFilePicker : IProgramFilePicker
{
    public ProgramFileSelection? PickProgram()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择程序",
            Filter = "应用程序 (*.exe)|*.exe",
            DefaultExt = ".exe",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var path = Path.GetFullPath(dialog.FileName);
        if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var processName = Path.GetFileName(path);
        return new ProgramFileSelection(path, processName, GetDisplayName(path));
    }

    private static string GetDisplayName(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return Path.GetFileNameWithoutExtension(path);
    }
}
