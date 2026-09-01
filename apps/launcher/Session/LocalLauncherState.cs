namespace Divinity.Launcher.Session;

public sealed record LocalLauncherState(string Authority, string ClientId, DateTimeOffset AuthenticatedAtUtc);
