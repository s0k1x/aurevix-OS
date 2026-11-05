using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Aurevix.Core;

namespace Aurevix.Update;

/// <summary>
/// Firma y verificación ECDSA P-256 / SHA-256 sobre los bytes exactos del manifiesto.
/// Las claves públicas de confianza van incrustadas en el cliente (Keys/*.public.pem);
/// la rotación consiste en publicar una versión del cliente que incluya la nueva clave
/// antes de empezar a firmar con ella.
/// </summary>
public static class ManifestSigner
{
    public const string Algorithm = "ECDSA-P256-SHA256";

    public static (string privatePem, string publicPem) GenerateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    public static ManifestSignature Sign(byte[] manifestBytes, string privatePem, string keyId)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privatePem);
        var sig = key.SignData(manifestBytes, HashAlgorithmName.SHA256);
        return new ManifestSignature { KeyId = keyId, Algorithm = Algorithm, Signature = Convert.ToBase64String(sig) };
    }

    public static bool Verify(byte[] manifestBytes, ManifestSignature signature, IReadOnlyDictionary<string, string> trustedKeys)
    {
        if (signature.Algorithm != Algorithm) return false;
        if (!trustedKeys.TryGetValue(signature.KeyId, out var pem)) return false;
        byte[] sig;
        try { sig = Convert.FromBase64String(signature.Signature); } catch (FormatException) { return false; }
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key.VerifyData(manifestBytes, sig, HashAlgorithmName.SHA256);
    }

    public static string SerializeSignature(ManifestSignature s) => JsonSerializer.Serialize(s, JsonDefaults.Options);
    public static ManifestSignature? ParseSignature(string json) => JsonSerializer.Deserialize<ManifestSignature>(json, JsonDefaults.Options);

    /// <summary>
    /// Claves públicas incrustadas en el ensamblado. El nombre del fichero (sin .public.pem) es el keyId.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoadEmbeddedTrustedKeys()
    {
        var asm = typeof(ManifestSigner).Assembly;
        const string prefix = "Aurevix.Update.Keys.";
        const string suffix = ".public.pem";
        var result = new Dictionary<string, string>();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix) || !name.EndsWith(suffix)) continue;
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            result[name[prefix.Length..^suffix.Length]] = reader.ReadToEnd();
        }
        return result;
    }
}
