using Divinity.GameGateway;

var builder = WebApplication.CreateBuilder(args);
var app = GatewayApp.Build(builder);
app.Run();

public partial class Program
{
}
