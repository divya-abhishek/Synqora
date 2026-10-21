using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;

namespace Synqora.Design;

// Native frosted backdrop. Keep transforms out of a backdrop effect graph:
// the Windows compositor does not support transforming a backdrop input.
public sealed class LensBrush(Glass glass) : XamlCompositionBrushBase
{
    private static CompositionEffectFactory? factory;
    private bool pressed;
    protected override void OnConnected()
    {
        FallbackColor = glass.Color("elevated", .55);
        if (CompositionBrush != null || glass.Solid) return;
        try {
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            factory ??= compositor.CreateEffectFactory(new ArithmeticCompositeEffect {
                Name = "Tint", Source1Amount = .88f, Source2Amount = .12f,
                Source1 = new GaussianBlurEffect { BlurAmount = 3, BorderMode = EffectBorderMode.Hard, Source = new CompositionEffectSourceParameter("Backdrop") },
                Source2 = new ColorSourceEffect { Name = "Color" }
            }, ["Tint.Source1Amount", "Tint.Source2Amount", "Color.Color"]);
            var brush = factory.CreateBrush(); brush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());
            brush.Properties.InsertColor("Color.Color", glass.Color("elevated")); CompositionBrush = brush; Update();
        } catch (Exception error) { StartupDiagnostics.Write("Native glass fallback", error); }
    }
    public void Pressure(bool value) { pressed = value; Update(); }
    private void Update()
    {
        if (CompositionBrush is not CompositionEffectBrush brush) return;
        var tint = pressed ? .18f : .12f; brush.Properties.InsertScalar("Tint.Source1Amount", 1 - tint); brush.Properties.InsertScalar("Tint.Source2Amount", tint);
    }
    protected override void OnDisconnected() { CompositionBrush?.Dispose(); CompositionBrush = null; }
}
