using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Synqora.Design;

public static class Brand
{
    // Original paired-loop symbol. Launcher exports use a larger safe-area canvas.
    public static UIElement Mark(Glass glass, double size = 30)
    {
        var canvas = new Canvas { Width = 72, Height = 72 };
        string[] paths = ["M54 40 H44 C36 40 30 46 30 54 V65 C30 73 36 79 44 79 H52 C60 79 66 73 66 65 V57", "M54 68 H64 C72 68 78 62 78 54 V43 C78 35 72 29 64 29 H56 C48 29 42 35 42 43 V51"];
        foreach (var data in paths) {
            var shape = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' StrokeThickness='8' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
            shape.Stroke = glass.Brush("accent"); Canvas.SetLeft(shape, -18); Canvas.SetTop(shape, -18); canvas.Children.Add(shape);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }
}
