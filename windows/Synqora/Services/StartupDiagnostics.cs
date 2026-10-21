using System.Runtime.InteropServices;

namespace Synqora;

// Logging must never throw back into WinUI's unhandled-exception callback.
// Installed files belong in Program Files; diagnostics belong to the current user.
internal static class StartupDiagnostics
{
    private static readonly object Gate = new();
    internal static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synqora", "Logs", "startup.log");

    internal static void Write(string stage, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                var path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                    File.Move(path, path + ".previous", true);
                // No command-line arguments, pairing links or credentials are logged.
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {stage}{Environment.NewLine}{(error is null ? "" : error + Environment.NewLine)}");
            }
        }
        catch { /* An unavailable log directory must never trigger another crash. */ }
    }

    internal static void Fail(Exception error, string[] args)
    {
        Write("Startup failed", error);
        if (!args.Contains("--background-capture"))
        {
            try { MessageBoxW(IntPtr.Zero, $"Synqora could not start. Please reinstall the latest build.\n\nDetails: {LogPath}", "Synqora could not start", 0x10); }
            catch { }
        }
        Environment.Exit(1);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
