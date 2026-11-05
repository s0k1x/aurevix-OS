using System.Diagnostics;
using System.Text;
using Aurevix.Core.Logging;
using Aurevix.Performance;

namespace Aurevix.Service;

/// <summary>
/// DNS y QoS con cmdlets oficiales (NetTCPIP / DnsClient / NetQos). Los parámetros llegan ya validados
/// (proveedor de una lista cerrada, nombre de .exe sin ruta) y además se citan con comillas simples.
/// El DNS original de cada adaptador se guarda la primera vez para poder restaurarlo.
/// </summary>
public sealed class NetworkConfigurator(string stateDir) : INetworkConfigurator
{
    private string BackupFile => Path.Combine(stateDir, "dns-original.json");
    private const string QosPrefix = "AUREVIX ";

    public (bool ok, string message) SetDns(DnsProvider p)
    {
        if (!File.Exists(BackupFile))
        {
            var backup = Ps("Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object { " +
                            "[pscustomobject]@{ Index = $_.ifIndex; " +
                            "V4 = @((Get-DnsClientServerAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4).ServerAddresses); " +
                            "V6 = @((Get-DnsClientServerAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv6).ServerAddresses); " +
                            "Dhcp = ((Get-ItemProperty \"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\$($_.InterfaceGuid)\" -Name NameServer -ErrorAction SilentlyContinue).NameServer -in @($null, '')) } } | ConvertTo-Json -Depth 3");
            if (!backup.ok) return (false, "No se pudo leer la configuración DNS actual.");
            File.WriteAllText(BackupFile, string.IsNullOrWhiteSpace(backup.output) ? "[]" : backup.output);
        }
        var v4 = string.Join(",", p.IPv4.Select(Q));
        var v6 = string.Join(",", p.IPv6.Select(Q));
        var r = Ps($"Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object {{ " +
                   $"Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses @({v4},{v6}) }}; Clear-DnsClientCache");
        AurevixLog.Info($"DNS → {p.Id}: {(r.ok ? "ok" : r.output)}");
        return r.ok ? (true, $"DNS cambiado a {p.Name}.") : (false, "No se pudo cambiar el DNS.");
    }

    public (bool ok, string message) RestoreDns()
    {
        if (!File.Exists(BackupFile))
        {
            var reset = Ps("Get-NetAdapter -Physical | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses }; Clear-DnsClientCache");
            return reset.ok ? (true, "DNS automático (DHCP) restaurado.") : (false, "No se pudo restaurar el DNS.");
        }
        // Se reconstruye con datos del propio archivo protegido del servicio.
        var script = $"$b = Get-Content -Raw -LiteralPath {Q(BackupFile)} | ConvertFrom-Json; foreach ($a in @($b)) {{ " +
                     "if ($a.Dhcp) { Set-DnsClientServerAddress -InterfaceIndex $a.Index -ResetServerAddresses } " +
                     "else { Set-DnsClientServerAddress -InterfaceIndex $a.Index -ServerAddresses (@($a.V4) + @($a.V6)) } }; Clear-DnsClientCache";
        var r = Ps(script);
        if (r.ok) File.Delete(BackupFile);
        return r.ok ? (true, "DNS original restaurado.") : (false, "No se pudo restaurar el DNS original.");
    }

    public (bool ok, string message) AddGameQos(string exe)
    {
        var name = QosPrefix + exe;
        var r = Ps($"New-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\QoS' -Name 'Do not use NLA' -Value '1' -PropertyType String -Force | Out-Null; " +
                   $"Remove-NetQosPolicy -Name {Q(name)} -Confirm:$false -ErrorAction SilentlyContinue; " +
                   $"New-NetQosPolicy -Name {Q(name)} -AppPathNameMatchCondition {Q(exe)} -IPProtocolMatchCondition Both -DSCPAction 46 -NetworkProfile All | Out-Null");
        return r.ok ? (true, $"Prioridad de red (DSCP 46) activada para {exe}. Tu router debe respetar QoS para notar el efecto.")
                    : (false, "No se pudo crear la directiva QoS.");
    }

    public (bool ok, string message) RemoveGameQos(string exe)
    {
        var r = Ps($"Remove-NetQosPolicy -Name {Q(QosPrefix + exe)} -Confirm:$false");
        return r.ok ? (true, $"Prioridad de red quitada para {exe}.") : (false, "No se pudo quitar la directiva QoS.");
    }

    public IReadOnlyList<string> ListGameQos()
    {
        var r = Ps($"Get-NetQosPolicy | Where-Object Name -like {Q(QosPrefix + "*")} | ForEach-Object {{ $_.AppPathNameMatchCondition }}");
        return r.ok ? r.output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
    }

    private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    private static (bool ok, string output) Ps(string script)
    {
        var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + script));
        using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 })!;
        var output = p.StandardOutput.ReadToEnd();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit(120_000);
        if (p.ExitCode != 0) AurevixLog.Warn($"PowerShell: {err}");
        return (p.ExitCode == 0, output.Trim());
    }
}
