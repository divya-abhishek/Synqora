using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Synqora.Design;

// SystemBackdrop owns activation, policy and accessibility configuration; the
// controller is recreated when the app theme changes, so overrides stay coherent.
public sealed class ClearBackdrop(Glass glass) : SystemBackdrop
{
    private readonly Dictionary<ICompositionSupportsSystemBackdrop, DesktopAcrylicController> controllers = [];
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        var controller = new DesktopAcrylicController {
            Kind = DesktopAcrylicKind.Thin,
            TintColor = glass.Color("background"),
            TintOpacity = glass.Dark ? .18f : .22f,
            LuminosityOpacity = glass.Dark ? .55f : .72f,
            FallbackColor = glass.Color("solid")
        };
        controller.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(target, xamlRoot));
        controller.AddSystemBackdropTarget(target); controllers.Add(target, controller);
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        if (controllers.Remove(target, out var controller)) { controller.RemoveSystemBackdropTarget(target); controller.Dispose(); }
        base.OnTargetDisconnected(target);
    }
}
