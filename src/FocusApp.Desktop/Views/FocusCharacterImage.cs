using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.Views;

/// <summary>Loads a character only when displayed and decodes at the physical display size.</summary>
public sealed class FocusCharacterImage : Image
{
    private BitmapImage? _decodedImage;
    public static readonly DependencyProperty CharacterIdProperty = DependencyProperty.Register(
        nameof(CharacterId), typeof(string), typeof(FocusCharacterImage), new PropertyMetadata(FocusCharacters.DefaultId, SourceChanged));
    public static readonly DependencyProperty IsNightProperty = DependencyProperty.Register(
        nameof(IsNight), typeof(bool), typeof(FocusCharacterImage), new PropertyMetadata(false, SourceChanged));
    public static readonly DependencyProperty DecodePixelWidthProperty = DependencyProperty.Register(
        nameof(DecodePixelWidth), typeof(int), typeof(FocusCharacterImage), new PropertyMetadata(0, SourceChanged));

    public FocusCharacterImage() => Loaded += (_, _) => UpdateSource();
    public string CharacterId { get => (string)GetValue(CharacterIdProperty); set => SetValue(CharacterIdProperty, value); }
    public bool IsNight { get => (bool)GetValue(IsNightProperty); set => SetValue(IsNightProperty, value); }
    public int DecodePixelWidth { get => (int)GetValue(DecodePixelWidthProperty); set => SetValue(DecodePixelWidthProperty, value); }

    private static void SourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((FocusCharacterImage)sender).UpdateSource();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateSource();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateSource();
    }

    private void UpdateSource()
    {
        if (!IsLoaded) return;
        // Image's render size can become zero while Source is being replaced. The explicit
        // layout width stays stable and prevents that transition from triggering another decode.
        var displayWidth = double.IsFinite(Width) && Width > 0 ? Width : ActualWidth;
        // Small fixed buckets avoid caching a new bitmap for every resize or monitor transition.
        var width = DecodePixelWidth > 0 ? DecodePixelWidth :
            Math.Clamp((int)Math.Ceiling(displayWidth * VisualTreeHelper.GetDpi(this).DpiScaleX / 160) * 160, 160, 800);
        var image = FocusCharacterImages.Get(CharacterId, IsNight, width);
        if (ReferenceEquals(_decodedImage, image)) return;
        _decodedImage = image;
        Source = image;
    }
}
