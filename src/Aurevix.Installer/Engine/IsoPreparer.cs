using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurevix.Core.Logging;

namespace Aurevix.Installer.Engine;

public enum PrepState { Idle, Running, Paused, Ready, NoSpace, Failed, Metered }

/// <summary>Datos de la ISO preparada: versión de AUREVIX con la que se hizo, fecha, tamaño y SHA-256.</summary>
public sealed class PreparedInfo
{
    public string AurevixVersion { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>
/// Prepara la ISO de AUREVIX OS en segundo plano nada más abrir el Installer (si hay sitio y el usuario no lo ha desactivado):
/// descarga AUREVIX y Windows (reanudable) y construye la ISO en <c>WorkRoot\prepared</c>. Si se cierra el Installer,
/// la descarga continúa donde se quedó y la construcción se repite si no había terminado.
/// </summary>
public sealed class IsoPreparer
{
    public static IsoPreparer Instance { get; } = new();

    /// <summary>Espacio libre mínimo (contando lo ya descargado) para empezar sola.</summary>
    public const long MinFreeBytes = 18_000_000_000;

    private readonly object _gate = new();
    private readonly MediaEngine _engine = new();
    private CancellationTokenSource? _cts;
    private Task? _task;
    private bool _userPaused;
    private volatile bool _optimisticReady;
    private volatile bool _verified; // la versión de la ISO preparada ya se comprobó en esta sesión
    private volatile bool _shuttingDown;
    private volatile bool _held;          // tras «Liberar espacio» no se relanza sola hasta que el usuario lo pida
    private volatile bool _allowMetered;  // el usuario ha aceptado preparar con una conexión de uso medido
    private bool _restartQueued;
    private volatile string? _mountedIso; // ISO de Windows montada ahora mismo por la preparación (para desmontarla al salir)

    private PrepState _state = PrepState.Idle;
    private double _fraction;
    private string _detail = "";

    /// <summary>Se dispara (desde cualquier hilo) cada vez que cambia el estado o el avance.</summary>
    public event Action? Changed;

    public PrepState State { get { lock (_gate) return _state; } }
    public double Fraction { get { lock (_gate) return _fraction; } }
    public string Detail { get { lock (_gate) return _detail; } }

    public static string PreparedDir => Path.Combine(MediaEngine.WorkRoot, "prepared");
    public static string IsoPath => Path.Combine(PreparedDir, "AUREVIX_OS.iso");
    private static string BuildingPath => Path.Combine(PreparedDir, "AUREVIX_OS.building.iso");
    private static string ShaPath => IsoPath + ".sha256";
    private static string MetaPath => Path.Combine(PreparedDir, "AUREVIX_OS.json");
    private static string SettingsPath => Path.Combine(MediaEngine.WorkRoot, "preparacion.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ------------------------------------------------------------------ Ajuste

    private sealed class Settings
    {
        [JsonPropertyName("auto")] public bool? Auto { get; set; }
        [JsonPropertyName("LastBuiltVersion")] public string? LastBuiltVersion { get; set; }
    }

    private static readonly object SettingsLock = new();

    private static Settings ReadSettings()
    {
        lock (SettingsLock)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new Settings();
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new Settings(); }
        }
    }

    private static void UpdateSettings(Action<Settings> change)
    {
        lock (SettingsLock)
        {
            var st = ReadSettings();
            change(st);
            try
            {
                Directory.CreateDirectory(MediaEngine.WorkRoot);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(st));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AurevixLog.Warn($"No se pudo guardar el ajuste de preparación: {ex.Message}"); }
        }
    }

    /// <summary>Elección del usuario: true = preparar, false = no, null = aún no ha decidido (no se descarga nada).</summary>
    public bool? AutoSetting => ReadSettings().Auto;

    public bool AutoDecided => AutoSetting is not null;

    /// <summary>Casilla «Preparar la ISO automáticamente» (se guarda en un JSON en la carpeta de trabajo). Sin decidir equivale a no.</summary>
    public bool AutoEnabled
    {
        get => AutoSetting == true;
        set
        {
            UpdateSettings(st => st.Auto = value);
            if (value) { _userPaused = false; _held = false; Start(); }
            else
            {
                _userPaused = false; // si estaba en pausa, al desactivar no debe quedar «En pausa»
                Cancel();
                SetState(PrepState.Idle, 0, "");
            }
        }
    }

    /// <summary>Última versión de AUREVIX con la que el usuario ya creó una ISO: no se vuelve a preparar otra igual.</summary>
    public static string? LastBuiltVersion => ReadSettings().LastBuiltVersion;

    public static void RecordBuilt(string? aurevixVersion)
    {
        if (string.IsNullOrEmpty(aurevixVersion)) return;
        UpdateSettings(st => st.LastBuiltVersion = aurevixVersion);
    }

    /// <summary>¿La conexión actual es de uso medido (datos limitados)? Ante la duda, false.</summary>
    public static bool IsMetered()
    {
        try
        {
            var cost = global::Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost();
            return cost is not null && cost.NetworkCostType != global::Windows.Networking.Connectivity.NetworkCostType.Unrestricted;
        }
        catch { return false; }
    }

    /// <summary>El usuario acepta preparar la ISO aunque la conexión sea de uso medido.</summary>
    public void PrepareAnyway()
    {
        _allowMetered = true;
        Resume();
    }

    // ------------------------------------------------------------------ Estado de la ISO preparada

    public bool IsBusy { get { lock (_gate) return _task is { IsCompleted: false }; } }

    public static PreparedInfo? ReadMeta()
    {
        try
        {
            if (!File.Exists(MetaPath) || !File.Exists(IsoPath)) return null;
            var meta = JsonSerializer.Deserialize<PreparedInfo>(File.ReadAllText(MetaPath));
            return meta is not null && meta.Size > 0 && new FileInfo(IsoPath).Length == meta.Size && meta.Sha256.Length == 64 ? meta : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static bool HasPrepared => ReadMeta() is not null;

    /// <summary>¿Hay una ISO preparada completa y de esta versión de AUREVIX?</summary>
    public static bool IsReadyFor(string? aurevixVersion) =>
        !string.IsNullOrEmpty(aurevixVersion) && ReadMeta() is { } m && m.AurevixVersion == aurevixVersion;

    /// <summary>Descarta la ISO preparada (la próxima vez se prepara de nuevo).</summary>
    public static void Discard()
    {
        foreach (var f in new[] { MetaPath, ShaPath, IsoPath, BuildingPath })
            try { File.Delete(f); } catch { }
    }

    // ------------------------------------------------------------------ Control

    /// <summary>Empieza (o continúa) la preparación si procede. No bloquea y se puede llamar las veces que haga falta.</summary>
    public void Start()
    {
        if (_shuttingDown || _held || _userPaused || !AutoEnabled) return;
        lock (_gate)
        {
            if (_task is { IsCompleted: false } running)
            {
                // Pausar y reanudar enseguida: la tarea anterior aún se está cancelando; se relanza cuando termine.
                if (_cts is { IsCancellationRequested: true } && !_restartQueued)
                {
                    _restartQueued = true;
                    _ = running.ContinueWith(_ => { lock (_gate) _restartQueued = false; Start(); }, TaskScheduler.Default);
                }
                return;
            }
            if (_verified && ReadMeta() is not null) return;
            var ready = ReadMeta() is not null;
            if (!ready)
            {
                // Lo ya descargado de Windows cuenta como espacio disponible: no se vuelve a pedir.
                if (!_allowMetered && IsMetered())
                {
                    SetStateLocked(PrepState.Metered, 0, "Tu conexión es de uso medido. No se descarga nada hasta que lo confirmes.");
                    return;
                }
                var free = MediaEngine.FreeBytes(MediaEngine.WorkRoot);
                var have = FileLength(_engine.IsoCachePath) + FileLength(_engine.IsoCachePath + ".partial");
                if (free >= 0 && free + have < MinFreeBytes)
                {
                    SetStateLocked(PrepState.NoSpace, 0, $"Hacen falta unos {MinFreeBytes / 1_000_000_000} GB libres en {Path.GetPathRoot(MediaEngine.WorkRoot)}");
                    return;
                }
            }
            _optimisticReady = ready;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            if (ready) SetStateLocked(PrepState.Ready, 1, "");
            else SetStateLocked(PrepState.Running, _fraction, "Empezando…");
            _task = Task.Run(() => RunAsync(ct));
        }
    }

    /// <summary>Pausa elegida por el usuario: no se reanuda sola hasta que pulse «Continuar».</summary>
    public void Pause()
    {
        _userPaused = true;
        Cancel();
    }

    public void Resume()
    {
        _userPaused = false;
        _held = false;
        Start();
    }

    /// <summary>Reintento tras un fallo o falta de espacio.</summary>
    public void Retry() => Resume();

    /// <summary>Detiene la preparación (sin marcarla como pausada por el usuario) y espera a que termine. Se usa antes de otras operaciones.</summary>
    public async Task SuspendAsync()
    {
        Task? t;
        lock (_gate) t = _task;
        Cancel();
        if (t is not null) { try { await t; } catch { } }
    }

    /// <summary>Detiene la preparación y evita que se relance sola (hasta que el usuario la pida). Se usa tras «Liberar espacio».</summary>
    public async Task HoldAsync()
    {
        _held = true;
        await SuspendAsync();
        SetState(PrepState.Idle, 0, "");
    }

    /// <summary>Al cerrar la ventana: cancela y espera un instante para que quede guardado el avance de la descarga.</summary>
    public void Shutdown()
    {
        _shuttingDown = true;
        Task? t;
        lock (_gate) t = _task;
        Cancel();
        // Si hay una ISO de Windows montada (construcción en curso) se da más margen para que se desmonte sola.
        try { t?.Wait(_mountedIso is not null ? 15000 : 2500); } catch { }
        var mounted = _mountedIso;
        if (mounted is not null)
        {
            // No bastó: se desmonta de forma síncrona para no dejar la ISO montada tras cerrar.
            try { Task.Run(() => Shell.PowerShellAsync($"Dismount-DiskImage -ImagePath {Shell.Quote(mounted)} | Out-Null")).Wait(10000); } catch { }
            _mountedIso = null;
        }
    }

    private void Cancel()
    {
        CancellationTokenSource? c;
        lock (_gate) c = _cts;
        try { c?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Espera a que termine la preparación en marcha, informando de su avance. Cancelar aquí no la detiene.</summary>
    public async Task WaitAsync(IProgress<Step> progress, CancellationToken ct)
    {
        while (true)
        {
            Task? t;
            lock (_gate) t = _task;
            if (t is null || t.IsCompleted) return;
            progress.Report(new Step("Preparando la ISO de AUREVIX en segundo plano", Fraction, Detail));
            await Task.WhenAny(t, Task.Delay(300, ct));
            ct.ThrowIfCancellationRequested();
        }
    }

    // ------------------------------------------------------------------ Trabajo

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(PreparedDir);

            // 1) AUREVIX (pequeño): da la versión actual.
            var setup = await _engine.DownloadAurevixAsync(new DirectProgress<Step>(s => { if (!_optimisticReady) OnStep(s, 0, 0.03); }), ct);
            var version = _engine.AurevixVersion ?? "";
            ct.ThrowIfCancellationRequested();

            var meta = ReadMeta();
            if (meta is not null && meta.AurevixVersion == version)
            {
                _optimisticReady = false;
                _verified = true;
                SetState(PrepState.Ready, 1, "");
                AurevixLog.Info($"ISO preparada ya lista (AUREVIX {version}).");
                return;
            }
            if (version.Length > 0 && LastBuiltVersion == version && meta is null && !File.Exists(IsoPath))
            {
                // El usuario ya creó una ISO con esta versión: no se prepara otra igual hasta que salga una nueva.
                _optimisticReady = false;
                SetState(PrepState.Idle, 0, "");
                AurevixLog.Info($"Ya se creó una ISO con AUREVIX {version}: no se prepara otra hasta que haya una versión nueva.");
                return;
            }
            if (_optimisticReady || File.Exists(IsoPath) || File.Exists(MetaPath))
            {
                AurevixLog.Info("La ISO preparada es de otra versión de AUREVIX (o está incompleta): se descarta y se prepara de nuevo.");
                Discard();
            }
            _optimisticReady = false;
            SetState(PrepState.Running, 0.03, "");

            // 2) Windows (reanudable).
            var isoWin = await _engine.DownloadWindowsIsoAsync(new DirectProgress<Step>(s => OnStep(s, 0.03, 0.60)), ct);

            // 3) Construir la ISO (si no termina, se repite la próxima vez).
            SetState(PrepState.Running, 0.63, "Montando la ISO de Windows");
            try { File.Delete(BuildingPath); } catch { }
            string hash;
            _mountedIso = isoWin;
            try
            {
                var root = await _engine.MountAsync(isoWin, ct);
                hash = await _engine.BuildIsoAsync(root, setup, BuildingPath, new DirectProgress<Step>(s => OnStep(s, 0.63, 0.37)), ct);
            }
            finally
            {
                // Se desmonta siempre, aunque se cancele o falle la comprobación de la ISO.
                try { await _engine.DismountAsync(isoWin); } catch { }
                _mountedIso = null;
            }

            // Desde aquí no se cancela: así una ISO ya construida nunca se queda sin sus metadatos.
            ct.ThrowIfCancellationRequested();
            File.Move(BuildingPath, IsoPath, overwrite: true);
            try { File.Delete(BuildingPath + ".sha256"); } catch { }
            await File.WriteAllTextAsync(ShaPath, $"{hash}  {Path.GetFileName(IsoPath)}", CancellationToken.None);
            var info = new PreparedInfo { AurevixVersion = version, CreatedUtc = DateTime.UtcNow, Size = new FileInfo(IsoPath).Length, Sha256 = hash };
            // El JSON se escribe el último: si existe, la ISO está completa.
            await File.WriteAllTextAsync(MetaPath, JsonSerializer.Serialize(info, JsonOpts), CancellationToken.None);
            _verified = true;
            SetState(PrepState.Ready, 1, "");
            AurevixLog.Info($"ISO preparada en segundo plano: {IsoPath}");
        }
        catch (OperationCanceledException)
        {
            try { File.Delete(BuildingPath); } catch { }
            var keepReady = _optimisticReady && ReadMeta() is not null;
            _optimisticReady = false;
            SetState(keepReady ? PrepState.Ready : _userPaused ? PrepState.Paused : PrepState.Idle, keepReady ? 1 : Fraction, "");
        }
        catch (Exception ex)
        {
            AurevixLog.Error("Preparación de la ISO en segundo plano", ex);
            var keepReady = _optimisticReady && ReadMeta() is not null; // sin red no se pudo comprobar la versión: se conserva
            _optimisticReady = false;
            SetState(keepReady ? PrepState.Ready : PrepState.Failed, keepReady ? 1 : Fraction,
                ex is MicrosoftBlockedException ? "Microsoft ha limitado la descarga; se reintentará más tarde." : ex.Message);
        }
    }

    /// <summary>Traduce el avance de una fase al avance global (<paramref name="from"/> + <paramref name="span"/> × fase).</summary>
    private void OnStep(Step s, double from, double span)
    {
        var f = s.Fraction is { } x ? from + span * Math.Clamp(x, 0, 1) : Fraction;
        var text = string.IsNullOrWhiteSpace(s.Detail) ? s.Text : s.Detail;
        SetState(PrepState.Running, Math.Max(f, from), text);
    }

    private void OnStep(Step s) => OnStep(s, 0, 1);

    private void SetState(PrepState state, double fraction, string detail)
    {
        lock (_gate) SetStateLocked(state, fraction, detail, raise: false);
        Changed?.Invoke();
    }

    private void SetStateLocked(PrepState state, double fraction, string detail, bool raise = true)
    {
        _state = state; _fraction = fraction; _detail = detail;
        if (raise) { var h = Changed; if (h is not null) Task.Run(h); } // fuera del lock del llamador
    }

    private static long FileLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; } catch (IOException) { return 0; }
    }

    // ------------------------------------------------------------------ Entrega al usuario

    /// <summary>
    /// Copia (o mueve) la ISO preparada a <paramref name="output"/> y comprueba su SHA-256 con el guardado.
    /// Devuelve el SHA-256. Si no coincide, descarta la ISO preparada y avisa.
    /// </summary>
    /// <summary>
    /// ¿Están los dos sitios en el mismo volumen real (no UNC, no unidad subst, ni carpeta montada en otro disco)?
    /// Ante cualquier duda devuelve false: entonces se copia con verificación en vez de mover.
    /// </summary>
    public static bool IsSameVolume(string a, string b)
    {
        try
        {
            var ra = Path.GetPathRoot(Path.GetFullPath(a));
            var rb = Path.GetPathRoot(Path.GetFullPath(b));
            if (string.IsNullOrEmpty(ra) || string.IsNullOrEmpty(rb) || ra.StartsWith(@"\\") || rb.StartsWith(@"\\")) return false;
            if (!string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase)) return false;
            var va = VolumeGuid(Path.GetDirectoryName(Path.GetFullPath(a)) ?? ra);
            var vb = VolumeGuid(Path.GetDirectoryName(Path.GetFullPath(b)) ?? rb);
            return va is not null && string.Equals(va, vb, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>GUID del volumen que contiene la ruta (null si no se puede saber: unidades subst o de red).</summary>
    private static string? VolumeGuid(string path)
    {
        var mount = new StringBuilder(1024);
        if (!GetVolumePathName(path.EndsWith('\\') ? path : path + "\\", mount, (uint)mount.Capacity)) return null;
        var name = new StringBuilder(64);
        return GetVolumeNameForVolumeMountPoint(mount.ToString(), name, (uint)name.Capacity) ? name.ToString() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumePathName(string lpszFileName, StringBuilder lpszVolumePathName, uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string lpszVolumeMountPoint, StringBuilder lpszVolumeName, uint cchBufferLength);

    public async Task<string> DeliverAsync(string output, bool move, IProgress<Step> progress, CancellationToken ct)
    {
        var meta = ReadMeta() ?? throw new InvalidOperationException("La ISO preparada ya no está disponible.");
        var temp = output + ".partial";
        try { File.Delete(temp); } catch { }
        try
        {
            if (move)
            {
                // Misma unidad: primero se comprueba la ISO preparada y luego se mueve (es instantáneo).
                var h = await MediaEngine.HashFileAsync(IsoPath, new Progress<double>(f =>
                    progress.Report(new Step("Verificando la ISO (SHA-256)", 0.9 * f))), ct);
                if (!string.Equals(h, meta.Sha256, StringComparison.OrdinalIgnoreCase)) { Discard(); throw new InvalidOperationException(DamagedMessage); }
                File.Move(IsoPath, output, overwrite: true);
                await File.WriteAllTextAsync(output + ".sha256", $"{h}  {Path.GetFileName(output)}", CancellationToken.None);
                try { File.Delete(MetaPath); } catch { }
                try { File.Delete(ShaPath); } catch { }
                SetState(PrepState.Idle, 0, "");
                progress.Report(new Step("ISO lista.", 1));
                return h;
            }

            await using (var src = new FileStream(IsoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan | FileOptions.Asynchronous))
            await using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.Asynchronous))
            {
                var buf = new byte[4 << 20];
                long done = 0; int n; var last = Environment.TickCount64;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (Environment.TickCount64 - last > 300)
                    {
                        last = Environment.TickCount64;
                        var speed = done / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                        progress.Report(new Step("Copiando la ISO ya preparada", 0.6 * done / Math.Max(1, src.Length),
                            $"{Eta.Bytes(done)} de {Eta.Bytes(src.Length)} · {Eta.Speed(speed)}"));
                    }
                }
            }

            var hash = await MediaEngine.HashFileAsync(temp, new Progress<double>(f =>
                progress.Report(new Step("Verificando la ISO (SHA-256)", 0.6 + 0.4 * f))), ct);
            if (!string.Equals(hash, meta.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Discard();
                throw new InvalidOperationException(DamagedMessage);
            }
            File.Move(temp, output, overwrite: true);
            await File.WriteAllTextAsync(output + ".sha256", $"{hash}  {Path.GetFileName(output)}", CancellationToken.None);
            progress.Report(new Step("ISO lista.", 1));
            return hash;
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }

    private const string DamagedMessage = "La ISO preparada estaba dañada y se ha descartado. Vuelve a pulsar Crear ISO para hacerla de nuevo.";

    private sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
