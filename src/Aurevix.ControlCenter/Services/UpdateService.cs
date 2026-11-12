using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Aurevix.Core;
using Aurevix.Core.Logging;
using Aurevix.Update;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// Comprueba actualizaciones periódicamente (solo conexiones salientes) y avisa a la UI.
/// La instalación siempre requiere consentimiento del usuario.
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateClient _client;
    private System.Threading.Timer? _timer;
    private int _checking;

    public event Action<UpdateCheckResult>? UpdateAvailable;
    public UpdateCheckResult? LastResult { get; private set; }

    public UpdateService()
    {
        var cfg = UpdateEndpointConfig.Load();
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"AurevixControlCenter/{AppServices.CurrentVersion}");
        _client = new UpdateClient(new UpdateClientOptions
        {
            ManifestBaseUrl = new Uri(cfg.ManifestBaseUrl),
            AllowedPackageHosts = cfg.AllowedPackageHosts,
            TrustedKeys = ManifestSigner.LoadEmbeddedTrustedKeys()
        }, http);
    }

    /// <summary>Identificador aleatorio local, solo para el despliegue gradual. No identifica a la persona.</summary>
    public static string InstallId
    {
        get
        {
            var path = Path.Combine(AurevixPaths.UserRoot, "install-id");
            if (File.Exists(path)) return File.ReadAllText(path).Trim();
            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, id);
            return id;
        }
    }

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(2);
    private bool _firstDone;

    public void StartBackgroundChecks()
    {
        _timer?.Dispose();
        _timer = null;
        if (!AppServices.Settings.CheckUpdatesAutomatically) return;
        // Primer chequeo a los 45 s del inicio de sesión para no ralentizar el arranque.
        _timer = new System.Threading.Timer(OnTimer, null, _firstDone ? NextInterval() : FirstCheckDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Vuelve a leer los ajustes de actualización (activado e intervalo) y reprograma las comprobaciones.</summary>
    public void RestartChecks() => StartBackgroundChecks();

    /// <summary>Intervalo de los ajustes con una variación aleatoria de ±10 % para no sincronizar todos los equipos.</summary>
    private static TimeSpan NextInterval()
    {
        var hours = Math.Clamp(AppServices.Settings.UpdateCheckIntervalHours, 1, 24);
        return TimeSpan.FromHours(hours * (0.9 + Random.Shared.NextDouble() * 0.2));
    }

    private async void OnTimer(object? _)
    {
        var next = NextInterval();
        try
        {
            var r = await CheckAsync(notify: true);
            // Si la primera comprobación falla (p. ej. aún sin red), se reintenta pronto.
            if (!_firstDone && r.Status == UpdateStatus.Error) next = RetryDelay;
            else _firstDone = true;
        }
        catch (Exception ex)
        {
            AurevixLog.Warn($"Fallo en la comprobación periódica de actualizaciones: {ex.Message}");
            if (!_firstDone) next = RetryDelay;
        }
        try
        {
            // Si se desactivó o se reprogramó mientras tanto, el temporizador ya no es el mismo.
            if (AppServices.Settings.CheckUpdatesAutomatically) _timer?.Change(next, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException) { }
    }

    public async Task<UpdateCheckResult> CheckAsync(bool notify)
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return LastResult ?? new(UpdateStatus.Error, null, null, "Comprobación en curso.");
        try
        {
            var state = AppServices.State;
            // La secuencia vista se guarda por canal: cambiar de canal no debe verse como una bajada de versión.
            var channel = AppServices.Settings.UpdateChannel;
            state.ManifestSequenceByChannel.TryGetValue(channel, out var lastSeq);
            var result = await _client.CheckAsync(AppServices.CurrentVersion, channel, InstallId, lastSeq);
            LastResult = result;
            state.LastUpdateCheckUtc = DateTime.UtcNow;
            if (result.Manifest is { } m && result.Status != UpdateStatus.Error && m.Sequence > lastSeq)
                state.ManifestSequenceByChannel[channel] = m.Sequence;
            AppServices.SaveState();
            AurevixLog.Info($"Comprobación de actualizaciones: {result.Status} — {result.Message}");

            if (notify && result.Status == UpdateStatus.Available && result.Release!.Version != state.SkippedVersion)
                UpdateAvailable?.Invoke(result);
            return result;
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    /// <summary>Descarga y verifica todos los paquetes; devuelve la ruta del instalador.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct)
    {
        var dir = Path.Combine(AurevixPaths.Downloads, release.Version);
        string? installer = null;
        foreach (var p in release.Packages)
        {
            var path = await _client.DownloadAsync(p, dir, progress, ct);
            if (p.Kind == PackageKind.AppInstaller) installer = path;
        }
        return installer ?? throw new InvalidOperationException("La versión no incluye instalador de la aplicación.");
    }

    /// <summary>
    /// Ejecuta el instalador verificado con argumentos FIJOS (nunca procedentes del servidor).
    /// Solo se usa si AUREVIX Core Service no está disponible (pide UAC). El instalador cierra y reinicia el Control Center.
    /// </summary>
    public void LaunchInstaller(string installerPath, ReleaseInfo release)
    {
        var history = UpdateHistory.Load();
        history.Add(new UpdateHistoryEntry(DateTime.UtcNow, AppServices.CurrentVersion.ToString(), release.Version,
            AppServices.Settings.UpdateChannel, "launched", installerPath));
        history.Save();

        AurevixLog.Info($"Lanzando instalador {release.Version}");
        Process.Start(new ProcessStartInfo(installerPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /AUREVIXUPDATE")
        {
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    /// <summary>Instalador anterior conservado para volver a la versión previa (rollback manual).</summary>
    public static string? FindPreviousInstaller()
    {
        // Historial del usuario (instalación local) + el del servicio (%ProgramData%\AUREVIX\update-history.json),
        // ordenados por fecha: el instalador de la versión actual es el último; el anterior, el penúltimo.
        var machine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AUREVIX", "update-history.json");
        var entries = UpdateHistory.Load().Entries
            .Concat(File.Exists(machine) ? JsonStore.LoadOrDefault<UpdateHistory>(machine).Entries : new List<UpdateHistoryEntry>())
            .OrderBy(e => e.AtUtc).ToList();
        return entries.Count >= 2 && File.Exists(entries[^2].InstallerBackupPath) ? entries[^2].InstallerBackupPath : null;
    }
}
