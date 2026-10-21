namespace Synqora.Core;

// Carry fractions between raw pointer reports instead of rounding every report
// to zero. This preserves slow movement from high-density/high-polling screens.
public sealed class RelativeMotion
{
    private double x, y;
    public (int X, int Y) Add(float dx, float dy)
    {
        x += dx; y += dy;
        int wholeX = (int)Math.Truncate(x), wholeY = (int)Math.Truncate(y);
        x -= wholeX; y -= wholeY;
        return (wholeX, wholeY);
    }
    public void Reset() { x = y = 0; }
}
