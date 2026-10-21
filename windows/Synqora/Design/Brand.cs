using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Synqora.Design;

public static class Brand
{
    // Same original vector as brand/synqora-mark.svg, tinted for readable light/dark UI.
    public static UIElement Mark(Glass glass, double size = 30)
    {
        var figure = new PathFigure { StartPoint = new(66, 36), IsClosed = false };
        figure.Segments.Add(new BezierSegment { Point1 = new(60, 29), Point2 = new(48, 28), Point3 = new(40, 35) });
        figure.Segments.Add(new BezierSegment { Point1 = new(30, 44), Point2 = new(38, 51), Point3 = new(48, 55) });
        figure.Segments.Add(new LineSegment { Point = new(60, 60) });
        figure.Segments.Add(new BezierSegment { Point1 = new(72, 66), Point2 = new(64, 79), Point3 = new(53, 79) });
        figure.Segments.Add(new BezierSegment { Point1 = new(45, 79), Point2 = new(39, 75), Point3 = new(35, 70) });
        var geometry = new PathGeometry(); geometry.Figures.Add(figure);
        var canvas = new Canvas { Width = 108, Height = 108 };
        canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = glass.Brush("accent"), StrokeThickness = 7.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
