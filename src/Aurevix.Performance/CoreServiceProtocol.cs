using System.Text.Json;
using Aurevix.Core;
using Aurevix.Core.Logging;

namespace Aurevix.Performance;

/// <summary>
/// Protocolo entre AUREVIX Control Center (sin privilegios) y AUREVIX Core Service (con privilegios).
/// Seguridad: el servicio NO acepta comandos del cliente. Solo identificadores de su propio catálogo incrustado,
/// copias de seguridad creadas por ese mismo usuario, listas cerradas (mantenimiento, DNS, canales) y unos pocos
/// valores muy acotados y validados aquí (nombre de un .exe, ruta de un .exe a bloquear, descripción corta).
/// </summary>
public static class CoreServiceProtocol
{
    public const string PipeName = "AurevixCoreService";
    public const int MaxMessageBytes = 64 * 1024;
    public const int Version = 1;

    /// <summary>Tareas de mantenimiento permitidas (comandos fijos definidos en el servicio).</summary>
    public static readonly IReadOnlyDictionary<string, string> MaintenanceActions = new Dictionary<string, string>
    {
        ["dism-cleanup"] = "Limpiar componentes antiguos de Windows Update (DISM /StartComponentCleanup)",
        ["retrim"] = "Optimizar el SSD del sistema (TRIM)",
        ["flush-dns"] = "Vaciar la caché DNS",
        ["tcp-defaults"] = "Restablecer los ajustes TCP óptimos de Windows (autoajuste y RSS)",
        ["network-repair"] = "Reparar la red (Winsock e IP; requiere reiniciar)",
        ["sfc"] = "Comprobar y reparar archivos de sistema (SFC /scannow)",
        ["dism-restorehealth"] = "Reparar la imagen de Windows (DISM /RestoreHealth)",
        ["chkdsk-scan"] = "Buscar errores en el disco del sistema (CHKDSK en línea, sin reiniciar)",
        ["optimize-drives"] = "Optimizar unidades (TRIM en SSD, desfragmentar en HDD)",
        ["purge-standby"] = "Vaciar la memoria en espera (como ISLC)",
        ["power-defaults"] = "Restaurar los planes de energía de fábrica de Windows",
    };

    /// <summary>Proveedores DNS permitidos. El cliente solo envía el identificador.</summary>
    public static readonly IReadOnlyList<DnsProvider> DnsProviders =
    [
        new("cloudflare", "Cloudflare", ["1.1.1.1", "1.0.0.1"], ["2606:4700:4700::1111", "2606:4700:4700::1001"]),
        new("google", "Google", ["8.8.8.8", "8.8.4.4"], ["2001:4860:4860::8888", "2001:4860:4860::8844"]),
        new("quad9", "Quad9 (bloquea dominios maliciosos)", ["9.9.9.9", "149.112.112.112"], ["2620:fe::fe", "2620:fe::9"]),
        new("opendns", "OpenDNS", ["208.67.222.222", "208.67.220.220"], ["2620:119:35::35", "2620:119:53::53"]),
    ];

    /// <summary>Nombre de ejecutable válido para una directiva QoS (sin rutas ni comodines).</summary>
    public static bool IsValidExeName(string? exe) =>
        exe is { Length: > 4 and <= 64 } && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        exe.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ') && !exe.Contains("..");
}

public sealed record DnsProvider(string Id, string Name, string[] IPv4, string[] IPv6);

/// <summary>Configuración de red del sistema (implementada en el servicio con PowerShell y parámetros validados).</summary>
public interface INetworkConfigurator
{
    (bool ok, string message) SetDns(DnsProvider provider);
    (bool ok, string message) RestoreDns();
    (bool ok, string message) AddGameQos(string exe);
    (bool ok, string message) RemoveGameQos(string exe);
    IReadOnlyList<string> ListGameQos();
    /// <summary>Ahorro de energía, EEE, Green Ethernet, moderación de interrupciones y offload en adaptadores físicos. Guarda los valores previos.</summary>
    (bool ok, string message) OptimizeAdapters();
    (bool ok, string message) RestoreAdapters();
    (bool ok, string message) SetMtu(int mtu);
}

/// <summary>Ajustes de dispositivos y procesos con privilegios (las rutas las calcula el propio servicio).</summary>
public interface IDeviceConfigurator
{
    /// <summary>Modo MSI (Message Signaled Interrupts) en las GPU detectadas. Requiere reiniciar.</summary>
    (bool ok, string message) SetGpuMsi(bool enabled);
    /// <summary>Prioridad de CPU alta permanente para un juego (Image File Execution Options\PerfOptions).</summary>
    (bool ok, string message) SetGamePriority(string exe, bool high);
    /// <summary>Memoria virtual fija de 2× la RAM en C: (true) o administrada por Windows (false).</summary>
    (bool ok, string message) SetPagefile(bool fixedDoubleRam);
    /// <summary>Procesos de fondo en baja prioridad: prioridad baja persistente para una lista FIJA de procesos de fondo de Windows.</summary>
    (bool ok, string message) SetBackgroundLowPriority(bool on);
    /// <summary>Prioridad de interrupciones alta para tarjetas de red y controladoras NVMe/SATA.</summary>
    (bool ok, string message) SetComponentPriority(bool on);
    /// <summary>Hipervisor de Windows (Hyper-V, WSL2, VBS). Requiere reiniciar.</summary>
    (bool ok, string message) SetHypervisor(bool on);
    /// <summary>Detección de redes y uso compartido de archivos e impresoras (reglas del Firewall).</summary>
    (bool ok, string message) SetNetworkSharing(bool on);
}

public sealed class CoreRequest
{
    public string Op { get; set; } = "";
    public List<string>? Ids { get; set; }
    public string? Label { get; set; }
    public string? BackupId { get; set; }
    public string? Action { get; set; }
    /// <summary>Texto libre corto (descripción del punto de restauración, ruta de un programa, canal…). Siempre se valida.</summary>
    public string? Value { get; set; }
    public int Seconds { get; set; }
}

public sealed class CoreStatus
{
    public TweakState State { get; set; }
    public int Matched { get; set; }
    public int Total { get; set; }
}

public sealed class CoreResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int Version { get; set; } = CoreServiceProtocol.Version;
    public string? BackupId { get; set; }
    public bool RequiresRestart { get; set; }
    public List<string> Messages { get; set; } = [];
    public List<string>? Items { get; set; }
    public Dictionary<string, CoreStatus>? Statuses { get; set; }
    /// <summary>Resultado estructurado (por ejemplo, la medición del Benchmark en JSON).</summary>
    public string? Data { get; set; }

    public static CoreResponse Fail(string error) => new() { Ok = false, Error = error };
}

public interface IMaintenanceRunner
{
    (bool ok, string message) Run(string action);
}

/// <summary>Lógica del servicio, independiente del transporte (named pipe) para poder probarla.</summary>
/// <summary>
/// Acciones de sistema que antes pedían UAC y ahora hace el servicio, porque AUREVIX forma parte del sistema.
/// Las que REDUCEN la seguridad (exclusiones de Defender) no están aquí a propósito: siguen pidiendo confirmación a Windows.
/// </summary>
public interface ISystemActions
{
    (bool ok, string message) CreateRestorePoint(string description);
    (bool ok, string message) BlockProgram(string exePath);
    (bool ok, string message) UnblockAll();
    IReadOnlyList<string> ListBlocked();
    /// <summary>Mide un juego con PresentMon (ruta fija del servicio). Devuelve el resultado en JSON.</summary>
    (bool ok, string message, string? json) RunBenchmark(string exe, int seconds, string label);
    /// <summary>Comprueba el manifiesto FIRMADO, descarga, verifica el SHA-256 y lanza el instalador con argumentos fijos.</summary>
    (bool ok, string message) InstallUpdate(string channel, string installId);
}

public static class SystemActionRules
{
    public static string SanitizeDescription(string? s) =>
        new string((s ?? "").Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '.' or '_').Take(60).ToArray()).Trim() is { Length: > 0 } d ? d : "AUREVIX";

    /// <summary>
    /// Ruta de programa que se puede bloquear en el Firewall: absoluta, .exe, sin comillas ni comodines y fuera de
    /// Windows y de Defender (así ningún programa puede usar el servicio para dejar sin red a la protección del sistema).
    /// </summary>
    public static bool IsBlockablePath(string? path, string windowsDir, IEnumerable<string> protectedDirs)
    {
        if (path is not { Length: > 7 and <= 260 } || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\') return false;
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.IndexOfAny(['"', '\'', '*', '?', '<', '>', '|', '\0', '\n', '\r']) >= 0) return false;
        if (path.Contains("..") || path.StartsWith(@"\\")) return false;
        foreach (var d in protectedDirs.Append(windowsDir))
            if (path.StartsWith(d.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    public static bool IsValidInstallId(string? id) => id is { Length: 32 } && id.All(Uri.IsHexDigit);
}

public sealed class CoreRequestHandler(IReadOnlyList<TweakDefinition> catalog, Func<string, TweakEngine> engineForUserSid, IMaintenanceRunner? maintenance, INetworkConfigurator? network = null, IDeviceConfigurator? devices = null, ISystemActions? system = null, string? windowsDir = null, IReadOnlyList<string>? protectedDirs = null)
{
    private readonly string _windir = windowsDir ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private readonly IReadOnlyList<string> _protected = protectedDirs ??
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows Defender"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AUREVIX"),
    ];

    private readonly Dictionary<string, TweakDefinition> _byId = catalog.ToDictionary(t => t.Id);

    public string HandleJson(string json, string callerSid)
    {
        CoreResponse resp;
        try
        {
            if (json.Length > CoreServiceProtocol.MaxMessageBytes) resp = CoreResponse.Fail("Petición demasiado grande.");
            else
            {
                var req = JsonSerializer.Deserialize<CoreRequest>(json, JsonDefaults.Options);
                resp = req is null ? CoreResponse.Fail("Petición vacía.") : Handle(req, callerSid);
            }
        }
        catch (JsonException) { resp = CoreResponse.Fail("Petición con formato no válido."); }
        catch (Exception ex)
        {
            AurevixLog.Error("Error en Core Service", ex);
            resp = CoreResponse.Fail("Error interno del servicio.");
        }
        return JsonSerializer.Serialize(resp, JsonDefaults.Options);
    }

    public CoreResponse Handle(CoreRequest req, string callerSid)
    {
        if (string.IsNullOrWhiteSpace(callerSid)) return CoreResponse.Fail("No se pudo identificar al usuario.");
        switch (req.Op)
        {
            case "ping":
                return new CoreResponse { Ok = true };

            case "status":
            {
                var (tweaks, err) = Resolve(req.Ids);
                if (err is not null) return CoreResponse.Fail(err);
                var engine = engineForUserSid(callerSid);
                return new CoreResponse
                {
                    Ok = true,
                    Statuses = tweaks.ToDictionary(t => t.Id, t =>
                    {
                        var s = engine.GetStatus(t);
                        return new CoreStatus { State = s.State, Matched = s.Matched, Total = s.Total };
                    })
                };
            }

            case "apply":
            {
                var (tweaks, err) = Resolve(req.Ids);
                if (err is not null) return CoreResponse.Fail(err);
                var machine = tweaks.Where(t => t.HasMachinePart).ToList();
                if (machine.Count == 0) return new CoreResponse { Ok = true };
                var label = Sanitize(req.Label) ?? "Ajustes";
                var r = engineForUserSid(callerSid).Apply(machine, label, ownerSid: callerSid);
                AurevixLog.Info($"Core Service: apply [{string.Join(", ", machine.Select(t => t.Id))}] por {callerSid}: {(r.Success ? "ok" : "fallo")}");
                return new CoreResponse
                {
                    Ok = r.Success, Error = r.Success ? null : string.Join(" ", r.Messages),
                    BackupId = r.Success ? r.Backup?.Id : null, RequiresRestart = r.Backup?.RequiresRestart ?? false,
                    Messages = r.Messages.ToList()
                };
            }

            case "revert":
            {
                if (string.IsNullOrWhiteSpace(req.BackupId) || req.BackupId.Length > 64 || req.BackupId.Any(c => !(char.IsLetterOrDigit(c) || c == '-')))
                    return CoreResponse.Fail("Identificador de copia no válido.");
                var engine = engineForUserSid(callerSid);
                var b = engine.FindBackup(req.BackupId);
                if (b is null) return CoreResponse.Fail("No existe esa copia de seguridad.");
                if (!string.Equals(b.OwnerSid, callerSid, StringComparison.OrdinalIgnoreCase))
                    return CoreResponse.Fail("Esa copia de seguridad pertenece a otro usuario.");
                engine.Revert(b);
                engine.DeleteBackup(b.Id);
                AurevixLog.Info($"Core Service: revert {b.Id} por {callerSid}");
                return new CoreResponse { Ok = true, RequiresRestart = b.RequiresRestart };
            }

            case "maintenance":
            {
                if (req.Action is null || !CoreServiceProtocol.MaintenanceActions.ContainsKey(req.Action))
                    return CoreResponse.Fail("Tarea de mantenimiento no permitida.");
                if (maintenance is null) return CoreResponse.Fail("Mantenimiento no disponible.");
                var (ok, msg) = maintenance.Run(req.Action);
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            case "dns-set":
            {
                var p = CoreServiceProtocol.DnsProviders.FirstOrDefault(d => d.Id == req.Action);
                if (p is null) return CoreResponse.Fail("Proveedor DNS no permitido.");
                if (network is null) return CoreResponse.Fail("Red no disponible.");
                var (ok, msg) = network.SetDns(p);
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            case "dns-restore":
            {
                if (network is null) return CoreResponse.Fail("Red no disponible.");
                var (ok, msg) = network.RestoreDns();
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            case "qos-add" or "qos-remove":
            {
                if (!CoreServiceProtocol.IsValidExeName(req.Action)) return CoreResponse.Fail("Nombre de juego no válido (debe ser un .exe sin ruta).");
                if (network is null) return CoreResponse.Fail("Red no disponible.");
                var (ok, msg) = req.Op == "qos-add" ? network.AddGameQos(req.Action!) : network.RemoveGameQos(req.Action!);
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            case "nic-optimize" or "nic-restore":
            {
                if (network is null) return CoreResponse.Fail("Red no disponible.");
                var (ok, msg) = req.Op == "nic-optimize" ? network.OptimizeAdapters() : network.RestoreAdapters();
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            case "mtu-set":
            {
                if (!int.TryParse(req.Action, out var mtu) || mtu is < 1280 or > 1500) return CoreResponse.Fail("MTU fuera de rango (1280-1500).");
                if (network is null) return CoreResponse.Fail("Red no disponible.");
                var (ok, msg) = network.SetMtu(mtu);
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            case "msi-gpu-on" or "msi-gpu-off":
            {
                if (devices is null) return CoreResponse.Fail("Dispositivos no disponibles.");
                var (ok, msg) = devices.SetGpuMsi(req.Op == "msi-gpu-on");
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg], RequiresRestart = ok };
            }

            case "game-priority-set" or "game-priority-remove":
            {
                if (!CoreServiceProtocol.IsValidExeName(req.Action)) return CoreResponse.Fail("Nombre de juego no válido (debe ser un .exe sin ruta).");
                if (devices is null) return CoreResponse.Fail("Dispositivos no disponibles.");
                var (ok, msg) = devices.SetGamePriority(req.Action!, req.Op == "game-priority-set");
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            case "pagefile-fixed" or "pagefile-auto":
            {
                if (devices is null) return CoreResponse.Fail("Dispositivos no disponibles.");
                var (ok, msg) = devices.SetPagefile(req.Op == "pagefile-fixed");
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg], RequiresRestart = ok };
            }

            case "bgprio-on" or "bgprio-off" or "irqprio-on" or "irqprio-off" or "hyperv-on" or "hyperv-off" or "sharing-on" or "sharing-off":
            {
                if (devices is null) return CoreResponse.Fail("Dispositivos no disponibles.");
                var on = req.Op.EndsWith("-on");
                var (ok, msg) = req.Op[..req.Op.IndexOf('-')] switch
                {
                    "bgprio" => devices.SetBackgroundLowPriority(on),
                    "irqprio" => devices.SetComponentPriority(on),
                    "hyperv" => devices.SetHypervisor(on),
                    _ => devices.SetNetworkSharing(on)
                };
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg], RequiresRestart = ok && !req.Op.StartsWith("sharing") };
            }

            case "qos-list":
                return new CoreResponse { Ok = true, Items = network?.ListGameQos().ToList() ?? [] };

            case "restore-point":
            {
                if (system is null) return CoreResponse.Fail("Acciones de sistema no disponibles.");
                var (ok, msg) = system.CreateRestorePoint(SystemActionRules.SanitizeDescription(req.Value));
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            case "firewall-block":
            {
                if (system is null) return CoreResponse.Fail("Acciones de sistema no disponibles.");
                if (!SystemActionRules.IsBlockablePath(req.Value, _windir, _protected))
                    return CoreResponse.Fail("Ese programa no se puede bloquear (solo .exe fuera de Windows y de Defender).");
                var (ok, msg) = system.BlockProgram(req.Value!);
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            case "firewall-unblock-all":
            {
                if (system is null) return CoreResponse.Fail("Acciones de sistema no disponibles.");
                var (ok, msg) = system.UnblockAll();
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            case "firewall-list":
                return new CoreResponse { Ok = true, Items = system?.ListBlocked().ToList() ?? [] };

            case "benchmark":
            {
                if (system is null) return CoreResponse.Fail("Acciones de sistema no disponibles.");
                if (!CoreServiceProtocol.IsValidExeName(req.Value)) return CoreResponse.Fail("Nombre de juego no válido.");
                if (req.Seconds is < 10 or > 300) return CoreResponse.Fail("Duración no válida (10-300 s).");
                var (ok, msg, json) = system.RunBenchmark(req.Value!, req.Seconds, Sanitize(req.Label) ?? "Medición");
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg], Data = json };
            }

            case "update-install":
            {
                if (system is null) return CoreResponse.Fail("Acciones de sistema no disponibles.");
                var channel = req.Value ?? "stable";
                if (!new[] { "stable", "beta", "developer" }.Contains(channel)) return CoreResponse.Fail("Canal no válido.");
                if (!SystemActionRules.IsValidInstallId(req.Label)) return CoreResponse.Fail("Identificador de instalación no válido.");
                var (ok, msg) = system.InstallUpdate(channel, req.Label!);
                return new CoreResponse { Ok = ok, Error = ok ? null : msg, Messages = [msg] };
            }

            default:
                return CoreResponse.Fail("Operación desconocida.");
        }
    }

    private (List<TweakDefinition> tweaks, string? error) Resolve(List<string>? ids)
    {
        if (ids is null || ids.Count == 0) return ([], "No se indicaron ajustes.");
        if (ids.Count > 200) return ([], "Demasiados ajustes en una petición.");
        var list = new List<TweakDefinition>();
        foreach (var id in ids.Distinct())
        {
            if (!_byId.TryGetValue(id, out var t)) return ([], $"Ajuste desconocido: {id}");
            list.Add(t);
        }
        return (list, null);
    }

    private static string? Sanitize(string? s) =>
        s is null ? null : new string(s.Where(c => !char.IsControl(c)).Take(120).ToArray());
}
