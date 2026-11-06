using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Aurevix.Core;
using Aurevix.Core.Logging;
using Aurevix.Update;

namespace Aurevix.Installer.Engine;

public sealed record UsbDisk(int Number, string Name, long SizeBytes)
{
    public string Display => $"{Name} · {SizeBytes / 1e9:0.#} GB";
}

public sealed record Step(string Text, double? Fraction = null);

/// <summary>
/// Motor del AUREVIX Installer:
///   - Descarga la ISO oficial de Windows 11 desde Microsoft (enlace obtenido con Fido).
///   - Descarga el instalador de AUREVIX verificado (manifiesto firmado + SHA-256).
///   - Prepara un USB arrancable (FAT32 + install.wim dividido) o una ISO (oscdimg).
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
            progress.Report(new Step("Usando la ISO de Windows 11 ya descargada."));
            return IsoCachePath;
        }
        progress.Report(new Step("Pidiendo a Microsoft la ISO oficial de Windows 11…"));
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
                var buf = new byte[1 << 20];
                long done = have; int n; var last = DateTime.MinValue;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 250 && total > 0)
                    {
                        last = DateTime.UtcNow;
                        progress.Report(new Step($"Descargando Windows 11 de Microsoft… {done / 1e9:0.0} de {total / 1e9:0.0} GB", (double)done / total));
                    }
                }
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
            "@(Get-Disk | Where-Object { $_.BusType -eq 'USB' -and -not $_.IsBoot -and -not $_.IsSystem -and $_.Size -gt 0 } | " +
            "Select-Object Number, FriendlyName, Size) | ConvertTo-Json -Compress", ct);
        if (!r.Ok || string.IsNullOrWhiteSpace(r.Output)) return [];
        using var doc = JsonDocument.Parse(r.Output);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        return items.Select(e => new UsbDisk(e.GetProperty("Number").GetInt32(), e.GetProperty("FriendlyName").GetString() ?? "USB", e.GetProperty("Size").GetInt64())).ToList();
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

        progress.Report(new Step("Copiando Windows 11 al USB…", 0.1));
        r = await Shell.RunAsync("robocopy.exe", $"\"{isoRoot}.\" \"{usb}.\" /E /XF install.wim /R:2 /W:2 /NFL /NDL /NJH /NJS /NP", ct);
        if (r.ExitCode >= 8) throw new InvalidOperationException("Falló la copia de archivos al USB.");

        var wim = Path.Combine(isoRoot, @"sources\install.wim");
        if (File.Exists(wim))
        {
            // FAT32 no admite archivos de más de 4 GB: DISM divide la imagen sin modificarla (install.swm). Windows Setup la admite.
            progress.Report(new Step("Dividiendo la imagen de Windows para FAT32 (DISM)…", 0.55));
            r = await Shell.RunAsync("dism.exe", $"/Split-Image /ImageFile:\"{wim}\" /SWMFile:\"{usb}sources\\install.swm\" /FileSize:3800", ct);
            if (!r.Ok) throw new InvalidOperationException("DISM no pudo dividir la imagen de Windows.");
        }

        progress.Report(new Step("Añadiendo AUREVIX y la instalación automática en español…", 0.9));
        AddAurevixFiles(usb, aurevixSetup);

        // Arranque BIOS (equipos antiguos). Los equipos UEFI arrancan desde \efi\boot.
        var bootsect = Path.Combine(isoRoot, @"boot\bootsect.exe");
        if (File.Exists(bootsect)) await Shell.RunAsync(bootsect, $"/nt60 {letter}: /mbr", ct);

        progress.Report(new Step("USB listo.", 1));
    }

    // ------------------------------------------------------------------ ISO

    public static string? FindOscdimg()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var dir = Path.Combine(pf86, @"Windows Kits\10\Assessment and Deployment Kit\Deployment Tools");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "oscdimg.exe", SearchOption.AllDirectories)
            .FirstOrDefault(p => p.Contains("amd64", StringComparison.OrdinalIgnoreCase)) ??
               Directory.EnumerateFiles(dir, "oscdimg.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    public async Task<string> BuildIsoAsync(string isoRoot, string aurevixSetup, string outputIso, IProgress<Step> progress, CancellationToken ct)
    {
        // oscdimg (Windows ADK) si ya está instalado; si no, IMAPI2, el motor de ISOs que trae el propio Windows.
        var oscdimg = FindOscdimg();
        var work = Path.Combine(WorkRoot, "iso-work");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        try
        {
            progress.Report(new Step("Copiando Windows 11…", 0.1));
            var r = await Shell.RunAsync("robocopy.exe", $"\"{isoRoot}.\" \"{work}\" /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP", ct);
            if (r.ExitCode >= 8) throw new InvalidOperationException("Falló la copia de archivos.");
            await Shell.RunAsync("attrib.exe", $"-R \"{work}\\*\" /S /D", ct);

            progress.Report(new Step("Añadiendo AUREVIX…", 0.5));
            AddAurevixFiles(work, aurevixSetup);

            progress.Report(new Step("Generando la ISO de AUREVIX OS…", 0.6));
            var etfs = Path.Combine(work, @"boot\etfsboot.com");
            var efi = Path.Combine(work, @"efi\microsoft\boot\efisys.bin");
            if (File.Exists(outputIso)) File.Delete(outputIso);
            var built = false;
            if (oscdimg is not null)
            {
                r = await Shell.RunAsync(oscdimg, $"-m -o -u2 -udfver102 -lAUREVIX_OS -bootdata:2#p0,e,b\"{etfs}\"#pEF,e,b\"{efi}\" \"{work}\" \"{outputIso}\"", ct);
                built = r.Ok && File.Exists(outputIso);
            }
            if (!built)
            {
                var isoProgress = new Progress<double>(p => progress.Report(new Step($"Generando la ISO de AUREVIX OS… {p:P0}", 0.6 + 0.35 * p)));
                await ImapiIsoWriter.BuildAsync(work, outputIso, "AUREVIX_OS", etfs, efi, isoProgress, ct);
            }
            if (!File.Exists(outputIso) || new FileInfo(outputIso).Length < 1_000_000_000)
                throw new InvalidOperationException("La ISO generada no es válida. Prueba con la opción de USB o vuelve a intentarlo.");
            var hash = await UpdateClient.ComputeSha256Async(outputIso, ct);
            await File.WriteAllTextAsync(outputIso + ".sha256", $"{hash}  {Path.GetFileName(outputIso)}", ct);
            progress.Report(new Step("ISO lista.", 1));
            return hash;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
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
