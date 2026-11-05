using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows.Controls;
using System.Windows.Threading;
using Aurevix.ControlCenter.Services;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Rendimiento en vivo: CPU, RAM, almacenamiento, red y procesos. Indica claramente lo que no está disponible.</summary>
public sealed class PerformancePage : UserControl, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly TextBlock _cpu, _cpuD, _ram, _ramD, _disk, _diskD, _net, _netD;
    private readonly StackPanel _procs = new();
    private long _lastBytes;
    private DateTime _lastNet = DateTime.UtcNow;
    private int _tick;

    public PerformancePage()
    {
        var (root, body) = Ui.Page("Rendimiento", "Datos en tiempo real obtenidos de APIs oficiales de Windows. Se actualiza cada 1,5 s.");
        body.Children.Add(Ui.Wrap(
            Ui.Metric("Procesador", out _cpu, out _cpuD),
            Ui.Metric("Memoria", out _ram, out _ramD),
            Ui.Metric("Almacenamiento (C:)", out _disk, out _diskD),
            Ui.Metric("Red", out _net, out _netD)));

        body.Children.Add(Ui.Card(Ui.H2("GPU y temperaturas"),
            Ui.Muted("El uso de GPU y las temperaturas no se muestran en esta versión: Windows no ofrece una API estándar sin privilegios " +
                     "para todos los fabricantes y no mostraremos valores que no podamos verificar. Puedes verlos en el Administrador de tareas (Rendimiento > GPU).")));

        body.Children.Add(Ui.Card(Ui.H2("Procesos con más memoria"), _procs,
            Ui.Row(Ui.Button("Abrir Administrador de tareas", () => SystemTools.Start("taskmgr.exe")))));

        SystemTools.CpuUsage(); // primera muestra de referencia
        _lastBytes = NetBytes();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
        Content = root;
    }

    private void Refresh()
    {
        _cpu.Text = $"{SystemTools.CpuUsage():0} %";
        _cpuD.Text = $"{Environment.ProcessorCount} procesadores lógicos";

        var (used, total, load) = SystemTools.Memory();
        _ram.Text = $"{load} %";
        _ramD.Text = $"{used:0.0} de {total:0.0} GB en uso";

        try
        {
            var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            _disk.Text = $"{c.AvailableFreeSpace / 1e9:0} GB libres";
            _diskD.Text = $"de {c.TotalSize / 1e9:0} GB";
        }
        catch { _disk.Text = "No disponible"; }

        var now = DateTime.UtcNow;
        var bytes = NetBytes();
        var secs = (now - _lastNet).TotalSeconds;
        var rate = secs > 0 ? (bytes - _lastBytes) / secs : 0;
        _lastBytes = bytes; _lastNet = now;
        _net.Text = rate > 1e6 ? $"{rate / 1e6:0.0} MB/s" : $"{rate / 1e3:0} KB/s";
        _netD.Text = "Tráfico total (envío + recepción)";

        if (_tick++ % 4 == 0) RefreshProcesses();
    }

    private void RefreshProcesses()
    {
        _procs.Children.Clear();
        try
        {
            var top = Process.GetProcesses()
                .Select(p => { try { return (p.ProcessName, p.WorkingSet64); } catch { return (p.ProcessName, 0L); } finally { p.Dispose(); } })
                .GroupBy(p => p.ProcessName)
                .Select(g => (Name: g.Key, Mem: g.Sum(x => x.Item2), Count: g.Count()))
                .OrderByDescending(p => p.Mem).Take(8);
            foreach (var p in top)
                _procs.Children.Add(Ui.Columns(Ui.Text(p.Count > 1 ? $"{p.Name} ({p.Count})" : p.Name), Ui.Muted($"{p.Mem / 1024d / 1024:0} MB")));
        }
        catch (Exception ex) { _procs.Children.Add(Ui.Muted($"No disponible: {ex.Message}")); }
    }

    private static long NetBytes()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Sum(n => { var s = n.GetIPStatistics(); return s.BytesReceived + s.BytesSent; });
        }
        catch { return 0; }
    }

    public void Dispose() => _timer.Stop();
}
