using Divinity.PlatformApi;

var builder = WebApplication.CreateBuilder(args);
var app = PlatformApiApp.Build(builder);
app.Run();

public partial class Program
{
}
