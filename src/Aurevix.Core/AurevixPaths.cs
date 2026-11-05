namespace Aurevix.Core;

/// <summary>
/// Rutas estándar de AUREVIX. Los datos de usuario van a %LOCALAPPDATA%\Aurevix,
/// los datos compartidos (logs del instalador, paquetes) a %ProgramData%\Aurevix.
/// </summary>
public static class AurevixPaths
{
    public static string UserRoot { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aurevix");

    public static string Logs => Path.Combine(UserRoot, "logs");
    public static string Reports => Path.Combine(UserRoot, "reports");
    public static string Backups => Path.Combine(UserRoot, "backups");
    public static string Downloads => Path.Combine(UserRoot, "downloads");
    public static string SettingsFile => Path.Combine(UserRoot, "settings.json");
    public static string StateFile => Path.Combine(UserRoot, "state.json");

    /// <summary>Permite redirigir la raíz (pruebas o modo portable).</summary>
    public static void OverrideRoot(string root) => UserRoot = root;

    public static void EnsureCreated()
    {
        foreach (var dir in new[] { UserRoot, Logs, Reports, Backups, Downloads })
            Directory.CreateDirectory(dir);
    }
}
