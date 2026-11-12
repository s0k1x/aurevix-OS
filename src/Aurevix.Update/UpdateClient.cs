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
    /// <summary>Esperas entre reintentos ante fallos de red (un reintento por elemento).</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(15)];
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
        {
            AurevixLog.Warn($"Canal desconocido: {channel}");
            return new(UpdateStatus.Error, null, null, "El canal de actualizaciones elegido no existe. Vuelve a «Estable» en Ajustes.");
        }
        try
        {
            var baseUrl = options.ManifestBaseUrl.ToString().TrimEnd('/');
            var manifestBytes = await WithRetryAsync(t => GetBytesLimitedAsync(new Uri($"{baseUrl}/{channel}/manifest.json"), options.MaxManifestBytes, t), ct);
            var sigBytes = await WithRetryAsync(t => GetBytesLimitedAsync(new Uri($"{baseUrl}/{channel}/manifest.json.sig"), 16 * 1024, t), ct);
            return Evaluate(manifestBytes, System.Text.Encoding.UTF8.GetString(sigBytes), current, channel, installId, lastSeenSequence, now ?? DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
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
        {
            AurevixLog.Warn("Firma del manifiesto inválida o clave no confiable. Se ignora la actualización.");
            return new(UpdateStatus.Error, null, null, "La actualización no tiene una firma válida de AUREVIX y no se instalará.");
        }

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
        {
            AurevixLog.Warn($"Manifiesto con secuencia {m.Sequence} < {lastSeenSequence} (posible downgrade). Se ignora.");
            return new(UpdateStatus.Error, null, m, "Esta versión es más antigua que la que tienes; no se instala.");
        }

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

    /// <summary>
    /// Descarga un paquete a destino verificando tamaño y SHA-256. Ante cortes de red conserva el .partial y
    /// reanuda con Range; solo lo borra si el tamaño o el hash no cuadran.
    /// </summary>
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
            await WithRetryAsync(t => DownloadChunkAsync(p, partial, progress, t), ct);

            var hash = await ComputeSha256Async(partial, ct);
            if (!string.Equals(hash, p.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                AurevixLog.Warn($"Hash SHA-256 de {p.Id} no coincide (esperado {p.Sha256}, obtenido {hash}). Se descarta.");
                throw new InvalidDataException(DamagedDownloadMessage);
            }

            File.Move(partial, final, overwrite: true);
            AurevixLog.Info($"Paquete {p.Id} descargado y verificado.");
            return final;
        }
        catch (InvalidDataException)
        {
            // Tamaño o hash incorrectos: el parcial no es fiable.
            try { File.Delete(partial); } catch { }
            throw;
        }
        // Resto de errores (red, cancelación): se conserva el .partial para reanudar.
    }

    private const string DamagedDownloadMessage = "La descarga llegó dañada. Se volverá a intentar más tarde.";

    private async Task DownloadChunkAsync(PackageInfo p, string partial, IProgress<double>? progress, CancellationToken ct)
    {
        long existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (existing > p.Size) { File.Delete(partial); existing = 0; }

        if (existing < p.Size)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, p.Url);
            if (existing > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                try { File.Delete(partial); } catch { }
                throw new IOException("El servidor rechazó el rango; se empieza de cero.");
            }
            resp.EnsureSuccessStatusCode();

            // 206 con el inicio esperado: se añade; 200 (sin soporte de rangos): de cero.
            var resume = existing > 0 && resp.StatusCode == System.Net.HttpStatusCode.PartialContent
                && resp.Content.Headers.ContentRange?.From == existing;
            if (!resume) existing = 0;

            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(partial, resume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
            var buffer = new byte[81920];
            long total = existing;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > p.Size)
                {
                    AurevixLog.Warn($"El paquete {p.Id} es mayor de lo declarado en el manifiesto.");
                    throw new InvalidDataException(DamagedDownloadMessage);
                }
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                progress?.Report((double)total / p.Size);
            }
            await dst.FlushAsync(ct);
            if (total < p.Size)
            {
                // Corte sin error explícito: se conserva lo descargado y se reintenta.
                throw new IOException("Conexión cortada antes de terminar la descarga.");
            }
        }
    }

    /// <summary>Reintenta ante errores de red, HTTP 5xx o timeout. Nunca reintenta firma/hash/tamaño ni cancelaciones del usuario.</summary>
    private async Task<T> WithRetryAsync<T>(Func<CancellationToken, Task<T>> op, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await op(ct); }
            catch (Exception ex) when (attempt < options.RetryDelays.Count && IsTransient(ex, ct))
            {
                AurevixLog.Warn($"Fallo de red (intento {attempt + 1}): {ex.Message}. Reintentando.");
                await Task.Delay(options.RetryDelays[attempt], ct);
            }
        }
    }

    private Task WithRetryAsync(Func<CancellationToken, Task> op, CancellationToken ct) =>
        WithRetryAsync<bool>(async t => { await op(t); return true; }, ct);

    private static bool IsTransient(Exception ex, CancellationToken ct) => ex switch
    {
        _ when ct.IsCancellationRequested => false,
        InvalidDataException => false,
        HttpRequestException h => h.StatusCode is null || (int)h.StatusCode >= 500,
        TaskCanceledException => true, // timeout (no cancelación del llamador)
        IOException => true,
        _ => false,
    };

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
            if (ms.Length + read > max) throw new InvalidDataException("Respuesta demasiado grande.");
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }
}
