using System.Globalization;
using System.IO;
using System.Text;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Services;

/// <summary>Genera un archivo .reg con los valores ORIGINALES que AUREVIX guardó antes de cambiar nada.</summary>
public static class RegistryExport
{
    /// <summary>Devuelve la ruta del archivo creado y cuántos valores contiene.</summary>
    public static (string Path, int Values) ExportOriginals(IEnumerable<ChangeBackup> backups, string folder)
    {
        // Las copias vienen de la más reciente a la más antigua: para cada valor gana la más antigua (el original real).
        var first = new Dictionary<string, RegistryBackupEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in backups.OrderBy(b => b.CreatedAtUtc).SelectMany(b => b.Registry))
        {
            var key = $"{Root(e.Hive)}\\{e.Key.Trim('\\')}";
            first.TryAdd($"{key}|{e.Name}", e);
        }

        var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
        foreach (var group in first.Values.GroupBy(e => $"{Root(e.Hive)}\\{e.Key.Trim('\\')}", StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("\r\n[").Append(group.Key).Append("]\r\n");
            foreach (var e in group) sb.Append(Line(e)).Append("\r\n");
        }

        Directory.CreateDirectory(folder);
        var path = System.IO.Path.Combine(folder, $"AUREVIX-copia-registro-{DateTime.Now:yyyyMMdd-HHmm}.reg");
        File.WriteAllText(path, sb.ToString(), Encoding.Unicode); // UTF-16 LE con BOM
        return (path, first.Count);
    }

    private static string Root(RegistryHive hive) =>
        hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Line(RegistryBackupEntry e)
    {
        var name = e.Name.Length == 0 ? "@" : $"\"{Escape(e.Name)}\"";
        if (!e.Existed) return $"{name}=-";
        if (e.Kind == RegistryValueKind.DWord)
        {
            var raw = e.PreviousValue ?? "0";
            var v = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? unchecked((uint)i)
                  : uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? u : 0u;
            return $"{name}=dword:{v:x8}";
        }
        return $"{name}=\"{Escape(e.PreviousValue ?? "")}\"";
    }
}
