using Aurevix.Core;

namespace Aurevix.Update;

/// <summary>
/// Configuración del servidor. El manifiesto vive en Supabase Storage (público, solo lectura).
/// Se puede sobrescribir con %ProgramData%\Aurevix\update.json — la firma sigue siendo obligatoria.
/// </summary>
public sealed class UpdateEndpointConfig
{
    public string ManifestBaseUrl { get; set; } = "https://zttqiphwurnferzlubzy.supabase.co/storage/v1/object/public/releases/manifests";
    public List<string> AllowedPackageHosts { get; set; } = ["zttqiphwurnferzlubzy.supabase.co", "github.com", "objects.githubusercontent.com"];

    public static UpdateEndpointConfig Load()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Aurevix", "update.json");
        return File.Exists(path) ? JsonStore.LoadOrDefault<UpdateEndpointConfig>(path) : new UpdateEndpointConfig();
    }
}
