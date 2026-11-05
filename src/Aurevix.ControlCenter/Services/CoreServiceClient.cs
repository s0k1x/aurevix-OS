using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Aurevix.Core;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Services;

/// <summary>Cliente del AUREVIX Core Service (named pipe local).</summary>
public static class CoreServiceClient
{
    public static async Task<CoreResponse> SendAsync(CoreRequest req, TimeSpan? timeout = null)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
            await using var pipe = new NamedPipeClientStream(".", CoreServiceProtocol.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(2000, cts.Token);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(req, JsonDefaults.Options) + "\n");
            await pipe.WriteAsync(bytes, cts.Token);
            await pipe.FlushAsync(cts.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            var line = await reader.ReadLineAsync(cts.Token);
            return line is null ? CoreResponse.Fail("El servicio no respondió.")
                : JsonSerializer.Deserialize<CoreResponse>(line, JsonDefaults.Options) ?? CoreResponse.Fail("Respuesta vacía.");
        }
        catch (TimeoutException) { return CoreResponse.Fail("AUREVIX Core Service no está en ejecución."); }
        catch (OperationCanceledException) { return CoreResponse.Fail("AUREVIX Core Service no respondió a tiempo."); }
        catch (Exception ex) { return CoreResponse.Fail($"No se pudo contactar con AUREVIX Core Service: {ex.Message}"); }
    }

    public static async Task<bool> IsAvailableAsync() => (await SendAsync(new CoreRequest { Op = "ping" }, TimeSpan.FromSeconds(3))).Ok;
}
