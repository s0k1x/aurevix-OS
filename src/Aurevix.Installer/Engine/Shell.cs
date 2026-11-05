using System.Diagnostics;
using System.IO;
using System.Text;
using Aurevix.Core.Logging;

namespace Aurevix.Installer.Engine;

public sealed record ShellResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Ejecuta herramientas oficiales de Windows (PowerShell, DISM, robocopy, setup.exe) y registra todo.</summary>
public static class Shell
{
    /// <summary>Ejecuta un script de PowerShell sin problemas de comillas (-EncodedCommand).</summary>
    public static Task<ShellResult> PowerShellAsync(string script, CancellationToken ct = default, Action<string>? onLine = null)
    {
        var wrapped = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " +
                      "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));
        return RunAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", ct, onLine);
    }

    public static async Task<ShellResult> RunAsync(string file, string args, CancellationToken ct = default, Action<string>? onLine = null)
    {
        AurevixLog.Info($"> {file} {(args.Contains("-EncodedCommand") ? "(script PowerShell)" : args)}");
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        using var p = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var error = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is null) return; output.AppendLine(e.Data); onLine?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var r = new ShellResult(p.ExitCode, output.ToString().Trim(), error.ToString().Trim());
        if (!r.Ok) AurevixLog.Warn($"{Path.GetFileName(file)} terminó con {r.ExitCode}: {Trunc(r.Error.Length > 0 ? r.Error : r.Output)}");
        return r;
    }

    public static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

    private static string Trunc(string s) => s.Length > 600 ? s[..600] + "…" : s;
}
