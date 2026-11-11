using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aurevix.ControlCenter.Views;
using Aurevix.Update;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// Modo de capturas (--render-screenshots &lt;carpeta&gt;): abre cada pantalla y la guarda como PNG.
/// Lo usa el workflow "Capturas" en un Windows real de GitHub Actions para revisar el diseño.
/// No cambia nada del sistema: solo abre ventanas y las renderiza.
/// </summary>
public static class Screenshotter
{
    public static async Task RunAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        Ui.ReduceMotion = true; // sin animaciones: capturas estables

        var main = new MainWindow { Left = 0, Top = 0, ShowActivated = false };
        main.Show();
        string[] pages = ["home", "diagnostics", "optimization", "gameboost", "benchmark", "network", "performance", "profiles",
                          "appearance", "cleanup", "tools", "apps", "updates", "recovery", "settings"];
        var i = 1;
        foreach (var p in pages)
        {
            main.Navigate(p);
            await Task.Delay(p is "optimization" or "network" or "cleanup" or "diagnostics" or "tools" ? 6000 : 2500);
            Save(main, Path.Combine(outDir, $"cc-{i++:00}-{p}.png"));
        }
        // Página larga: también la parte inferior de Optimización.
        main.Close();

        var wizard = new FirstRunWindow { ShowActivated = false };
        wizard.Show();
        await Task.Delay(1500);
        Save(wizard, Path.Combine(outDir, "cc-20-asistente-bienvenida.png"));
        wizard.Close();

        var upd = new UpdateWindow(new ReleaseInfo
        {
            Version = "0.4.0", Title = "AUREVIX OS 0.4.0",
            Notes = "- Nuevos ajustes de rendimiento\n- Mejoras en Game Boost\n- Correcciones"
        }) { ShowActivated = false, Topmost = false };
        upd.Show();
        await Task.Delay(1200);
        Save(upd, Path.Combine(outDir, "cc-21-actualizacion.png"));
        upd.Close();

        var rp = new RestartPrompt("Reinicia para aplicarlo todo", "Algunos ajustes de sistema solo se activan por completo tras reiniciar.") { ShowActivated = false, Topmost = false };
        rp.Show();
        await Task.Delay(1000);
        Save(rp, Path.Combine(outDir, "cc-22-reinicio.png"));
        rp.Close();
    }

    // ------------------------------------------------------------------ Escritorio real (antes / después del Estilo AUREVIX)

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    private static void CaptureScreen(string path)
    {
        var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var bmp = new System.Drawing.Bitmap(b.Width, b.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, System.Drawing.Point.Empty, b.Size);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void Open(string target) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });

    private static void Kill(string name)
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(name)) { try { p.Kill(); } catch { } finally { p.Dispose(); } }
    }

    private static void StartMenu()
    {
        keybd_event(0x5B, 0, 0, UIntPtr.Zero); // tecla Windows
        keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }

    /// <summary>Captura el escritorio de Windows antes y después de aplicar el Estilo AUREVIX (solo en máquinas de pruebas).</summary>
    public static async Task RenderDesktopAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        await Task.Delay(3000);
        CaptureScreen(Path.Combine(outDir, "win-01-antes-escritorio.png"));
        StartMenu(); await Task.Delay(2500);
        CaptureScreen(Path.Combine(outDir, "win-02-antes-inicio.png"));
        StartMenu(); await Task.Delay(1000);

        await AurevixStyle.ApplyAsync(includeLockScreen: false);
        await Task.Delay(6000);
        Kill("SystemSettings");
        AurevixStyle.RestartExplorer();
        await Task.Delay(9000);
        CaptureScreen(Path.Combine(outDir, "win-03-despues-escritorio.png"));
        StartMenu(); await Task.Delay(2500);
        CaptureScreen(Path.Combine(outDir, "win-04-despues-inicio.png"));
        StartMenu(); await Task.Delay(1000);

        Open("shell:MyComputerFolder"); await Task.Delay(5000);
        CaptureScreen(Path.Combine(outDir, "win-05-despues-explorador.png"));
        Kill("explorer"); await Task.Delay(2000);
        if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0) Open("explorer.exe");
        await Task.Delay(5000);

        Open("ms-settings:personalization"); await Task.Delay(7000);
        CaptureScreen(Path.Combine(outDir, "win-06-despues-configuracion.png"));
        Kill("SystemSettings");

        var main = new MainWindow { Left = 80, Top = 60 };
        main.Show(); main.Activate();
        await Task.Delay(3000);
        CaptureScreen(Path.Combine(outDir, "win-07-control-center-en-escritorio.png"));
        main.Close();
    }

    /// <summary>
    /// «Con todo instalado»: Estilo AUREVIX completo (tema claro, fondo, iconos) + AUREVIX Shell activo, y capturas del
    /// escritorio, el menú Inicio, el Explorador, Configuración y el Control Center (solo en máquinas de pruebas).
    /// </summary>
    public static async Task RenderInstalledAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new List<string>();
        var r = await AurevixStyle.ApplyAsync(includeLockScreen: false);
        log.Add($"aplicado: {string.Join(", ", r.Applied)}"); log.Add($"omitido: {string.Join(", ", r.Skipped)}");
        AppServices.Settings.ShellEnabled = true;
        AurevixShell.Start();
        Kill("SystemSettings");
        AurevixStyle.RestartExplorer();
        await Task.Delay(15000);
        CaptureScreen(Path.Combine(outDir, "todo-01-escritorio.png"));
        StartMenu(); await Task.Delay(4000);
        CaptureScreen(Path.Combine(outDir, "todo-02-inicio.png"));
        StartMenu(); await Task.Delay(1500);

        Open("shell:MyComputerFolder"); await Task.Delay(6000);
        CaptureScreen(Path.Combine(outDir, "todo-03-este-equipo.png"));
        Open("shell:UsersFilesFolder"); await Task.Delay(6000);
        CaptureScreen(Path.Combine(outDir, "todo-04-carpeta-usuario.png"));
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
            try { if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0) p.CloseMainWindow(); } catch { }
        await Task.Delay(2000);

        Open("ms-settings:personalization"); await Task.Delay(9000);
        CaptureScreen(Path.Combine(outDir, "todo-05-configuracion.png"));
        Open("ms-settings:colors"); await Task.Delay(5000);
        CaptureScreen(Path.Combine(outDir, "todo-06-configuracion-colores.png"));
        Kill("SystemSettings"); await Task.Delay(1500);

        var main = new MainWindow { Left = 60, Top = 40 };
        main.Show(); main.Activate();
        await Task.Delay(4000);
        CaptureScreen(Path.Combine(outDir, "todo-07-control-center.png"));
        main.Close();
        log.Add($"shell: {AurevixShell.LastResult}");
        File.WriteAllLines(Path.Combine(outDir, "todo-info.txt"), log);
    }

    public static void Save(Window w, string path)
    {
        w.UpdateLayout();
        var el = (FrameworkElement)w.Content;
        var width = (int)Math.Ceiling(el.ActualWidth);
        var height = (int)Math.Ceiling(el.ActualHeight);
        if (width <= 0 || height <= 0) return;
        const double dpi = 144; // 1,5x para nitidez
        var scale = dpi / 96;
        var rtb = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), dpi, dpi, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            ctx.DrawRectangle(w.Background ?? Brushes.Black, null, new Rect(0, 0, width, height));
        }
        rtb.Render(dv);
        rtb.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
