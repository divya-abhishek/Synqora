using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Numerics;
using Windows.UI;
using Synqora.Services;

namespace Synqora.Design;

public sealed class Glass(Preferences preferences)
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, SpringVector3NaturalMotionAnimation> selectionAnimations = new();
    public bool Dark { get; set; } = true;
    public bool HighContrast => new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
    public bool Motion => !preferences.ReducedMotion && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    public bool Solid => preferences.ReducedTransparency || preferences.Effects == "Economy" || HighContrast || !new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled || Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On;
    public Color Color(string token, double alpha = 1)
    {
        if (HighContrast)
        {
            var ui = new Windows.UI.ViewManagement.UISettings();
            var c = ui.GetColorValue(token is "background" or "surface" or "solid" or "elevated" or "onAccent" ? Windows.UI.ViewManagement.UIColorType.Background : Windows.UI.ViewManagement.UIColorType.Foreground);
            return c;
        }
        var hex = (Dark ? Tokens.Dark : Tokens.Light)[token].TrimStart('#');
        return Windows.UI.Color.FromArgb((byte)(alpha * 255), Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }
    public SolidColorBrush Brush(string token, double alpha = 1) => new(Color(token, alpha));
    public TextBlock Text(string text, double size = 14, string color = "text", bool bold = false)
        => new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI Variable"), IsTextSelectionEnabled = false };
    public TextBlock Display(string text, double size = 30) => new() { Text = text, FontSize = size, Foreground = Brush("text"), FontFamily = new FontFamily("Segoe UI Variable"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, CharacterSpacing = -20 };
    public static void RoundClip(FrameworkElement element, float radius)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var shape = visual.Compositor.CreateRoundedRectangleGeometry(); shape.CornerRadius = new(radius);
        visual.Clip = visual.Compositor.CreateGeometricClip(shape);
        element.SizeChanged += (_, _) => shape.Size = new((float)element.ActualWidth, (float)element.ActualHeight);
    }
    public void MoveSelection(Border highlight, Button item, UIElement container, bool animate) {
        if (item.ActualWidth <= 0) return;
        highlight.Width = item.ActualWidth;
        var point = item.TransformToVisual(container).TransformPoint(new Windows.Foundation.Point(0, 0));
        var compositor = CompositionTarget.GetCompositorForCurrentThread();
        var target = new Vector3((float)point.X, 0, 0);
        var spring = selectionAnimations.GetValue(highlight, _ => compositor.CreateSpringVector3Animation());
        spring.Target = "Translation"; spring.FinalValue = target; spring.DampingRatio = .9f; spring.Period = TimeSpan.FromMilliseconds(260);
        if (animate && Motion) {
            highlight.StartAnimation(spring);
        } else { highlight.StopAnimation(spring); highlight.Translation = target; }
    }
    public FontIcon Icon(string glyph, double size = 20, string color = "accent")
        => new() { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = size, Foreground = Brush(color), IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    public static StackPanel Stack(double gap = 16, params UIElement[] children)
    {
        var p = new StackPanel { Spacing = gap };
        foreach (var child in children) p.Children.Add(child);
        return p;
    }
    public static StackPanel Row(double gap = 12, params UIElement[] children)
    {
        var p = Stack(gap, children); p.Orientation = Orientation.Horizontal; p.VerticalAlignment = VerticalAlignment.Center; return p;
    }
    public Border Surface(UIElement content, string material = "Regular", double padding = 24)
    {
        var m = Tokens.Materials[Solid ? "SolidFallback" : material];
        Brush fill;
        if (Solid) fill = Brush("solid");
        else if (material == "Regular") fill = new AcrylicBrush { TintColor = Color("surface"), TintOpacity = .16, TintLuminosityOpacity = .65, FallbackColor = Color("solid") };
        else
        {
            var gradient = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
            gradient.GradientStops.Add(new GradientStop { Color = Color(material == "Elevated" ? "elevated" : "surface", Math.Min(1, m.Opacity + .08)), Offset = 0 });
            gradient.GradientStops.Add(new GradientStop { Color = Color("surface", m.Opacity), Offset = .65 });
            gradient.GradientStops.Add(new GradientStop { Color = Color("surface", m.Opacity), Offset = 1 });
            fill = gradient;
        }
        var rim = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        rim.GradientStops.Add(new() { Color = Color("text", material == "Elevated" ? .18 : .1), Offset = 0 });
        rim.GradientStops.Add(new() { Color = Color("text", .05), Offset = .45 });
        rim.GradientStops.Add(new() { Color = Color("text", .08), Offset = 1 });
        var b = new Border { Background = fill, BorderBrush = Solid ? Brush("border") : rim, BorderThickness = new(1), CornerRadius = new(m.Radius), Padding = new(padding), Child = content };
        if (material == "Elevated" && !Solid) { b.Shadow = new ThemeShadow(); b.Translation = new Vector3(0, 0, m.Elevation); }
        return b;
    }
    public Border Card(UIElement content) => Surface(content, "Subtle");
    public Button Button(string label, Action action, bool primary = false, string? glyph = null, bool enabled = true)
    {
        var b = new Button { Content = glyph is null ? Text(label, 14, primary ? "onAccent" : "text", true) : Row(10, Icon(glyph, 18, primary ? "onAccent" : "accent"), Text(label, 14, primary ? "onAccent" : "text", true)), Background = Brush(primary ? "accent" : "elevated", primary || Solid ? 1 : .48), BorderBrush = Brush("border", .32), BorderThickness = new(1), CornerRadius = new(Tokens.RadiusButton), Padding = new(18, 12, 18, 12), MinHeight = 48, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Left };
        b.Resources["ButtonBackgroundPointerOver"] = Brush(primary ? "accent" : "elevated", primary || Solid ? 1 : .85);
        b.Resources["ButtonBackgroundPressed"] = Brush(primary ? "accent" : "elevated", primary || Solid ? .85 : 1);
        b.Resources["ButtonBorderBrushPointerOver"] = Brush("text", .3);
        b.Resources["ButtonBorderBrushPressed"] = Brush("text", .16);
        AutomationProperties.SetName(b, label);
        b.Click += (_, _) => action();
        AttachMotion(b);
        return b;
    }
    public Button IconButton(string glyph, string label, Action action)
    {
        var b = Button(label, action);
        var icon = Icon(glyph); icon.HorizontalAlignment = HorizontalAlignment.Center;
        b.Content = icon; b.Width = 48; b.Height = 48; b.Padding = new(0); b.CornerRadius = new(24);
        ToolTipService.SetToolTip(b, label); return b;
    }
    public Border Status(string label, bool connected = false)
    {
        var dot = new Ellipse { Width = 6, Height = 6, Fill = Brush(connected ? "success" : "muted") };
        var status = Surface(Row(8, dot, Text(label, 12, connected ? "success" : "muted")), "Subtle", 10);
        status.CornerRadius = new(18); status.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(status, label); return status;
    }
    public Slider Slider(string label, double value, Action<double> changed)
    {
        var s = new Slider { Header = label, Minimum = 0, Maximum = 100, Value = value, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(s, label); s.ValueChanged += (_, e) => changed(e.NewValue); return s;
    }
    public ToggleSwitch Switch(string label, bool value, Action<bool> changed)
    {
        var s = new ToggleSwitch { Header = label, IsOn = value, OnContent = "On", OffContent = "Off", MinWidth = 130 };
        AutomationProperties.SetName(s, label); s.Toggled += (_, _) => changed(s.IsOn); return s;
    }
    public ComboBox Dropdown(string label, string[] choices, string current, Action<string> changed)
    {
        var s = new ComboBox { Header = label, MinWidth = 190, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var c in choices) s.Items.Add(c);
        s.SelectedItem = current; AutomationProperties.SetName(s, label);
        s.SelectionChanged += (_, _) => { if (s.SelectedItem is string choice) changed(choice); }; return s;
    }
    public async Task Dialog(XamlRoot root, string title, UIElement content)
    {
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(180, Math.Min(620, root.Size.Height - 180)) };
        var dialog = new ContentDialog { XamlRoot = root, Title = title, Content = scroll, CloseButtonText = "Done", DefaultButton = ContentDialogButton.Close, RequestedTheme = Dark ? ElementTheme.Dark : ElementTheme.Light, CornerRadius = new(Tokens.RadiusModal), Background = Solid ? Brush("solid") : new AcrylicBrush { TintColor = Color("surface"), TintOpacity = .94, TintLuminosityOpacity = .85, FallbackColor = Color("solid") } };
        await dialog.ShowAsync();
    }
    public void Enter(UIElement element)
    {
        if (!Motion) return;
        var compositor = CompositionTarget.GetCompositorForCurrentThread();
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 0); opacity.InsertKeyFrame(1, 1); opacity.Duration = TimeSpan.FromMilliseconds(Tokens.MotionNavigation);
        opacity.Target = "Opacity"; element.StartAnimation(opacity);
        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.InsertKeyFrame(0, new(0, 8, 0)); offset.InsertKeyFrame(1, Vector3.Zero); offset.Duration = opacity.Duration;
        offset.Target = "Translation"; element.StartAnimation(offset);
    }
    public void AttachMotion(Button b)
    {
        SpriteVisual? light = null;
        CompositionRoundedRectangleGeometry? lightShape = null;
        b.Loaded += (_, _) => {
            if (HighContrast || light != null) return;
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            light = compositor.CreateSpriteVisual(); light.RelativeSizeAdjustment = Vector2.One; light.Opacity = 0;
            light.Brush = compositor.CreateColorBrush(Color("text", .07));
            lightShape = compositor.CreateRoundedRectangleGeometry(); lightShape.CornerRadius = new((float)b.CornerRadius.TopLeft); lightShape.Size = new((float)b.ActualWidth, (float)b.ActualHeight);
            light.Clip = compositor.CreateGeometricClip(lightShape);
            ElementCompositionPreview.SetElementChildVisual(b, light);
        };
        b.SizeChanged += (_, _) => { if (lightShape != null) lightShape.Size = new((float)b.ActualWidth, (float)b.ActualHeight); };
        void Light(float value) {
            if (light == null) return;
            if (!Motion) { light.Opacity = value; return; }
            var fade = light.Compositor.CreateScalarKeyFrameAnimation(); fade.InsertKeyFrame(1, value); fade.Duration = TimeSpan.FromMilliseconds(140); light.StartAnimation("Opacity", fade);
        }
        void Scale(float scale)
        {
            if (!Motion) return;
            b.CenterPoint = new((float)b.ActualWidth / 2, (float)b.ActualHeight / 2, 0);
            var a = CompositionTarget.GetCompositorForCurrentThread().CreateSpringVector3Animation(); a.Target = "Scale"; a.FinalValue = new(scale, scale, 1); a.DampingRatio = .86f; a.Period = TimeSpan.FromMilliseconds(180);
            b.StartAnimation(a);
        }
        b.PointerEntered += (_, _) => Light(.7f);
        b.PointerPressed += (_, _) => { Scale(.98f); Light(1); };
        b.PointerReleased += (_, _) => { Scale(1); Light(.7f); };
        b.PointerCanceled += (_, _) => { Scale(1); Light(0); };
        b.PointerCaptureLost += (_, _) => { Scale(1); Light(0); };
        b.PointerExited += (_, _) => { Scale(1); Light(0); };
    }
}

// Bounded view only. The producer can publish levels from a background adapter.
public sealed class GlassAudioVisualizer : Canvas
{
    private readonly Glass glass;
    private readonly float[] latest = new float[64];
    private readonly Line[] bars = new Line[64];
    private readonly object gate = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
    public GlassAudioVisualizer(Glass g)
    {
        glass = g; Height = 170;
        for (int i = 0; i < bars.Length; i++) { bars[i] = new Line { Stroke = g.Brush(i % 3 == 0 ? "cyan" : "accent", .85), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; Children.Add(bars[i]); }
        SizeChanged += (_, _) => Draw(); timer.Tick += (_, _) => Draw(); Unloaded += (_, _) => timer.Stop();
        AutomationProperties.SetName(this, "Audio input level. No audio stream.");
    }
    public void Publish(ReadOnlySpan<float> samples) { lock (gate) { for (int i = 0; i < latest.Length; i++) latest[i] = i < samples.Length ? Math.Clamp(samples[i], 0, 1) : 0; } }
    public void SetStreaming(bool active) { if (active) timer.Start(); else { timer.Stop(); Array.Clear(latest); Draw(); } }
    private void Draw()
    {
        lock (gate) for (int i = 0; i < bars.Length; i++)
        {
            var h = Math.Max(3, latest[i] * (ActualHeight - 16));
            bars[i].X1 = bars[i].X2 = (i + .5) * ActualWidth / bars.Length;
            bars[i].Y1 = (ActualHeight - h) / 2; bars[i].Y2 = (ActualHeight + h) / 2;
        }
    }
}
