using System.Security.Cryptography;
using System.Text.Json;
using Aurevix.Core;
using Aurevix.Core.Logging;

namespace Aurevix.Update;

public sealed class UpdateClientOptions
{
    /// <summary>URL base de los manifiestos. Se añade {canal}/manifest.json y {canal}/manifest.json.sig.</summary>
    public required Uri ManifestBaseUrl { get; init; }
    /// <summary>Solo se descargan paquetes de estos hosts (además de HTTPS obligatorio).</summary>
    public required IReadOnlyCollection<string> AllowedPackageHosts { get; init; }
    public required IReadOnlyDictionary<string, string> TrustedKeys { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public long MaxManifestBytes { get; init; } = 256 * 1024;
    public long MaxPackageBytes { get; init; } = 1024L * 1024 * 1024;
}

public enum UpdateStatus { UpToDate, Available, NotEligibleYet, RequiresManualUpdate, Error }

public sealed record UpdateCheckResult(UpdateStatus Status, ReleaseInfo? Release, UpdateManifest? Manifest, string Message);

public static class UpdateChannels
{
    public static readonly string[] All = ["stable", "beta", "developer"];
    public static bool IsValid(string c) => All.Contains(c);
}

/// <summary>
/// Cliente de actualizaciones. Solo hace peticiones salientes HTTPS, verifica la firma
/// del manifiesto, su caducidad y secuencia, y el hash/tamaño de cada paquete.
/// Nunca ejecuta comandos recibidos del servidor: la instalación usa argumentos fijos.
/// </summary>
public sealed class UpdateClient(UpdateClientOptions options, HttpClient http)
{
    public async Task<UpdateCheckResult> CheckAsync(SemVersion current, string channel, string installId,
        long lastSeenSequence = 0, DateTimeOffset? now = null, CancellationToken ct = default)
    {
        if (!UpdateChannels.IsValid(channel))
            return new(UpdateStatus.Error, null, null, $"Canal desconocido: {channel}");
        try
        {
            var baseUrl = options.ManifestBaseUrl.ToString().TrimEnd('/');
            var manifestBytes = await GetBytesLimitedAsync(new Uri($"{baseUrl}/{channel}/manifest.json"), options.MaxManifestBytes, ct);
            var sigBytes = await GetBytesLimitedAsync(new Uri($"{baseUrl}/{channel}/manifest.json.sig"), 16 * 1024, ct);
            return Evaluate(manifestBytes, System.Text.Encoding.UTF8.GetString(sigBytes), current, channel, installId, lastSeenSequence, now ?? DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            AurevixLog.Warn($"Comprobación de actualizaciones fallida: {ex.Message}");
            return new(UpdateStatus.Error, null, null, "No se pudo contactar con el servidor de actualizaciones.");
        }
    }

    /// <summary>Validación pura del manifiesto (separada para poder probarla sin red).</summary>
    public UpdateCheckResult Evaluate(byte[] manifestBytes, string signatureJson, SemVersion current, string channel,
        string installId, long lastSeenSequence, DateTimeOffset now)
    {
        ManifestSignature? sig;
        try { sig = ManifestSigner.ParseSignature(signatureJson); } catch (JsonException) { sig = null; }
        if (sig is null || !ManifestSigner.Verify(manifestBytes, sig, options.TrustedKeys))
            return new(UpdateStatus.Error, null, null, "La firma del manifiesto no es válida. Se ignora la actualización.");

        UpdateManifest? m;
        try { m = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes, JsonDefaults.Options); }
        catch (JsonException) { m = null; }
        if (m is null || m.SchemaVersion != 1 || m.Product != "aurevix-os")
            return new(UpdateStatus.Error, null, null, "Manifiesto con formato no compatible.");
        if (m.Channel != channel)
            return new(UpdateStatus.Error, null, m, "El manifiesto no corresponde al canal configurado.");
        if (m.ExpiresAt < now)
            return new(UpdateStatus.Error, null, m, "El manifiesto ha caducado.");
        if (m.Sequence < lastSeenSequence)
            return new(UpdateStatus.Error, null, m, "El manifiesto es más antiguo que uno ya visto (posible downgrade). Se ignora.");

        var r = m.Latest;
        if (r is null || !SemVersion.TryParse(r.Version, out var latest))
            return new(UpdateStatus.UpToDate, null, m, "No hay versiones publicadas.");
        if (latest! <= current)
            return new(UpdateStatus.UpToDate, r, m, $"AUREVIX está al día ({current}).");

        foreach (var p in r.Packages)
        {
            var problem = ValidatePackage(p);
            if (problem is not null) return new(UpdateStatus.Error, null, m, problem);
        }

        if (r.MinimumVersion is not null && SemVersion.TryParse(r.MinimumVersion, out var min) && current < min!)
            return new(UpdateStatus.RequiresManualUpdate, r, m,
                $"Tu versión ({current}) es demasiado antigua para actualizar directamente. Descarga el instalador desde la web.");

        if (!IsInRollout(installId, r.Version, r.RolloutPercent))
            return new(UpdateStatus.NotEligibleYet, r, m, "La actualización se está desplegando gradualmente; llegará pronto.");

        return new(UpdateStatus.Available, r, m, $"Nueva versión disponible: {r.Version}");
    }

    public string? ValidatePackage(PackageInfo p)
    {
        if (!Uri.TryCreate(p.Url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
            return $"El paquete {p.Id} no usa HTTPS.";
        if (!options.AllowedPackageHosts.Contains(u.Host, StringComparer.OrdinalIgnoreCase))
            return $"El paquete {p.Id} procede de un host no autorizado ({u.Host}).";
        if (p.Sha256.Length != 64 || !p.Sha256.All(Uri.IsHexDigit))
            return $"El paquete {p.Id} no tiene un hash SHA-256 válido.";
        if (p.Size <= 0 || p.Size > options.MaxPackageBytes)
            return $"El paquete {p.Id} tiene un tamaño no válido.";
        return null;
    }

    /// <summary>Despliegue gradual determinista: el mismo equipo obtiene siempre el mismo resultado para una versión.</summary>
    public static bool IsInRollout(string installId, string version, int percent)
    {
        if (percent >= 100) return true;
        if (percent <= 0) return false;
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{installId}:{version}"));
        var bucket = BitConverter.ToUInt32(hash, 0) % 100;
        return bucket < percent;
    }

    /// <summary>Descarga un paquete a destino verificando tamaño y SHA-256. Si falla, borra el fichero parcial.</summary>
    public async Task<string> DownloadAsync(PackageInfo p, string directory, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var problem = ValidatePackage(p);
        if (problem is not null) throw new InvalidOperationException(problem);

        Directory.CreateDirectory(directory);
        var fileName = Path.GetFileName(new Uri(p.Url).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            fileName = p.Id + ".bin";
        var final = Path.Combine(directory, fileName);
        var partial = final + ".partial";

        try
        {
            using var resp = await http.GetAsync(p.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(partial))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > p.Size) throw new InvalidDataException("El paquete es mayor de lo declarado en el manifiesto.");
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress?.Report((double)total / p.Size);
                }
                if (total != p.Size) throw new InvalidDataException("El tamaño descargado no coincide con el manifiesto.");
            }

            var hash = await ComputeSha256Async(partial, ct);
            if (!string.Equals(hash, p.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El hash SHA-256 del paquete no coincide. Se descarta.");

            File.Move(partial, final, overwrite: true);
            AurevixLog.Info($"Paquete {p.Id} descargado y verificado.");
            return final;
        }
        catch
        {
            try { File.Delete(partial); } catch { }
            throw;
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<byte[]> GetBytesLimitedAsync(Uri url, long max, CancellationToken ct)
    {
        if (url.Scheme != Uri.UriSchemeHttps) throw new HttpRequestException("Solo se permite HTTPS.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(options.Timeout);
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync(cts.Token);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await s.ReadAsync(buffer, cts.Token)) > 0)
        {
            if (ms.Length + read > max) throw new IOException("Respuesta demasiado grande.");
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }
}
