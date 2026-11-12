using System.IO;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>
/// Herramientas: Explorador, reparación del sistema (SFC, DISM, CHKDSK), discos, memoria,
/// drivers (información del equipo y enlaces oficiales), modo MSI de la GPU y ajustes avanzados del sistema.
/// </summary>
public sealed class ToolsPage : UserControl
{
    public ToolsPage()
    {
        var (root, body) = Ui.Page("Herramientas", "Reparación, mantenimiento y drivers con las herramientas oficiales de Windows.");

        body.Children.Add(Section("Explorador de Windows",
            Tool("Reiniciar el Explorador", "Útil si la barra de tareas, el escritorio o el Explorador no responden.", () =>
            {
                if (Confirm("Se cerrarán las ventanas de carpetas abiertas (no tus programas). ¿Reiniciar el Explorador?")) AurevixStyle.RestartExplorer();
            }),
            Tool("Reparar la caché de iconos", "Si los iconos se ven en blanco o equivocados. Reinicia el Explorador.", RepairIconCache)));

        body.Children.Add(Section("Reparar Windows",
            Svc("Comprobar archivos de sistema (SFC)", "Busca y repara archivos de Windows dañados. Tarda unos 10-15 minutos.", "sfc"),
            Svc("Reparar la imagen de Windows (DISM)", "Repara el almacén de componentes desde Windows Update. Ejecútalo antes de SFC si SFC no puede reparar.", "dism-restorehealth"),
            Svc("Buscar errores en el disco (CHKDSK)", "Analiza el disco del sistema en línea, sin reiniciar.", "chkdsk-scan"),
            Svc("Limpiar componentes antiguos (DISM)", "Libera espacio de actualizaciones antiguas de Windows.", "dism-cleanup"),
            Svc("Comprobar la imagen de Windows (rápido)", "Mira si Windows ha marcado la imagen como dañada. Tarda unos segundos.", "dism-checkhealth"),
            Svc("Analizar la imagen de Windows a fondo", "Busca daños en la imagen de Windows sin repararlos. Puede tardar varios minutos.", "dism-scanhealth"),
            Svc("Ver si TRIM está activado", "Muestra si Windows envía TRIM a tus unidades SSD (0 = activado).", "trim-query"),
            Svc("Renovar la dirección IP", "Suelta y vuelve a pedir la dirección de red. La conexión se interrumpe unos segundos.", "ip-renew"),
            Svc("Informe de energía (30 s)", "Analiza 30 segundos el consumo y los problemas de energía y abre el informe.", "energy-report")));

        body.Children.Add(Ui.Card(Ui.H2("Informes del sistema"),
            Ui.Muted("Solo muestran información; no cambian nada en tu equipo."),
            Ui.Wrap(
                Report("Configuración de red (ipconfig /all)", "ipconfig.exe", "/all", false),
                Report("Conexiones activas (netstat -ano)", "netstat.exe", "-ano", false),
                Report("Ajustes TCP (netsh)", "netsh.exe", "int tcp show global", false),
                Report("Planes de energía (powercfg /list)", "powercfg.exe", "/list", false),
                Report("Servicios en ejecución", "powershell.exe", "-NoProfile -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-Service | Where-Object Status -eq Running | Sort-Object DisplayName | Format-Table -AutoSize Name,DisplayName | Out-String -Width 200\"", true),
                Report("Tareas programadas activas", "powershell.exe", "-NoProfile -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-ScheduledTask | Where-Object State -ne Disabled | Sort-Object TaskPath | Format-Table -AutoSize TaskPath,TaskName,State | Out-String -Width 220\"", true))));

        body.Children.Add(Section("Discos y memoria",
            Svc("Optimizar unidades", "TRIM en SSD y desfragmentación en discos duros, como hace Windows.", "optimize-drives"),
            Tool("Liberador de espacio en disco", "La herramienta clásica de Windows: útil para borrar Windows.old tras una actualización.", () => SystemTools.Start("cleanmgr.exe")),
            Svc("Vaciar la memoria en espera", "Libera la caché de memoria en espera (como ISLC). Game Boost puede hacerlo solo al empezar a jugar.", "purge-standby")));

        body.Children.Add(DriversCard());

        body.Children.Add(Section("Gráfica avanzada",
            Tool("Activar modo MSI en la GPU", "Message Signaled Interrupts: interrupciones más eficientes entre CPU y GPU. Muchas GPU modernas ya lo usan. Requiere reiniciar.", () => _ = MsiAsync(true)),
            Tool("Restaurar modo de interrupciones original", "Deshace el cambio anterior. Requiere reiniciar.", () => _ = MsiAsync(false))));

        body.Children.Add(Section("Sistema avanzado",
            Pair("Memoria virtual fija", "Archivo de paginación fijo de 2× tu RAM en C: (evita que crezca a tirones mientras juegas). Necesita espacio libre. Requiere reiniciar.",
                "pagefile-fixed", "pagefile-auto", "Fijar", "Automática (Windows)", restart: true),
            Pair("Procesos de fondo de Windows en baja prioridad", "Indexador de búsqueda, telemetría de compatibilidad y trabajadores de Windows Update ceden CPU y disco a tus juegos. Siguen funcionando.",
                "bgprio-on", "bgprio-off", "Activar", "Restaurar"),
            Pair("Prioridad para red y discos", "Prioridad de interrupciones alta para tarjetas de red y controladoras NVMe/SATA. Guarda los valores originales. Requiere reiniciar.",
                "irqprio-on", "irqprio-off", "Activar", "Restaurar", restart: true),
            Pair("Hipervisor de Windows (Hyper-V, WSL2, VBS)", "Desactivarlo puede dar algo de rendimiento en juegos, pero WSL2, Docker, Sandbox y algunos anticheat lo necesitan. Requiere reiniciar.",
                "hyperv-off", "hyperv-on", "Desactivar", "Activar", restart: true),
            Pair("Detección de redes y uso compartido", "Solo en redes privadas. Desactívalo si no compartes carpetas ni impresoras con otros equipos.",
                "sharing-off", "sharing-on", "Desactivar", "Activar"),
            Svc("Restaurar los planes de energía de Windows", "Vuelve a los planes de fábrica (Equilibrado, etc.). Útil si un plan da problemas.", "power-defaults")));

        Content = root;
    }

    private static UIElement Pair(string title, string text, string opA, string opB, string labelA, string labelB, bool restart = false)
    {
        async Task Run(string op, string label)
        {
            if (!Confirm($"{title}: {label.ToLowerInvariant()}.\n\n{text}\n\n¿Continuar?")) return;
            var r = await CoreServiceClient.SendAsync(new CoreRequest { Op = op }, TimeSpan.FromMinutes(3));
            if (!r.Ok) { MessageBox.Show(r.Error ?? "Error", title); return; }
            if (restart && r.RequiresRestart) RestartPrompt.Ask("Reinicia para aplicar el cambio", string.Join(" ", r.Messages));
            else MessageBox.Show(string.Join("\n", r.Messages), title);
        }
        var info = new StackPanel { Children = { Ui.Text(title, 14, FontWeights.SemiBold), Ui.Muted(text) } };
        var buttons = Ui.Row(Ui.Button(labelA, () => _ = Run(opA, labelA), primary: true), Ui.Button(labelB, () => _ = Run(opB, labelB)));
        var row = Ui.Columns(info, buttons);
        row.Margin = new Thickness(0, 8, 0, 8);
        return row;
    }

    private static Border Section(string title, params UIElement[] items)
    {
        var card = Ui.Card(Ui.H2(title));
        foreach (var i in items) ((StackPanel)card.Child).Children.Add(i);
        return card;
    }

    private static UIElement Tool(string title, string text, Action action)
    {
        var info = new StackPanel { Children = { Ui.Text(title, 14, FontWeights.SemiBold), Ui.Muted(text) } };
        var row = Ui.Columns(info, Ui.Button("Ejecutar", action));
        row.Margin = new Thickness(0, 8, 0, 8);
        return row;
    }

    private static UIElement Svc(string title, string text, string action) => Tool(title, text, () => _ = RunServiceAsync(title, action));

    private static bool Confirm(string text) => MessageBox.Show(text, "Herramientas", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static async Task RunServiceAsync(string title, string action)
    {
        if (!Confirm($"{title}\n\nPuede tardar varios minutos. Puedes seguir usando el equipo. ¿Continuar?")) return;
        var r = await CoreServiceClient.SendAsync(new CoreRequest { Op = "maintenance", Action = action }, TimeSpan.FromMinutes(90));
        MessageBox.Show(r.Ok ? string.Join("\n", r.Messages) : r.Error ?? "Error", title);
        if (r.Ok && action == "energy-report")
        {
            var html = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AUREVIX", "informe-energia.html");
            if (File.Exists(html)) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(html) { UseShellExecute = true }); } catch { } }
        }
    }

    // ------------------------------------------------------------------ Informes (solo lectura, sin servicio)

    private UIElement Report(string title, string file, string args, bool utf8) =>
        Ui.Button(title, () => _ = ShowReportAsync(title, file, args, utf8));

    private async Task ShowReportAsync(string title, string file, string args, bool utf8)
    {
        string output;
        try { output = await Task.Run(() => RunReadOnly(file, args, utf8)); }
        catch (Exception ex) { output = "No se pudo obtener el informe: " + ex.Message; }

        var box = new TextBox
        {
            Text = output, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12,
            TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 10),
        };
        var copy = Ui.Button("Copiar", () => { try { Clipboard.SetText(output); } catch { } });
        copy.HorizontalAlignment = HorizontalAlignment.Right;
        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(copy, 1);
        grid.Children.Add(box);
        grid.Children.Add(copy);
        new Window
        {
            Title = title, Width = 900, Height = 600, Background = Ui.Brush("Bg"), Content = grid,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner,
        }.Show();
    }

    private static string RunReadOnly(string file, string args, bool utf8)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var enc = utf8 ? System.Text.Encoding.UTF8 : System.Text.Encoding.GetEncoding(850);
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = enc,
        })!;
        var read = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(60_000))
        {
            try { p.Kill(true); } catch { }
            return "El informe tardó más de 60 segundos y se canceló.";
        }
        return read.Wait(5000) ? read.Result : "";
    }

    private static async Task MsiAsync(bool on)
    {
        if (!Confirm(on ? "¿Activar el modo MSI en la GPU? Se guarda el valor original y puedes restaurarlo. Requiere reiniciar." : "¿Restaurar el modo de interrupciones original?")) return;
        var r = await CoreServiceClient.SendAsync(new CoreRequest { Op = on ? "msi-gpu-on" : "msi-gpu-off" });
        if (!r.Ok) { MessageBox.Show(r.Error ?? "Error", "GPU"); return; }
        RestartPrompt.Ask("Reinicia para aplicar el cambio", string.Join(" ", r.Messages));
    }

    private static void RepairIconCache()
    {
        if (!Confirm("Se borrará la caché de iconos (Windows la vuelve a crear) y se reiniciará el Explorador. ¿Continuar?")) return;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var files = new List<string> { Path.Combine(local, "IconCache.db") };
        var exp = Path.Combine(local, @"Microsoft\Windows\Explorer");
        if (Directory.Exists(exp)) files.AddRange(Directory.GetFiles(exp, "iconcache*.db").Concat(Directory.GetFiles(exp, "thumbcache*.db")));
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer")) { try { p.Kill(); p.WaitForExit(5000); } catch { } finally { p.Dispose(); } }
        var deleted = 0;
        foreach (var f in files) { try { File.Delete(f); deleted++; } catch { } }
        System.Diagnostics.Process.Start("explorer.exe");
        MessageBox.Show($"Caché de iconos reparada ({deleted} archivos).", "Herramientas");
    }

    // ------------------------------------------------------------------ Drivers

    private static Border DriversCard()
    {
        string Q(string cls, string prop)
        {
            try
            {
                using var s = new ManagementObjectSearcher($"SELECT {prop} FROM {cls}");
                return string.Join(", ", s.Get().Cast<ManagementBaseObject>().Select(o => o[prop]?.ToString()?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
            }
            catch { return "No disponible"; }
        }
        var board = $"{Q("Win32_BaseBoard", "Manufacturer")} {Q("Win32_BaseBoard", "Product")}".Trim();
        var gpu = Q("Win32_VideoController", "Name");
        var nic = Q("Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE", "Name");
        var audio = Q("Win32_SoundDevice", "Name");

        var links = Ui.Wrap(
            Link("AMD", "https://www.amd.com/es/support/download/drivers.html"),
            Link("NVIDIA", "https://www.nvidia.com/es-es/drivers/"),
            Link("Intel", "https://www.intel.es/content/www/es/es/support/detect.html"),
            Link("Realtek (red y audio)", "https://www.realtek.com/Download/Index"),
            Link("Windows Update", "ms-settings:windowsupdate"));
        var third = Ui.Wrap(
            Link("DDU: desinstalar drivers de vídeo/audio", "https://www.wagnardsoft.com/display-driver-uninstaller-ddu-"),
            Link("NVCleanstall: driver NVIDIA limpio", "https://www.techpowerup.com/nvcleanstall/"));

        return Ui.Card(Ui.Eyebrow("DRIVERS"), Ui.H2("Tu equipo"),
            Ui.Muted($"Placa base: {board}"), Ui.Muted($"Gráfica: {gpu}"), Ui.Muted($"Red: {nic}"), Ui.Muted($"Audio: {audio}"),
            Ui.Muted("Instala siempre los drivers desde la web oficial del fabricante de tu placa base o de cada componente."),
            links,
            Ui.Muted("Herramientas de terceros reconocidas (opcionales, se abren en su web oficial). Usa DDU en modo seguro antes de cambiar de driver de GPU."),
            third);
    }

    private static Button Link(string text, string url)
    {
        var b = Ui.Button(text, () => SystemTools.Start(url));
        b.Margin = new Thickness(0, 0, 8, 8);
        return b;
    }
}
