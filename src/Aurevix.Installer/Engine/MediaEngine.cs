using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Aurevix.Core;
using Aurevix.Core.Logging;
using Aurevix.Update;

namespace Aurevix.Installer.Engine;

public sealed record UsbDisk(int Number, string Name, long SizeBytes, string Letters = "")
{
    public string Display => $"{Name} · {SizeBytes / 1e9:0.#} GB" + (Letters.Length > 0 ? $" · unidad {Letters}" : "");
}

/// <summary>Paso de una operación. <c>Fraction</c> es el avance del paso actual (0-1); <c>Detail</c>, la línea de apoyo (GB, velocidad…).</summary>
public sealed record Step(string Text, double? Fraction = null, string? Detail = null);

/// <summary>
/// Motor del AUREVIX Installer:
///   - Descarga la ISO oficial de Windows 11 desde Microsoft (enlace obtenido con Fido).
///   - Descarga el instalador de AUREVIX verificado (manifiesto firmado + SHA-256).
///   - Prepara un USB arrancable (FAT32 + install.wim dividido) o una ISO (IMAPI2, el motor de Windows).
///   - Lanza una reinstalación limpia con el Windows Setup oficial.
/// Nunca modifica install.wim ni componentes protegidos de Windows.
/// </summary>
public sealed class MediaEngine
{
    public const string FidoSha256 = "ceedef02aca0135fcd98f481d1c386e467432f2973bbbade093205c380330da5";
    private const string ManifestBase = "https://zttqiphwurnferzlubzy.supabase.co/storage/v1/object/public/releases/manifests";
    private static readonly string[] AllowedHosts = ["zttqiphwurnferzlubzy.supabase.co", "github.com", "objects.githubusercontent.com"];

    public static string WorkRoot { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Aurevix", "media");
    public string IsoCachePath => Path.Combine(WorkRoot, "Win11_Oficial_Espanol_x64.iso");

    private readonly HttpClient _http;

    public MediaEngine()
    {
        Directory.CreateDirectory(WorkRoot);
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AurevixInstaller/1.0");
    }

    // ------------------------------------------------------------------ AUREVIX

    /// <summary>Descarga el instalador de AUREVIX de la última versión estable, verificando firma y hash.</summary>
    public async Task<string> DownloadAurevixAsync(IProgress<Step> progress, CancellationToken ct)
    {
        progress.Report(new Step("Buscando la última versión de AUREVIX…"));
        var client = new UpdateClient(new UpdateClientOptions
        {
            ManifestBaseUrl = new Uri(ManifestBase),
            AllowedPackageHosts = AllowedHosts,
            TrustedKeys = ManifestSigner.LoadEmbeddedTrustedKeys()
        }, _http);
        var r = await client.CheckAsync(new SemVersion(0, 0, 0), "stable", Guid.NewGuid().ToString("N"), ct: ct);
        if (r.Status is not (UpdateStatus.Available or UpdateStatus.NotEligibleYet or UpdateStatus.RequiresManualUpdate) || r.Release is null)
            throw new InvalidOperationException($"No se pudo obtener AUREVIX: {r.Message}");
        var pkg = r.Release.Packages.First(p => p.Kind == PackageKind.AppInstaller);
        var path = await client.DownloadAsync(pkg, Path.Combine(WorkRoot, "aurevix"),
            new Progress<double>(f => progress.Report(new Step($"Descargando AUREVIX {r.Release.Version}…", f))), ct);
        AurevixLog.Info($"AUREVIX {r.Release.Version} descargado y verificado: {path}");
        return path;
    }

    // ------------------------------------------------------------------ Windows ISO

    /// <summary>Obtiene el enlace OFICIAL de Microsoft para la ISO de Windows 11 en español (x64) usando Fido.</summary>
    public async Task<Uri> GetWindowsIsoUrlAsync(CancellationToken ct)
    {
        var fido = Path.Combine(WorkRoot, "Fido.ps1");
        await using (var res = typeof(MediaEngine).Assembly.GetManifestResourceStream("Fido.ps1")!)
        await using (var fs = File.Create(fido)) await res.CopyToAsync(fs, ct);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fido, ct))).ToLowerInvariant();
        if (hash != FidoSha256) throw new InvalidOperationException("La herramienta de descarga (Fido) no supera la verificación de integridad.");

        var r = await Shell.RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{fido}\" -Win 11 -Rel Latest -Ed Pro -Lang Spanish -Arch x64 -GetUrl", ct);
        var line = r.Output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("https://"));
        if (!r.Ok || line is null || !Uri.TryCreate(line, UriKind.Absolute, out var uri))
            throw new MicrosoftBlockedException(r.Output + "\n" + r.Error);
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"El enlace obtenido no es de Microsoft ({uri.Host}). Se cancela por seguridad.");
        return uri;
    }

    /// <summary>Descarga la ISO de Microsoft con reanudación (si se corta, continúa donde se quedó).</summary>
    public async Task<string> DownloadWindowsIsoAsync(IProgress<Step> progress, CancellationToken ct)
    {
        if (File.Exists(IsoCachePath) && new FileInfo(IsoCachePath).Length > 3_000_000_000)
        {
            progress.Report(new Step("Usando la ISO de Windows 11 ya descargada", 1, "Ya la tenías descargada: no hace falta volver a bajarla"));
            return IsoCachePath;
        }
        progress.Report(new Step("Pidiendo a Microsoft la ISO oficial de Windows 11"));
        var url = await GetWindowsIsoUrlAsync(ct);
        var partial = IsoCachePath + ".partial";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                long have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                if (have > 0 && resp.StatusCode != System.Net.HttpStatusCode.PartialContent) have = 0; // el servidor no reanuda
                var total = (resp.Content.Headers.ContentLength ?? 0) + have;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(partial, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                var buf = new byte[4 << 20];
                long done = have; int n;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long lastBytes = done; var lastMs = 0L; double speed = 0;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (sw.ElapsedMilliseconds - lastMs > 500 && total > 0)
                    {
                        var inst = (done - lastBytes) / ((sw.ElapsedMilliseconds - lastMs) / 1000.0);
                        speed = speed == 0 ? inst : speed * 0.8 + inst * 0.2;
                        lastBytes = done; lastMs = sw.ElapsedMilliseconds;
                        progress.Report(new Step("Descargando Windows 11 de Microsoft", (double)done / total,
                            $"{Eta.Bytes(done)} de {Eta.Bytes(total)} · {Eta.Speed(speed)}"));
                    }
                }
                if (total > 0 && done < total) throw new IOException("La descarga terminó antes de tiempo.");
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException && attempt < 5 && !ct.IsCancellationRequested)
            {
                AurevixLog.Warn($"Descarga interrumpida (intento {attempt}): {ex.Message}. Reanudando…");
                progress.Report(new Step("Conexión interrumpida, reanudando…"));
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
            }
        }
        File.Move(partial, IsoCachePath, overwrite: true);
        return IsoCachePath;
    }

    public async Task<string> MountAsync(string iso, CancellationToken ct)
    {
        var r = await Shell.PowerShellAsync($"(Mount-DiskImage -ImagePath {Shell.Quote(iso)} -PassThru | Get-Volume).DriveLetter", ct);
        var letter = r.Output.Trim();
        if (!r.Ok || letter.Length != 1) throw new InvalidOperationException("No se pudo montar la ISO de Windows.");
        var root = letter + @":\";
        if (!File.Exists(Path.Combine(root, "setup.exe")) ||
            (!File.Exists(Path.Combine(root, @"sources\install.wim")) && !File.Exists(Path.Combine(root, @"sources\install.esd"))))
            throw new InvalidOperationException("El archivo no es una ISO de instalación de Windows.");
        return root;
    }

    public Task DismountAsync(string iso) => Shell.PowerShellAsync($"Dismount-DiskImage -ImagePath {Shell.Quote(iso)} | Out-Null");

    // ------------------------------------------------------------------ Archivos AUREVIX

    public static string Resource(string name)
    {
        using var s = typeof(MediaEngine).Assembly.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static string Unattend(string uiLanguage = "es-ES", string inputLocale = "040a:0000040a", string timeZone = "Romance Standard Time") =>
        Resource("autounattend.xml").Replace("{{UI_LANGUAGE}}", uiLanguage).Replace("{{INPUT_LOCALE}}", inputLocale).Replace("{{TIME_ZONE}}", timeZone);

    /// <summary>Añade autounattend.xml y $OEM$ (SetupComplete.cmd + instalador) a una carpeta raíz de medio de instalación.</summary>
    public static void AddAurevixFiles(string mediaRoot, string aurevixSetup)
    {
        File.WriteAllText(Path.Combine(mediaRoot, "autounattend.xml"), Unattend());
        var scripts = Path.Combine(mediaRoot, @"sources\$OEM$\$$\Setup\Scripts");
        var payload = Path.Combine(mediaRoot, @"sources\$OEM$\$1\AUREVIX");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(scripts, "SetupComplete.cmd"), Resource("SetupComplete.cmd"));
        File.Copy(aurevixSetup, Path.Combine(payload, "AurevixSetup.exe"), overwrite: true);
    }

    // ------------------------------------------------------------------ USB

    /// <summary>Lista solo discos USB que no sean de arranque ni de sistema.</summary>
    public async Task<IReadOnlyList<UsbDisk>> ListUsbDisksAsync(CancellationToken ct = default)
    {
        var r = await Shell.PowerShellAsync(
            "@(Get-Disk | Where-Object { $_.BusType -eq 'USB' -and -not $_.IsBoot -and -not $_.IsSystem -and $_.Size -gt 0 } | ForEach-Object { " +
            "$l = (Get-Partition -DiskNumber $_.Number -ErrorAction SilentlyContinue | Where-Object DriveLetter | ForEach-Object { \"$($_.DriveLetter):\" }) -join ', '; " +
            "[pscustomobject]@{ Number = $_.Number; FriendlyName = $_.FriendlyName; Size = $_.Size; Letters = $l } }) | ConvertTo-Json -Compress", ct);
        if (!r.Ok || string.IsNullOrWhiteSpace(r.Output)) return [];
        using var doc = JsonDocument.Parse(r.Output);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        return items.Select(e => new UsbDisk(e.GetProperty("Number").GetInt32(), e.GetProperty("FriendlyName").GetString() ?? "USB", e.GetProperty("Size").GetInt64(),
            e.TryGetProperty("Letters", out var l) ? l.GetString() ?? "" : "")).ToList();
    }

    /// <summary>BORRA el USB indicado y crea en él un instalador de AUREVIX OS (UEFI + BIOS).</summary>
    public async Task WriteUsbAsync(UsbDisk disk, string isoRoot, string aurevixSetup, IProgress<Step> progress, CancellationToken ct)
    {
        if (disk.SizeBytes < 8_000_000_000) throw new InvalidOperationException("El USB debe tener al menos 8 GB.");

        progress.Report(new Step($"Preparando el USB {disk.Name} (se borra su contenido)…"));
        // Revalida el disco justo antes de borrarlo: debe seguir siendo USB y no ser de sistema.
        var script = $$"""
            $d = Get-Disk -Number {{disk.Number}}
            if ($d.BusType -ne 'USB' -or $d.IsBoot -or $d.IsSystem) { throw 'El disco ya no es un USB seguro de borrar.' }
            if ($d.PartitionStyle -ne 'RAW') { Clear-Disk -Number {{disk.Number}} -RemoveData -RemoveOEM -Confirm:$false }
            Initialize-Disk -Number {{disk.Number}} -PartitionStyle MBR -ErrorAction SilentlyContinue
            $size = [Math]::Min(($d.Size - 8MB), 31GB)
            $p = New-Partition -DiskNumber {{disk.Number}} -Size $size -IsActive -AssignDriveLetter
            Format-Volume -Partition $p -FileSystem FAT32 -NewFileSystemLabel 'AUREVIX' -Confirm:$false | Out-Null
            (Get-Partition -DiskNumber {{disk.Number}} -PartitionNumber $p.PartitionNumber).DriveLetter
            """;
        var r = await Shell.PowerShellAsync(script, ct);
        var letter = r.Output.Split('\n').Last().Trim();
        if (!r.Ok || letter.Length != 1) throw new InvalidOperationException($"No se pudo preparar el USB: {r.Error}");
        var usb = letter + @":\";

        progress.Report(new Step("Copiando Windows 11 al USB", 0.06));
        var wim = Path.Combine(isoRoot, @"sources\install.wim");
        var expected = Directory.EnumerateFiles(isoRoot, "*", SearchOption.AllDirectories)
            .Where(f => !f.Equals(wim, StringComparison.OrdinalIgnoreCase)).Sum(f => new FileInfo(f).Length);
        r = await RunWithCopyProgressAsync("robocopy.exe", $"\"{isoRoot}.\" \"{usb}.\" /E /XF install.wim /R:2 /W:2 /NFL /NDL /NJH /NJS /NP",
            usb, expected, progress, "Copiando Windows 11 al USB", 0.06, 0.55, ct);
        if (r.ExitCode >= 8) throw new InvalidOperationException("Falló la copia de archivos al USB.");

        if (File.Exists(wim))
        {
            // FAT32 no admite archivos de más de 4 GB: DISM divide la imagen sin modificarla (install.swm). Windows Setup la admite.
            progress.Report(new Step("Preparando la imagen de Windows para el USB", 0.55));
            r = await Shell.RunAsync("dism.exe", $"/Split-Image /ImageFile:\"{wim}\" /SWMFile:\"{usb}sources\\install.swm\" /FileSize:3800", ct, line =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"(\d{1,3})(?:[.,]\d)?%");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var pct))
                    progress.Report(new Step("Preparando la imagen de Windows para el USB", 0.55 + 0.35 * Math.Clamp(pct, 0, 100) / 100.0, $"{pct} %"));
            });
            if (!r.Ok) throw new InvalidOperationException("DISM no pudo dividir la imagen de Windows.");
        }

        progress.Report(new Step("Añadiendo AUREVIX y la instalación automática en español", 0.92));
        AddAurevixFiles(usb, aurevixSetup);

        // Arranque BIOS (equipos antiguos). Los equipos UEFI arrancan desde \efi\boot.
        var bootsect = Path.Combine(isoRoot, @"boot\bootsect.exe");
        if (File.Exists(bootsect)) await Shell.RunAsync(bootsect, $"/nt60 {letter}: /mbr", ct);

        progress.Report(new Step("Comprobando que el USB está completo", 0.97));
        VerifyUsb(usb, wim, aurevixSetup);
        progress.Report(new Step("USB listo.", 1));
    }

    /// <summary>Comprueba que el USB tiene todo lo necesario para arrancar e instalar (archivos de arranque, imagen completa y AUREVIX).</summary>
    private static void VerifyUsb(string usb, string sourceWim, string aurevixSetup)
    {
        foreach (var f in new[] { "setup.exe", "autounattend.xml", @"sources\boot.wim", @"efi\boot\bootx64.efi", @"sources\$OEM$\$1\AUREVIX\AurevixSetup.exe" })
            if (!File.Exists(Path.Combine(usb, f)))
                throw new InvalidOperationException($"Falta {f} en el USB. Vuelve a crearlo; si se repite, prueba otro pendrive.");
        if (new FileInfo(Path.Combine(usb, @"sources\$OEM$\$1\AUREVIX\AurevixSetup.exe")).Length != new FileInfo(aurevixSetup).Length)
            throw new InvalidOperationException("El instalador de AUREVIX no se copió entero al USB. Prueba otro pendrive.");
        if (File.Exists(sourceWim))
        {
            var parts = Directory.GetFiles(Path.Combine(usb, "sources"), "install*.swm");
            var sum = parts.Sum(f => new FileInfo(f).Length);
            if (parts.Length == 0 || sum < new FileInfo(sourceWim).Length * 0.98)
                throw new InvalidOperationException("La imagen de Windows del USB está incompleta. Prueba otro pendrive.");
        }
    }

    /// <summary>Ejecuta una copia larga y va midiendo cuánto ha crecido el destino para dar progreso real.</summary>
    private static async Task<ShellResult> RunWithCopyProgressAsync(string file, string args, string destRoot, long expected,
        IProgress<Step> progress, string text, double from, double to, CancellationToken ct)
    {
        var run = Shell.RunAsync(file, args, ct);
        var drive = new DriveInfo(destRoot);
        var baseUsed = drive.TotalSize - drive.AvailableFreeSpace;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long lastBytes = 0; var lastMs = 0L; double speed = 0;
        while (!run.IsCompleted)
        {
            await Task.WhenAny(run, Task.Delay(700, ct));
            var used = Math.Max(0, drive.TotalSize - drive.AvailableFreeSpace - baseUsed);
            var secs = (sw.ElapsedMilliseconds - lastMs) / 1000.0;
            if (secs > 0) { var inst = (used - lastBytes) / secs; speed = speed == 0 ? inst : speed * 0.7 + inst * 0.3; }
            lastBytes = used; lastMs = sw.ElapsedMilliseconds;
            var f = expected > 0 ? Math.Clamp((double)used / expected, 0, 1) : 0;
            progress.Report(new Step(text, from + (to - from) * f, $"{Eta.Bytes(used)} de {Eta.Bytes(expected)}" + (speed > 0 ? $" · {Eta.Speed(speed)}" : "")));
        }
        return await run;
    }

    // ------------------------------------------------------------------ Espacio y caché

    public const long WindowsIsoBytes = 8_000_000_000;

    public bool IsoCached => File.Exists(IsoCachePath) && new FileInfo(IsoCachePath).Length > 3_000_000_000;

    /// <summary>Lanza un error claro, antes de empezar, si la unidad de <paramref name="path"/> no tiene sitio.</summary>
    public static void RequireFreeSpace(string path, long bytes, string what)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))!;
        var free = new DriveInfo(root).AvailableFreeSpace;
        if (free < bytes)
            throw new InvalidOperationException($"Falta espacio en {root}: {what} necesita unos {bytes / 1e9:0} GB libres y hay {free / 1e9:0.0} GB. Libera espacio y vuelve a intentarlo.");
    }

    public static long CacheBytes()
    {
        try { return Directory.Exists(WorkRoot) ? Directory.EnumerateFiles(WorkRoot, "*", SearchOption.AllDirectories).Where(f => !f.Contains(@"\logs\")).Sum(f => new FileInfo(f).Length) : 0; }
        catch { return 0; }
    }

    /// <summary>Borra la ISO de Windows descargada y los archivos temporales (no el registro).</summary>
    public static void ClearCache()
    {
        foreach (var f in new[] { "Win11_Oficial_Espanol_x64.iso", "Win11_Oficial_Espanol_x64.iso.partial", "Fido.ps1" })
            try { File.Delete(Path.Combine(WorkRoot, f)); } catch { }
        foreach (var d in new[] { "aurevix", "iso-overlay", "iso-work" })
            try { Directory.Delete(Path.Combine(WorkRoot, d), true); } catch { }
    }

    // ------------------------------------------------------------------ ISO

    /// <summary>
    /// Crea la ISO de AUREVIX OS leyendo Windows directamente de la ISO montada (sin copiarlo antes) y añadiendo
    /// los archivos de AUREVIX encima. Devuelve el SHA-256 de la ISO resultante.
    /// </summary>
    public async Task<string> BuildIsoAsync(string isoRoot, string aurevixSetup, string outputIso, IProgress<Step> progress, CancellationToken ct)
    {
        var overlay = Path.Combine(WorkRoot, "iso-overlay");
        if (Directory.Exists(overlay)) Directory.Delete(overlay, true);
        Directory.CreateDirectory(overlay);
        try
        {
            progress.Report(new Step("Añadiendo AUREVIX", 0.01));
            AddAurevixFiles(overlay, aurevixSetup);
            if (File.Exists(outputIso)) File.Delete(outputIso);

            var imapi = new Progress<(long done, long total)>(p =>
            {
                var f = p.total > 0 ? Math.Clamp((double)p.done / p.total, 0, 1) : 0;
                progress.Report(new Step("Generando la ISO de AUREVIX OS", 0.02 + 0.88 * f, $"{Eta.Bytes(p.done)} de {Eta.Bytes(p.total)}"));
            });
            await ImapiIsoWriter.BuildAsync(isoRoot, overlay, outputIso, "AUREVIX_OS",
                Path.Combine(isoRoot, @"boot\etfsboot.com"), Path.Combine(isoRoot, @"efi\microsoft\boot\efisys.bin"), imapi, ct);

            if (!File.Exists(outputIso) || new FileInfo(outputIso).Length < 1_000_000_000)
                throw new InvalidOperationException("La ISO generada no es válida. Prueba con la opción de USB o vuelve a intentarlo.");

            var hash = await HashFileAsync(outputIso, new Progress<double>(f =>
                progress.Report(new Step("Verificando la ISO (SHA-256)", 0.9 + 0.1 * f))), ct);
            await File.WriteAllTextAsync(outputIso + ".sha256", $"{hash}  {Path.GetFileName(outputIso)}", ct);
            progress.Report(new Step("ISO lista.", 1));
            return hash;
        }
        finally
        {
            try { Directory.Delete(overlay, true); } catch { }
        }
    }

    private static async Task<string> HashFileAsync(string path, IProgress<double> progress, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var sha = SHA256.Create();
        var buf = new byte[4 << 20];
        long done = 0; int n; var last = Environment.TickCount64;
        while ((n = await fs.ReadAsync(buf, ct)) > 0)
        {
            sha.TransformBlock(buf, 0, n, null, 0);
            done += n;
            if (Environment.TickCount64 - last > 400) { last = Environment.TickCount64; progress.Report((double)done / fs.Length); }
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    // ------------------------------------------------------------------ Reinstalación limpia

    public sealed record Precheck(List<string> Blockers, List<string> Warnings);

    public static async Task<Precheck> CheckCleanInstallAsync()
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        var sys = Path.GetPathRoot(Environment.SystemDirectory)!;
        var free = new DriveInfo(sys).AvailableFreeSpace;
        if (free < 25_000_000_000) blockers.Add($"Hacen falta al menos 25 GB libres en {sys} (hay {free / 1e9:0} GB).");
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        if (power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline)
            blockers.Add("Conecta el portátil al cargador antes de reinstalar.");
        var bl = await Shell.PowerShellAsync("(Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction SilentlyContinue).ProtectionStatus");
        if (bl.Output.Trim() == "On")
            warnings.Add("BitLocker está activado. Asegúrate de tener tu clave de recuperación (aka.ms/myrecoverykey) antes de continuar.");
        return new Precheck(blockers, warnings);
    }

    /// <summary>
    /// Reinstala Windows limpio con el Windows Setup OFICIAL:
    ///   setup.exe /auto clean /unattend (español) /postoobe (instala AUREVIX).
    /// Borra programas, ajustes y archivos de la unidad del sistema. El equipo se reinicia solo.
    /// </summary>
    public async Task StartCleanInstallAsync(string isoRoot, string aurevixSetup, IProgress<Step> progress, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "AUREVIX-Setup");
        var post = Path.Combine(dir, "postoobe");
        Directory.CreateDirectory(post);
        File.WriteAllText(Path.Combine(post, "SetupComplete.cmd"), Resource("SetupComplete.cmd"));
        File.Copy(aurevixSetup, Path.Combine(post, "AurevixSetup.exe"), overwrite: true);
        var unattend = Path.Combine(dir, "unattend.xml");
        File.WriteAllText(unattend, Unattend());

        progress.Report(new Step("Iniciando Windows Setup. El equipo se reiniciará solo varias veces…"));
        var r = await Shell.RunAsync(Path.Combine(isoRoot, "setup.exe"),
            $"/auto clean /dynamicupdate enable /unattend:\"{unattend}\" /postoobe \"{post}\" /copylogs \"{Path.Combine(dir, "logs")}\"", ct);
        // setup.exe devuelve el control cuando arranca su propia interfaz o si falla de inmediato.
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"Windows Setup no pudo iniciar la reinstalación (código 0x{r.ExitCode:X8}). " +
                                                "Puedes usar la opción de crear un USB.");
    }
}

public sealed class MicrosoftBlockedException(string detail)
    : Exception("Microsoft no ha entregado el enlace de descarga (a veces limita las descargas automáticas durante unas horas).")
{
    public string Detail { get; } = detail;
}
