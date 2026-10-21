using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Synqora.Services;

public static class MicrophoneSetup
{
    private const string PackageHash = "B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB";
    // Preserve the vendor's original package and separate installer. No silent
    // driver installation, default-device changes or vendor renaming occurs.
    public static void OpenPackage()
    {
        var package = Path.Combine(AppContext.BaseDirectory, "Drivers", "VBCABLE_Driver_Pack45.zip");
        using (var file = File.OpenRead(package))
            if (Convert.ToHexString(SHA256.HashData(file)) != PackageHash) throw new InvalidDataException("The microphone setup package could not be verified. Reinstall Synqora.");
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synqora", "Setup", "VB-CABLE-Pack45");
        Directory.CreateDirectory(folder);
        ZipFile.ExtractToDirectory(package, folder, true);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
