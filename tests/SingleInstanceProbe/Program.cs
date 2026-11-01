using System.Diagnostics;
using Synqora;

if (args.Length > 0)
{
    using var child = new SingleInstance(args[0]);
    if (child.IsPrimary) return 3;
    child.Signal();
    return 0;
}
var key = "Local\\Synqora.Test." + Guid.NewGuid();
using (var primary = new SingleInstance(key))
{
    if (!primary.IsPrimary) throw new Exception("First launch did not acquire ownership.");
    using var restored = new AutoResetEvent(false);
    primary.Listen(() => restored.Set());
    for (int i = 0; i < 5; i++)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(key);
        using var process = Process.Start(info)!;
        if (!process.WaitForExit(10000) || process.ExitCode != 0 || !restored.WaitOne(5000))
            throw new Exception("Reopening did not forward activation to the original owner.");
    }
}
using var restart = new SingleInstance(key);
if (!restart.IsPrimary) throw new Exception("Ownership was not released on exit.");
Console.WriteLine("PASS: five separate launches forwarded activation; ownership released on exit.");
return 0;
