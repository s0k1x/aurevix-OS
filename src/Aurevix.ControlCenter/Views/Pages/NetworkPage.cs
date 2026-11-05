using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>
/// Red y latencia: test de DNS real, cambio a DNS rápido (y restaurar), test de ping/jitter,
/// prioridad de red (QoS) por juego y reparación. Honesto: nada aumenta la velocidad contratada.
/// </summary>
public sealed class NetworkPage : UserControl
{
    private readonly StackPanel _dnsResults = new();
    private readonly StackPanel _pingResults = new();
    private readonly StackPanel _qosList = new();
    private readonly TextBox _host = Box("1.1.1.1", 220);
    private readonly TextBox _qosExe = Box("juego.exe", 260);

    public NetworkPage()
    {
        var (root, body) = Ui.Page("Red y latencia", "Menos ping y menos tirones de red. Todo se mide antes y después.");

        body.Children.Add(Ui.Card(Ui.Eyebrow("LA VERDAD SOBRE «MÁS INTERNET»"),
            Ui.Muted("Ningún programa puede darte más velocidad de la que contratas. Lo que sí baja el ping: que nada consuma red en segundo plano " +
                     "mientras juegas (actualizaciones, P2P, apps de fondo), un DNS rápido para conectar antes, y usar cable en lugar de Wi-Fi. " +
                     "Los ajustes de red de AUREVIX están en Optimización > Red y en los niveles Rendimiento y Máximo.")));

        body.Children.Add(Ui.Card(Ui.H2("DNS más rápido"),
            Ui.Muted("Mide cuánto tarda cada servidor DNS en responder desde tu conexión (5 consultas, se usa la mediana)."),
            Ui.Row(Ui.Button("Medir DNS", () => _ = TestDnsAsync(), primary: true), Ui.Button("Restaurar DNS original", () => _ = ServiceAsync(new CoreRequest { Op = "dns-restore" }))),
            _dnsResults));

        body.Children.Add(Ui.Card(Ui.H2("Ping y estabilidad"),
            Ui.Muted("Envía 20 pings y calcula la media, el jitter (variación entre pings) y la pérdida. Prueba con la IP del servidor de tu juego si la conoces."),
            Ui.Row(Ui.Muted("Servidor:  "), _host, Ui.Button("Medir ping", () => _ = PingAsync(_host.Text.Trim()), primary: true)),
            _pingResults));

        body.Children.Add(Ui.Card(Ui.H2("Prioridad de red para tus juegos (QoS)"),
            Ui.Muted("Marca el tráfico del juego con DSCP 46 (como hace Xbox). Solo tiene efecto si tu router respeta QoS/WMM; si no, no cambia nada."),
            Ui.Row(Ui.Muted("Ejecutable:  "), _qosExe, Ui.Button("Añadir", () => _ = QosAsync("qos-add", _qosExe.Text.Trim()))),
            _qosList));

        body.Children.Add(Ui.Card(Ui.Eyebrow("ADAPTADOR DE RED"), Ui.H2("Optimizar la tarjeta de red"),
            Ui.Muted("Desactiva en tus adaptadores físicos el ahorro de energía (EEE, Green Ethernet, «permitir que el equipo apague este dispositivo»), " +
                     "la moderación de interrupciones y el offload de envío grande. Menos picos de ping a cambio de algo más de CPU. " +
                     "Se guardan los valores originales. La red se corta unos segundos al aplicarlo."),
            Ui.Row(Ui.Button("Optimizar adaptadores", () => _ = NicAsync("nic-optimize"), primary: true), Ui.Button("Restaurar originales", () => _ = NicAsync("nic-restore")))));

        _mtuResult = Ui.Muted("");
        body.Children.Add(Ui.Card(Ui.H2("MTU óptimo"),
            Ui.Muted("Busca el tamaño de paquete más grande que tu conexión admite sin fragmentar. Lo normal es 1500; con PPPoE (algunas fibras y ADSL) suele ser 1492."),
            Ui.Row(Ui.Button("Detectar MTU", () => _ = DetectMtuAsync(), primary: true), Ui.Button("Restablecer 1500", () => _ = ServiceAsync(new CoreRequest { Op = "mtu-set", Action = "1500" }))),
            _mtuResult));

        body.Children.Add(Ui.Card(Ui.H2("Bufferbloat"),
            Ui.Muted("El bufferbloat (ping que se dispara al descargar) depende sobre todo del router. Haz el test: si sacas menos de A, activa SQM/QoS en tu router."),
            Ui.Row(Ui.Button("Abrir test de bufferbloat", () => SystemTools.Start("https://www.waveform.com/tools/bufferbloat")))));

        body.Children.Add(Ui.Card(Ui.H2("Bloquear internet a una aplicación"),
            Ui.Muted("Crea una regla en el Firewall de Windows que impide que el programa elegido se conecte a internet. Lo hace AUREVIX Core Service, sin avisos de permisos."),
            Ui.Row(Ui.Button("Elegir programa…", () => _ = FirewallAsync(block: true)), Ui.Button("Quitar bloqueos de AUREVIX", () => _ = FirewallAsync(block: false)))));

        var tools = Ui.Wrap(
            Ui.Button("Vaciar caché DNS", () => _ = ServiceAsync(new CoreRequest { Op = "maintenance", Action = "flush-dns" })),
            Ui.Button("Restablecer TCP óptimo", () => _ = ServiceAsync(new CoreRequest { Op = "maintenance", Action = "tcp-defaults" })),
            Ui.Button("Reparar red", async () =>
            {
                if (MessageBox.Show("Restablece Winsock y la pila IP. Útil si internet falla. Requiere reiniciar. ¿Continuar?", "Red", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                if (await ServiceAsync(new CoreRequest { Op = "maintenance", Action = "network-repair" }))
                    RestartPrompt.Ask("Reinicia para terminar la reparación", "La red se ha restablecido; los cambios se aplican tras reiniciar.");
            }));
        foreach (UIElement b in tools.Children) ((FrameworkElement)b).Margin = new Thickness(0, 0, 8, 8);
        body.Children.Add(Ui.Card(Ui.H2("Herramientas"), tools,
            Ui.Muted(NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                ? "Estás usando Wi-Fi: para jugar, un cable Ethernet suele bajar el ping y, sobre todo, el jitter."
                : "Conexión por cable detectada: es la mejor opción para jugar.")));

        Content = root;
        Loaded += async (_, _) => await RefreshQosAsync();
    }

    private TextBlock _mtuResult = new();

    private static async Task NicAsync(string op)
    {
        if (MessageBox.Show(op == "nic-optimize" ? "¿Optimizar los adaptadores de red? La conexión se cortará unos segundos." : "¿Restaurar la configuración original de los adaptadores?",
                "Red", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await ServiceAsync(new CoreRequest { Op = op });
    }

    private async Task DetectMtuAsync()
    {
        _mtuResult.Text = "Probando tamaños de paquete…";
        using var ping = new Ping();
        int lo = 1200, hi = 1472, best = 0; // carga útil: MTU = carga + 28
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            PingReply? r = null;
            try { r = await ping.SendPingAsync("1.1.1.1", 1500, new byte[mid], new PingOptions(64, dontFragment: true)); } catch { }
            if (r?.Status == IPStatus.Success) { best = mid; lo = mid + 1; } else hi = mid - 1;
        }
        if (best == 0) { _mtuResult.Text = "No se pudo determinar (sin respuesta a ping)."; return; }
        var mtu = best + 28;
        _mtuResult.Text = mtu >= 1500 ? "Tu MTU óptimo es 1500: no hace falta cambiar nada." : $"Tu MTU óptimo es {mtu}.";
        if (mtu < 1500 && MessageBox.Show($"Tu conexión admite como máximo {mtu} bytes. ¿Ajustar el MTU a {mtu}?", "MTU", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            await ServiceAsync(new CoreRequest { Op = "mtu-set", Action = mtu.ToString() });
    }

    /// <summary>
    /// Bloqueo de programas en el Firewall a través de AUREVIX Core Service (sin UAC). El servicio solo acepta
    /// .exe fuera de Windows y de Defender, y solo toca las reglas del grupo AUREVIX.
    /// </summary>
    private static async Task FirewallAsync(bool block)
    {
        CoreResponse r;
        if (block)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programas|*.exe", Title = "Programa al que bloquear internet" };
            if (dlg.ShowDialog() != true) return;
            r = await CoreServiceClient.SendAsync(new CoreRequest { Op = "firewall-block", Value = dlg.FileName });
        }
        else r = await CoreServiceClient.SendAsync(new CoreRequest { Op = "firewall-unblock-all" });
        MessageBox.Show(r.Ok ? string.Join("\n", r.Messages) : r.Error ?? "No se pudo cambiar el Firewall.", "Red");
    }

    private static TextBox Box(string text, double width) => new()
    {
        Text = text, Width = width, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 10, 0),
        Background = Ui.Brush("SurfaceAlt"), Foreground = Ui.Brush("Text"), BorderBrush = Ui.Brush("BorderBrushAx"), CaretBrush = Ui.Brush("Text")
    };

    // ------------------------------------------------------------------ DNS

    private async Task TestDnsAsync()
    {
        _dnsResults.Children.Clear();
        _dnsResults.Children.Add(Ui.Muted("Midiendo…"));
        var targets = CoreServiceProtocol.DnsProviders.Select(p => (p.Id, p.Name, Ip: p.IPv4[0])).ToList();
        var current = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().DnsAddresses).Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.ToString()).Distinct().FirstOrDefault();
        if (current is not null && targets.All(t => t.Ip != current)) targets.Insert(0, ("current", "Tu DNS actual", current));

        var results = new List<(string Id, string Name, string Ip, double? Ms)>();
        foreach (var t in targets) results.Add((t.Id, t.Name, t.Ip, await DnsProbe.MedianMsAsync(t.Ip)));

        _dnsResults.Children.Clear();
        var best = results.Where(r => r.Ms is not null && r.Id != "current").OrderBy(r => r.Ms).FirstOrDefault();
        foreach (var r in results.OrderBy(r => r.Ms ?? double.MaxValue))
        {
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(Ui.Text(r.Ms is { } ms ? $"{ms:0} ms" : "sin respuesta", 14, FontWeights.SemiBold));
            if (r.Id != "current" && r.Ms is not null)
            {
                var use = Ui.Button("Usar", () => _ = SetDnsAsync(r.Id, r.Name), primary: r.Id == best.Id);
                use.Margin = new Thickness(14, 0, 0, 0);
                right.Children.Add(use);
            }
            var row = Ui.Columns(new StackPanel { Children = { Ui.Text(r.Name), Ui.Muted(r.Ip + (r.Id == best.Id ? " · el más rápido" : "")) } }, right);
            row.Margin = new Thickness(0, 6, 0, 6);
            _dnsResults.Children.Add(row);
        }
    }

    private async Task SetDnsAsync(string id, string name)
    {
        if (MessageBox.Show($"¿Usar {name} como DNS en tus adaptadores de red? Puedes restaurar el original cuando quieras.", "DNS", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await ServiceAsync(new CoreRequest { Op = "dns-set", Action = id });
    }

    // ------------------------------------------------------------------ Ping

    private async Task PingAsync(string host)
    {
        _pingResults.Children.Clear();
        if (string.IsNullOrWhiteSpace(host)) return;
        _pingResults.Children.Add(Ui.Muted($"Enviando 20 pings a {host}…"));
        var times = new List<long>();
        var lost = 0;
        using var ping = new Ping();
        for (var i = 0; i < 20; i++)
        {
            try
            {
                var r = await ping.SendPingAsync(host, 2000);
                if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime); else lost++;
            }
            catch { lost++; }
            await Task.Delay(150);
        }
        _pingResults.Children.Clear();
        if (times.Count == 0) { _pingResults.Children.Add(Ui.Muted("Sin respuesta (el servidor puede bloquear el ping).")); return; }
        var jitter = times.Count > 1 ? times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0;
        _pingResults.Children.Add(Ui.Wrap(
            Ui.Metric("Ping medio", out var avg, out var avgD), Ui.Metric("Jitter", out var jit, out var jitD), Ui.Metric("Pérdida", out var loss, out var lossD)));
        avg.Text = $"{times.Average():0} ms"; avgD.Text = $"mín {times.Min()} · máx {times.Max()} ms";
        jit.Text = $"{jitter:0.0} ms"; jitD.Text = jitter < 5 ? "Muy estable" : jitter < 15 ? "Aceptable" : "Inestable: prueba con cable";
        loss.Text = $"{lost * 100 / 20} %"; lossD.Text = lost == 0 ? "Sin pérdida" : "Hay pérdida de paquetes";
    }

    // ------------------------------------------------------------------ QoS

    private async Task QosAsync(string op, string exe)
    {
        if (await ServiceAsync(new CoreRequest { Op = op, Action = exe }, quiet: false)) await RefreshQosAsync();
    }

    private async Task RefreshQosAsync()
    {
        _qosList.Children.Clear();
        var r = await CoreServiceClient.SendAsync(new CoreRequest { Op = "qos-list" });
        if (!r.Ok) { _qosList.Children.Add(Ui.Muted("Requiere AUREVIX Core Service.")); return; }
        foreach (var exe in r.Items ?? [])
        {
            var row = Ui.Columns(Ui.Text(exe), Ui.Button("Quitar", () => _ = QosAsync("qos-remove", exe)));
            row.Margin = new Thickness(0, 4, 0, 4);
            _qosList.Children.Add(row);
        }
    }

    private static async Task<bool> ServiceAsync(CoreRequest req, bool quiet = false)
    {
        var r = await CoreServiceClient.SendAsync(req, TimeSpan.FromMinutes(3));
        if (!quiet || !r.Ok) MessageBox.Show(r.Ok ? string.Join("\n", r.Messages) : r.Error ?? "Error", "Red");
        return r.Ok;
    }
}

/// <summary>Consulta DNS mínima por UDP (registro A) para medir el tiempo de respuesta de un servidor.</summary>
public static class DnsProbe
{
    public static async Task<double?> MedianMsAsync(string server, int tries = 5, string name = "www.microsoft.com")
    {
        var samples = new List<double>();
        for (var i = 0; i < tries; i++)
        {
            try
            {
                using var udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Connect(IPAddress.Parse(server), 53);
                var query = Build((ushort)Random.Shared.Next(1, 65535), name);
                var sw = Stopwatch.StartNew();
                await udp.SendAsync(query);
                var recv = udp.ReceiveAsync();
                if (await Task.WhenAny(recv, Task.Delay(1500)) == recv && recv.Result.Buffer.Length > 12 && recv.Result.Buffer[0] == query[0] && recv.Result.Buffer[1] == query[1])
                    samples.Add(sw.Elapsed.TotalMilliseconds);
            }
            catch { }
        }
        if (samples.Count == 0) return null;
        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static byte[] Build(ushort id, string name)
    {
        var bytes = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.Split('.')) { bytes.Add((byte)label.Length); bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(label)); }
        bytes.AddRange([0, 0, 1, 0, 1]);
        return bytes.ToArray();
    }
}
