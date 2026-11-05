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
        string[] pages = ["home", "diagnostics", "performance", "gameboost", "benchmark", "network", "profiles",
                          "optimization", "cleanup", "appearance", "updates", "recovery", "settings"];
        var i = 1;
        foreach (var p in pages)
        {
            main.Navigate(p);
            await Task.Delay(p is "optimization" or "network" or "cleanup" or "diagnostics" ? 6000 : 2500);
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
