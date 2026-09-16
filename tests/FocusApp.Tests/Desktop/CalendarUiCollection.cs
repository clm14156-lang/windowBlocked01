using Xunit;

namespace FocusApp.Tests.Desktop;

// WPF popup tests share process-wide mouse capture and must run alone.
[CollectionDefinition("Calendar UI", DisableParallelization = true)]
public sealed class CalendarUiCollection { }
