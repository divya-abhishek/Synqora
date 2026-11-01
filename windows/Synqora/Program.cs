using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace Synqora;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Automated captures and isolated receiver probes must not activate a user's session.
        var isolated = args.Contains("--capture") || args.Contains("--receiver-test");
        using var instance = new SingleInstance(isolated ? "Local\\Synqora.Probe." + Guid.NewGuid() : "Local\\Synqora." + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value);
        if (!instance.IsPrimary) { instance.Signal(); return; }
        StartupDiagnostics.Write("Starting Synqora " + typeof(Program).Assembly.GetName().Version);
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(startArgs =>
            {
                SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
                _ = new SynqoraApp(args, instance);
            });
        }
        catch (Exception e) { StartupDiagnostics.Fail(e, args); }
    }
}

public sealed class SynqoraApp(string[] args, SingleInstance instance) : Application, Microsoft.UI.Xaml.Markup.IXamlMetadataProvider
{
    private readonly Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider metadata = new();
    public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(Type type) => metadata.GetXamlType(type);
    public Microsoft.UI.Xaml.Markup.IXamlType GetXamlType(string name) => metadata.GetXamlType(name);
    public Microsoft.UI.Xaml.Markup.XmlnsDefinition[] GetXmlnsDefinitions() => metadata.GetXmlnsDefinitions();
    private MainWindow? window;
    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        UnhandledException += (_, eventArgs) => StartupDiagnostics.Write("Unhandled UI exception", eventArgs.Exception);
        try
        {
            Resources.MergedDictionaries.Add(new Microsoft.UI.Xaml.Controls.XamlControlsResources());
            window = new MainWindow(args);
            instance.Listen(() => window.DispatcherQueue.TryEnqueue(window.RestoreExistingSession));
            if (args.Contains("--background-capture"))
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(-16000, -16000));
                window.AppWindow.Show(false);
            }
            else window.Activate();
            StartupDiagnostics.Write("Window initialized");
        }
        catch (Exception error) { StartupDiagnostics.Fail(error, args); }
    }
}
