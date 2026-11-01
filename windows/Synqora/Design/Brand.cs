using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Synqora.Design;

public static class Brand
{
    // Use the supplied transparent PNG, including its original uncropped margin.
    public static UIElement Mark(Glass glass, double size = 30)
    {
        return new Image {
            Width = size, Height = size, Stretch = Stretch.Uniform,
            Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "synqora-mark.png"))),
            IsHitTestVisible = false
        };
    }
}
