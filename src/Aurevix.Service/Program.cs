using Aurevix.Service;

// AUREVIX Core Service: servicio de Windows (LocalSystem) con una API local MUY limitada.
// Ver Aurevix.Performance.CoreRequestHandler para las reglas de autorización.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "AurevixCoreService");
builder.Services.AddHostedService<PipeServer>();
builder.Build().Run();
