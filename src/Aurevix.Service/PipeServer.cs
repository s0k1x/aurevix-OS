using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Aurevix.Core.Logging;
using Aurevix.Diagnostics;
using Aurevix.Performance;

namespace Aurevix.Service;

public sealed class PipeServer : BackgroundService
{
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Aurevix", "service");
    private CoreRequestHandler? _handler;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        SecureDirectory(Root);
        AurevixLog.Initialize(Path.Combine(Root, "logs"));
        AurevixLog.Info("AUREVIX Core Service iniciado.");

        var catalog = TweakEngine.LoadCatalog();
        var system = SystemProfileDetector.Detect();
        var power = new PowercfgPowerPlanAccess();
        var tasks = new WindowsTaskAccess();
        var build = Environment.OSVersion.Version.Build;
        _handler = new CoreRequestHandler(catalog, sid => new TweakEngine(new TweakContext
        {
            Registry = new WindowsRegistryAccess { UserSid = sid },
            PowerSettings = power,
            Tasks = tasks,
            BackupDirectory = Path.Combine(Root, "backups"),
            WindowsBuild = build,
            Scope = TweakScope.Machine,
            System = system
        }), new MaintenanceRunner(), new NetworkConfigurator(Root), new DeviceConfigurator(Root));

        while (!ct.IsCancellationRequested)
        {
            var pipe = NamedPipeServerStreamAcl.Create(CoreServiceProtocol.PipeName, PipeDirection.InOut, 4,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, PipeAcl());
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                _ = Task.Run(() => ServeAsync(pipe, ct), ct);
            }
            catch (OperationCanceledException) { pipe.Dispose(); break; }
            catch (Exception ex) { AurevixLog.Error("Error aceptando conexión", ex); pipe.Dispose(); }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var _ = pipe;
        try
        {
            string? sid = null;
            pipe.RunAsClient(() => sid = WindowsIdentity.GetCurrent(true)?.User?.Value);
            var buffer = new byte[CoreServiceProtocol.MaxMessageBytes + 1];
            var read = 0;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (read < buffer.Length)
            {
                var n = await pipe.ReadAsync(buffer.AsMemory(read), timeout.Token);
                if (n == 0) break;
                read += n;
                if (buffer[read - 1] == (byte)'\n') break;
            }
            var request = Encoding.UTF8.GetString(buffer, 0, read).Trim();
            var response = _handler!.HandleJson(request, sid ?? "");
            var bytes = Encoding.UTF8.GetBytes(response + "\n");
            await pipe.WriteAsync(bytes, ct);
            await pipe.FlushAsync(ct);
            pipe.WaitForPipeDrain();
        }
        catch (Exception ex) { AurevixLog.Warn($"Conexión fallida: {ex.Message}"); }
    }

    /// <summary>Solo usuarios con sesión local interactiva; nunca desde la red.</summary>
    private static PipeSecurity PipeAcl()
    {
        var s = new PipeSecurity();
        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return s;
    }

    /// <summary>
    /// La carpeta del servicio (copias de seguridad incluidas) solo es accesible para SYSTEM y Administradores.
    /// Así un usuario no puede colocar una copia falsa para que el servicio la "restaure".
    /// </summary>
    private static void SecureDirectory(string dir)
    {
        var info = Directory.CreateDirectory(dir);
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(sec);
    }
}
