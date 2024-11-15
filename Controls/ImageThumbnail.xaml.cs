using System.Windows;
using System.Windows.Controls;

public partial class ImageThumbnail : UserControl
{
    public static readonly DependencyProperty PreserveAspectRatioProperty =
        DependencyProperty.Register(
            nameof(PreserveAspectRatio),
            typeof(bool),
            typeof(ImageThumbnail),
            new PropertyMetadata(false));

    public bool PreserveAspectRatio
    {
        get => (bool)GetValue(PreserveAspectRatioProperty);
        set => SetValue(PreserveAspectRatioProperty, value);
    }
} 