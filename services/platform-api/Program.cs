using Divinity.PlatformApi;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/healthz", () => new
{
    service = PlatformApiInfo.ComponentName,
    status = PlatformApiInfo.Status
});

app.Run();

public partial class Program
{
}
