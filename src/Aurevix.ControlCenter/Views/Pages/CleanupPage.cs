using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core.Logging;
using Microsoft.Win32;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>
/// Limpieza: aplicaciones preinstaladas (por usuario, reinstalables desde la Store), programas de inicio
/// (mismo mecanismo StartupApproved que usa el Administrador de tareas) y archivos temporales.
/// </summary>
public sealed class CleanupPage : UserControl
{
    private sealed record Bloat(string Package, string Name, bool Suggested);

    private static readonly Bloat[] Catalog =
    [
        new("Microsoft.BingNews", "Noticias", true),
        new("Microsoft.BingWeather", "El Tiempo", false),
        new("Microsoft.BingSearch", "Búsqueda de Bing", true),
        new("Microsoft.GetHelp", "Obtener ayuda", true),
        new("Microsoft.Getstarted", "Consejos", true),
        new("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", true),
        new("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office) promocional", true),
        new("Microsoft.People", "Contactos", true),
        new("Microsoft.PowerAutomateDesktop", "Power Automate", true),
        new("Microsoft.Todos", "Microsoft To Do", false),
        new("Microsoft.WindowsFeedbackHub", "Centro de opiniones", true),
        new("Microsoft.WindowsMaps", "Mapas", true),
        new("Clipchamp.Clipchamp", "Clipchamp", true),
        new("Microsoft.549981C3F5F10", "Cortana", true),
        new("Microsoft.Copilot", "Copilot", false),
        new("Microsoft.YourPhone", "Vincular teléfono", false),
        new("Microsoft.OutlookForWindows", "Outlook (nuevo)", false),
        new("Microsoft.Windows.DevHome", "Dev Home", true),
        new("MSTeams", "Microsoft Teams (personal)", false),
        new("Microsoft.MicrosoftStickyNotes", "Notas rápidas", false),
        new("Microsoft.XboxApp", "Xbox (antigua)", false),
    ];

    private readonly StackPanel _apps = new();
    private readonly StackPanel _startup = new();
    private readonly Dictionary<string, CheckBox> _appChecks = new();

    public CleanupPage()
    {
        var (root, body) = Ui.Page("Limpieza", "Menos procesos y menos ruido. Todo es reversible o reinstalable.");

        body.Children.Add(Ui.Card(Ui.Eyebrow("APLICACIONES PREINSTALADAS"), Ui.H2("Quitar aplicaciones que no usas"),
            Ui.Muted("Se eliminan solo para tu usuario. Cualquiera puede volver a instalarse gratis desde Microsoft Store. " +
                     "No se tocan componentes de Windows, Edge, la Store, Xbox Game Bar ni la seguridad."),
            _apps, Ui.Row(Ui.Button("Quitar las seleccionadas", () => _ = RemoveAppsAsync(), primary: true))));

        body.Children.Add(Ui.Card(Ui.Eyebrow("INICIO DE WINDOWS"), Ui.H2("Programas que arrancan con Windows"),
            Ui.Muted("Desactivar no desinstala nada: el programa simplemente no se abre al iniciar sesión (igual que en el Administrador de tareas)."),
            _startup, Ui.Row(Ui.Button("Abrir el Administrador de tareas", () => SystemTools.Start("taskmgr.exe")))));

        body.Children.Add(Ui.Card(Ui.Eyebrow("ARCHIVOS TEMPORALES"), Ui.H2("Vaciar archivos temporales"),
            Ui.Muted("Borra la carpeta temporal de tu usuario. Los archivos en uso se omiten."),
            Ui.Row(Ui.Button("Vaciar temporales", CleanTemp), Ui.Button("Abrir Sensor de almacenamiento", () => SystemTools.Start("ms-settings:storagesense")))));

        Content = root;
        Loaded += async (_, _) => { await LoadAppsAsync(); LoadStartup(); };
    }

    // ------------------------------------------------------------------ Apps

    private static async Task<string> PsAsync(string script)
    {
        var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference='SilentlyContinue'; " + script));
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return output.Trim();
    }

    private async Task LoadAppsAsync()
    {
        _apps.Children.Clear();
        _apps.Children.Add(Ui.Muted("Buscando aplicaciones instaladas…"));
        var names = string.Join(",", Catalog.Select(b => $"'{b.Package}'"));
        var json = await PsAsync($"@(Get-AppxPackage | Where-Object {{ @({names}) -contains $_.Name }} | Select-Object -ExpandProperty Name -Unique) | ConvertTo-Json -Compress");
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array) foreach (var e in doc.RootElement.EnumerateArray()) installed.Add(e.GetString()!);
                else if (doc.RootElement.ValueKind == JsonValueKind.String) installed.Add(doc.RootElement.GetString()!);
            }
        }
        catch (JsonException) { }

        _apps.Children.Clear();
        _appChecks.Clear();
        var present = Catalog.Where(b => installed.Contains(b.Package)).ToList();
        if (present.Count == 0) { _apps.Children.Add(Ui.Muted("No queda ninguna aplicación preinstalada de la lista.")); return; }
        var wrap = Ui.Wrap();
        foreach (var b in present)
        {
            var cb = new CheckBox { Content = b.Name, IsChecked = b.Suggested, Width = 270 };
            _appChecks[b.Package] = cb;
            wrap.Children.Add(cb);
        }
        _apps.Children.Add(wrap);
    }

    private async Task RemoveAppsAsync()
    {
        var sel = _appChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        if (sel.Count == 0) return;
        var names = string.Join("\n", Catalog.Where(b => sel.Contains(b.Package)).Select(b => "• " + b.Name));
        if (MessageBox.Show($"Se quitarán para tu usuario:\n\n{names}\n\nPodrás reinstalarlas desde Microsoft Store. ¿Continuar?",
                "Limpieza", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var list = string.Join(",", sel.Select(s => $"'{s}'"));
        await PsAsync($"Get-AppxPackage | Where-Object {{ @({list}) -contains $_.Name }} | ForEach-Object {{ try {{ Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop }} catch {{ }} }}");
        AurevixLog.Info($"Apps quitadas: {string.Join(", ", sel)}");
        await LoadAppsAsync();
    }

    // ------------------------------------------------------------------ Inicio

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private void LoadStartup()
    {
        _startup.Children.Clear();
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedRun);
        var names = run?.GetValueNames().Where(n => !string.Equals(n, "AurevixControlCenter", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        if (names.Count == 0) _startup.Children.Add(Ui.Muted("No hay programas de inicio de tu usuario."));
        foreach (var n in names)
        {
            var enabled = approved?.GetValue(n) is not byte[] b || b.Length == 0 || (b[0] & 1) == 0; // 02/06 = activado, 03/07 = desactivado
            var cb = new CheckBox { Content = n, IsChecked = enabled };
            cb.Checked += (_, _) => SetStartup(n, true);
            cb.Unchecked += (_, _) => SetStartup(n, false);
            _startup.Children.Add(cb);
        }
        using var lm = Registry.LocalMachine.OpenSubKey(RunKey);
        var system = lm?.GetValueNames().Length ?? 0;
        if (system > 0) _startup.Children.Add(Ui.Muted($"Además hay {system} programa(s) de inicio para todos los usuarios: gestiónalos en el Administrador de tareas > Aplicaciones de arranque."));
    }

    private static void SetStartup(string name, bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(ApprovedRun, true);
        var data = new byte[12];
        data[0] = (byte)(enabled ? 0x02 : 0x03);
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        k.SetValue(name, data, RegistryValueKind.Binary);
        AurevixLog.Info($"Inicio: {name} → {(enabled ? "activado" : "desactivado")}");
    }

    // ------------------------------------------------------------------ Temporales

    private static void CleanTemp()
    {
        var dir = Path.GetTempPath();
        long freed = 0; int files = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            try
            {
                var info = new FileInfo(f);
                if (info.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-2)) continue; // no tocar lo recién creado
                var len = info.Length;
                info.Delete();
                freed += len; files++;
            }
            catch { }
        }
        MessageBox.Show($"Se liberaron {freed / 1024d / 1024:0} MB ({files} archivos).", "Limpieza");
    }
}
