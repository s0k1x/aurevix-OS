using System.Text;
using System.Text.Json;
using Aurevix.Core;
using Aurevix.Update;

// aurevix-release: herramienta de publicación.
//   keygen   --out <dir> --key-id <id>
//   manifest --channel stable --version 0.2.0 --sequence 2 --installer <ruta> --installer-url <url>
//            [--notes <fichero.md>] [--title <t>] [--min-version 0.1.0] [--rollout 100] [--expires-days 30] --out manifest.json
//   sign     --manifest manifest.json --key <privada.pem | env:VAR> --key-id <id>
//   verify   --manifest manifest.json --public <publica.pem> --key-id <id>

var cmd = args.FirstOrDefault();
var opts = ParseOptions(args.Skip(1).ToArray());

try
{
    switch (cmd)
    {
        case "keygen":
        {
            var dir = Require(opts, "out");
            var keyId = Require(opts, "key-id");
            Directory.CreateDirectory(dir);
            var (priv, pub) = ManifestSigner.GenerateKeyPair();
            File.WriteAllText(Path.Combine(dir, $"{keyId}.private.pem"), priv);
            File.WriteAllText(Path.Combine(dir, $"{keyId}.public.pem"), pub);
            Console.WriteLine($"Claves generadas en {dir}. GUARDA la privada en un lugar seguro y NUNCA la subas al repositorio.");
            return 0;
        }
        case "manifest":
        {
            var installer = Require(opts, "installer");
            var info = new FileInfo(installer);
            if (!info.Exists) throw new FileNotFoundException(installer);
            var version = Require(opts, "version");
            SemVersion.Parse(version);
            var channel = Require(opts, "channel");
            if (!UpdateChannels.IsValid(channel)) throw new ArgumentException($"Canal no válido: {channel}");
            var now = DateTimeOffset.UtcNow;
            var manifest = new UpdateManifest
            {
                Channel = channel,
                Sequence = long.Parse(Require(opts, "sequence")),
                PublishedAt = now,
                ExpiresAt = now.AddDays(int.Parse(opts.GetValueOrDefault("expires-days", "30"))),
                Latest = new ReleaseInfo
                {
                    Version = version,
                    MinimumVersion = opts.GetValueOrDefault("min-version"),
                    Title = opts.GetValueOrDefault("title", $"AUREVIX OS {version}"),
                    Notes = opts.TryGetValue("notes", out var n) && File.Exists(n) ? File.ReadAllText(n) : opts.GetValueOrDefault("notes", ""),
                    RolloutPercent = int.Parse(opts.GetValueOrDefault("rollout", "100")),
                    Packages =
                    [
                        new PackageInfo
                        {
                            Id = "aurevix-app",
                            Kind = PackageKind.AppInstaller,
                            Url = Require(opts, "installer-url"),
                            Sha256 = await UpdateClient.ComputeSha256Async(installer),
                            Size = info.Length
                        }
                    ]
                }
            };
            var outPath = opts.GetValueOrDefault("out", "manifest.json");
            File.WriteAllText(outPath, JsonSerializer.Serialize(manifest, JsonDefaults.Options), new UTF8Encoding(false));
            Console.WriteLine($"Manifiesto escrito en {outPath}");
            return 0;
        }
        case "sign":
        {
            var manifest = Require(opts, "manifest");
            var keyArg = Require(opts, "key");
            var pem = keyArg.StartsWith("env:")
                ? Environment.GetEnvironmentVariable(keyArg[4..]) ?? throw new ArgumentException($"Variable {keyArg[4..]} vacía")
                : File.ReadAllText(keyArg);
            var sig = ManifestSigner.Sign(File.ReadAllBytes(manifest), pem, Require(opts, "key-id"));
            File.WriteAllText(manifest + ".sig", ManifestSigner.SerializeSignature(sig), new UTF8Encoding(false));
            Console.WriteLine($"Firma escrita en {manifest}.sig");
            return 0;
        }
        case "verify":
        {
            var manifest = Require(opts, "manifest");
            var sig = ManifestSigner.ParseSignature(File.ReadAllText(manifest + ".sig"))!;
            var keys = new Dictionary<string, string> { [Require(opts, "key-id")] = File.ReadAllText(Require(opts, "public")) };
            var ok = ManifestSigner.Verify(File.ReadAllBytes(manifest), sig, keys);
            Console.WriteLine(ok ? "Firma VÁLIDA" : "Firma NO válida");
            return ok ? 0 : 2;
        }
        default:
            Console.WriteLine("Uso: aurevix-release <keygen|manifest|sign|verify> [opciones]. Ver docs/RELEASING.md");
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static Dictionary<string, string> ParseOptions(string[] a)
{
    var d = new Dictionary<string, string>();
    for (var i = 0; i < a.Length; i++)
        if (a[i].StartsWith("--"))
            d[a[i][2..]] = i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[++i] : "true";
    return d;
}

static string Require(Dictionary<string, string> o, string k) =>
    o.TryGetValue(k, out var v) ? v : throw new ArgumentException($"Falta --{k}");
