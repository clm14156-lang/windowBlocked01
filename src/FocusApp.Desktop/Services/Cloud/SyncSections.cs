namespace FocusApp.Desktop.Services.Cloud;

[Flags]
internal enum SyncSections
{
    None = 0,
    Focus = 1,
    Websites = 2,
    All = Focus | Websites
}
