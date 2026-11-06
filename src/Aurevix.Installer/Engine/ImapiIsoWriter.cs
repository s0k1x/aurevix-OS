using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Aurevix.Core.Logging;

namespace Aurevix.Installer.Engine;

/// <summary>
/// Crea ISOs con IMAPI2 (Image Mastering API), el motor de grabación que ya trae Windows. No hace falta descargar
/// Windows ADK ni usar winget. Sistema de archivos UDF 1.02 (admite install.wim de más de 4 GB) y arranque doble:
/// BIOS (etfsboot.com) y UEFI (efisys.bin), igual que las ISO oficiales de Microsoft.
/// </summary>
public static class ImapiIsoWriter
{
    private const int FsiFileSystemUdf = 4;
    private const int PlatformX86 = 0, PlatformEfi = 0xEF;
    private const int EmulationNone = 0;
    private const int MediaDvdPlusRDualLayer = 13; // 8,5 GB: suficiente para Windows 11

    public static Task BuildAsync(string sourceDir, string outputIso, string volumeLabel, string? biosBoot, string? uefiBoot,
        IProgress<double>? progress, CancellationToken ct) =>
        RunSta(() => Build(sourceDir, outputIso, volumeLabel, biosBoot, uefiBoot, progress, ct));

    private static void Build(string sourceDir, string outputIso, string volumeLabel, string? biosBoot, string? uefiBoot,
        IProgress<double>? progress, CancellationToken ct)
    {
        var fsiType = Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage") ?? throw new InvalidOperationException("IMAPI2 no está disponible en este Windows.");
        dynamic image = Activator.CreateInstance(fsiType)!;
        image.ChooseImageDefaultsForMediaType(MediaDvdPlusRDualLayer);
        image.FileSystemsToCreate = FsiFileSystemUdf;
        image.UDFRevision = 0x102;
        image.VolumeName = volumeLabel;

        var boots = new List<object>();
        foreach (var (file, platform) in new[] { (biosBoot, PlatformX86), (uefiBoot, PlatformEfi) })
        {
            if (file is null || !File.Exists(file)) continue;
            var bootType = Type.GetTypeFromProgID("IMAPI2FS.BootOptions")!;
            dynamic boot = Activator.CreateInstance(bootType)!;
            var stream = OpenComStream(file);
            boot.AssignBootImage(stream);
            boot.PlatformId = platform;
            boot.Emulation = EmulationNone;
            boot.Manufacturer = "AUREVIX";
            boots.Add(boot);
        }
        if (boots.Count > 0) image.BootImageOptionsArray = boots.ToArray();

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.05);
        image.Root.AddTree(sourceDir, false);
        ct.ThrowIfCancellationRequested();

        dynamic result = image.CreateResultImage();
        var isoStream = (IStream)result.ImageStream;
        long total = (long)result.TotalBlocks * (long)result.BlockSize;
        WriteStream(isoStream, outputIso, total, progress, ct);
        Marshal.FinalReleaseComObject(isoStream);
        AurevixLog.Info($"ISO creada con IMAPI2: {outputIso} ({total / 1_000_000} MB)");
    }

    private static void WriteStream(IStream src, string path, long total, IProgress<double>? progress, CancellationToken ct)
    {
        var buffer = new byte[4 * 1024 * 1024];
        var readPtr = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            long written = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                src.Read(buffer, buffer.Length, readPtr);
                var n = Marshal.ReadInt32(readPtr);
                if (n <= 0) break;
                fs.Write(buffer, 0, n);
                written += n;
                if (total > 0) progress?.Report(0.1 + 0.9 * Math.Min(1, (double)written / total));
            }
        }
        catch
        {
            try { File.Delete(path); } catch { }
            throw;
        }
        finally { Marshal.FreeHGlobal(readPtr); }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern IStream SHCreateStreamOnFileEx(string file, int mode, int attributes, bool create, IStream? template);

    private static IStream OpenComStream(string file) => SHCreateStreamOnFileEx(file, 0x20 /* STGM_READ | SHARE_DENY_WRITE */, 0, false, null);

    /// <summary>IMAPI2 es COM: se ejecuta en un hilo STA propio para no depender del hilo que llama.</summary>
    private static Task RunSta(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (OperationCanceledException) { tcs.SetCanceled(); }
            catch (Exception ex) { tcs.SetException(ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex); }
        }) { IsBackground = true, Name = "AUREVIX ISO" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }
}
