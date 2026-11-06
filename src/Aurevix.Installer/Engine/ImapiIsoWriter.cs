using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Aurevix.Core.Logging;

namespace Aurevix.Installer.Engine;

/// <summary>
/// Crea ISOs con IMAPI2 (Image Mastering API), el motor de grabación que ya trae Windows. No hace falta descargar
/// Windows ADK ni usar winget. Sistema de archivos UDF 1.02 (admite install.wim de más de 4 GB) y arranque doble:
/// BIOS (etfsboot.com) y UEFI (efisys.bin), igual que las ISO oficiales de Microsoft.
///
/// Rápido: lee Windows directamente desde la ISO montada (sin copiarlo antes a una carpeta temporal) y añade encima
/// los pocos archivos de AUREVIX (<paramref name="overlayDir"/>), fusionando carpetas como «sources».
/// </summary>
public static class ImapiIsoWriter
{
    private const int FsiFileSystemUdf = 4;
    private const int PlatformX86 = 0, PlatformEfi = 0xEF;
    private const int EmulationNone = 0;
    private const int MediaDvdPlusRDualLayer = 13;

    public static Task BuildAsync(string sourceDir, string? overlayDir, string outputIso, string volumeLabel, string? biosBoot, string? uefiBoot,
        IProgress<(long done, long total)>? progress, CancellationToken ct) =>
        RunSta(() => Build(sourceDir, overlayDir, outputIso, volumeLabel, biosBoot, uefiBoot, progress, ct));

    // Todos los flujos de archivo que IMAPI mantiene abiertos: hay que soltarlos al terminar o la ISO de Windows
    // montada quedaría «en uso» y no se podría desmontar (ni borrar las carpetas temporales).
    [ThreadStatic] private static List<object>? _held;

    private static void Build(string sourceDir, string? overlayDir, string outputIso, string volumeLabel, string? biosBoot, string? uefiBoot,
        IProgress<(long done, long total)>? progress, CancellationToken ct)
    {
        _held = [];
        try { BuildCore(sourceDir, overlayDir, outputIso, volumeLabel, biosBoot, uefiBoot, progress, ct); }
        finally
        {
            foreach (var o in _held) { try { Marshal.FinalReleaseComObject(o); } catch { } }
            _held = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static void BuildCore(string sourceDir, string? overlayDir, string outputIso, string volumeLabel, string? biosBoot, string? uefiBoot,
        IProgress<(long done, long total)>? progress, CancellationToken ct)
    {
        var fsiType = Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage") ?? throw new InvalidOperationException("IMAPI2 no está disponible en este Windows.");
        dynamic image = Activator.CreateInstance(fsiType)!;
        _held!.Add(image);
        image.ChooseImageDefaultsForMediaType(MediaDvdPlusRDualLayer);
        image.FreeMediaBlocks = 0; // sin límite de capacidad: la ISO de Windows 11 puede pasar de 8,5 GB con AUREVIX
        image.FileSystemsToCreate = FsiFileSystemUdf;
        image.UDFRevision = 0x102;
        image.VolumeName = volumeLabel;

        var boots = new List<object>();
        foreach (var (file, platform) in new[] { (biosBoot, PlatformX86), (uefiBoot, PlatformEfi) })
        {
            if (file is null || !File.Exists(file)) continue;
            var bootType = Type.GetTypeFromProgID("IMAPI2FS.BootOptions")!;
            dynamic boot = Activator.CreateInstance(bootType)!;
            _held!.Add(boot);
            boot.AssignBootImage(OpenComStream(file));
            boot.PlatformId = platform;
            boot.Emulation = EmulationNone;
            boot.Manufacturer = "AUREVIX";
            boots.Add(boot);
        }
        if (boots.Count > 0) image.BootImageOptionsArray = boots.ToArray();

        ct.ThrowIfCancellationRequested();
        image.Root.AddTree(sourceDir, false);
        if (overlayDir is not null && Directory.Exists(overlayDir)) Merge(image.Root, overlayDir);
        ct.ThrowIfCancellationRequested();

        dynamic result = image.CreateResultImage();
        _held!.Add(result);
        var isoStream = (IStream)result.ImageStream;
        _held.Add(isoStream);
        long total = (long)result.TotalBlocks * (long)result.BlockSize;
        WriteStream(isoStream, outputIso, total, progress, ct);
        AurevixLog.Info($"ISO creada con IMAPI2: {outputIso} ({total / 1_000_000} MB)");
    }

    /// <summary>Añade los archivos de <paramref name="dir"/> a la imagen, entrando en las carpetas que ya existen (sources…).</summary>
    private static void Merge(dynamic target, string dir)
    {
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            dynamic? child = null;
            try { child = target[name]; } catch (COMException) { }
            if (child is null) { target.AddDirectory(name); child = target[name]; }
            _held?.Add(child!);
            Merge(child, sub);
        }
        foreach (var file in Directory.GetFiles(dir))
            target.AddFile(Path.GetFileName(file), OpenComStream(file));
    }

    private static void WriteStream(IStream src, string path, long total, IProgress<(long done, long total)>? progress, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024 * 1024];
        var readPtr = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            long written = 0;
            var last = Environment.TickCount64;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                src.Read(buffer, buffer.Length, readPtr);
                var n = Marshal.ReadInt32(readPtr);
                if (n <= 0) break;
                fs.Write(buffer, 0, n);
                written += n;
                if (Environment.TickCount64 - last > 300) { last = Environment.TickCount64; progress?.Report((written, total)); }
            }
            progress?.Report((written, total));
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

    private static IStream OpenComStream(string file)
    {
        var stream = SHCreateStreamOnFileEx(file, 0x20 /* STGM_READ | SHARE_DENY_WRITE */, 0, false, null);
        _held?.Add(stream);
        return stream;
    }

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
