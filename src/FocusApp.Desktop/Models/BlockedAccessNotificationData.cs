namespace FocusApp.Desktop.Models;

/// <summary>
/// Presentation data used by the blocked-access notification mock.
/// It is intentionally independent from the access-control domain model.
/// </summary>
public sealed record BlockedAccessNotificationData(
    string Name,
    string Address,
    string Type);
