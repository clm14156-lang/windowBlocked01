namespace FocusApp.Desktop.Services.Cloud;

public interface ICloudConnectionProbe
{
    Task<bool> CheckConnectionAsync(CancellationToken cancellationToken);
}
