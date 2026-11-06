using System.IO;

namespace Aurevix.Installer.Engine;

/// <summary>
/// Comprueba el motor de ISOs con un archivo de más de 4 GB (como install.wim), arranque BIOS + UEFI y la fusión
/// de los archivos de AUREVIX dentro de carpetas que ya existen en Windows («sources»).
/// </summary>
public static class SelfTest
{
    public static async Task IsoAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var src = Path.Combine(outDir, "iso-src");
        var overlay = Path.Combine(outDir, "iso-overlay");
        Directory.CreateDirectory(Path.Combine(src, "sources"));
        Directory.CreateDirectory(Path.Combine(src, "boot"));
        Directory.CreateDirectory(Path.Combine(src, @"efi\microsoft\boot"));
        await File.WriteAllTextAsync(Path.Combine(src, "setup.exe"), "setup");
        using (var big = new FileStream(Path.Combine(src, @"sources\install.wim"), FileMode.Create)) big.SetLength(4_500_000_000);
        var bios = Path.Combine(src, @"boot\etfsboot.com");
        var efi = Path.Combine(src, @"efi\microsoft\boot\efisys.bin");
        await File.WriteAllBytesAsync(bios, new byte[4096]);
        await File.WriteAllBytesAsync(efi, new byte[1_474_560]);

        var fakeSetup = Path.Combine(outDir, "AurevixSetup.exe");
        await File.WriteAllBytesAsync(fakeSetup, new byte[123_456]);
        Directory.CreateDirectory(overlay);
        MediaEngine.AddAurevixFiles(overlay, fakeSetup);

        var iso = Path.Combine(outDir, "selftest.iso");
        await ImapiIsoWriter.BuildAsync(src, overlay, iso, "AUREVIX_TEST", bios, efi, null, CancellationToken.None);
        var size = new FileInfo(iso).Length;
        if (size < 4_500_000_000) throw new InvalidOperationException($"ISO demasiado pequeña: {size} bytes");
        Directory.Delete(src, true);
        Directory.Delete(overlay, true);
    }
}
