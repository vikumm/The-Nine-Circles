using System.Text.Json;
using Divinity.ContentSchema;

namespace Divinity.ContentBuilder;

public static class ContentBuilder
{
    public static async Task<ContentBuilderResult> RunAsync(ContentBuilderOptions options, CancellationToken cancellationToken = default)
    {
        var loadResult = ContentSourceLoader.Load(options.ContentRoot);
        if (!loadResult.Success || loadResult.Catalog is null)
        {
            return ContentBuilderResult.Failed(loadResult.Errors);
        }

        if (!options.WriteArtifacts)
        {
            return ContentBuilderResult.Validated(loadResult.Catalog.ContentHash);
        }

        var clientArtifact = ContentArtifactFactory.CreateClientArtifact(loadResult.Catalog);
        var serverArtifact = ContentArtifactFactory.CreateServerArtifact(loadResult.Catalog);

        var clientPath = Path.Combine(options.OutputRoot, "unity", $"{loadResult.Catalog.Map.MapId}.visual.json");
        var serverPath = Path.Combine(options.OutputRoot, "server", $"{loadResult.Catalog.Map.MapId}.authoritative.json");

        Directory.CreateDirectory(Path.GetDirectoryName(clientPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(serverPath)!);

        await WriteJsonAsync(clientPath, clientArtifact, cancellationToken);
        await WriteJsonAsync(serverPath, serverArtifact, cancellationToken);

        return ContentBuilderResult.Built(loadResult.Catalog.ContentHash, clientPath, serverPath);
    }

    private static async Task WriteJsonAsync<T>(string path, T artifact, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(artifact, ContentJson.Options);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken);
    }
}

public sealed record ContentBuilderOptions(string ContentRoot, string OutputRoot, bool WriteArtifacts);

public sealed record ContentBuilderResult(
    bool Success,
    string ContentHash,
    IReadOnlyList<string> Errors,
    string? ClientArtifactPath,
    string? ServerArtifactPath)
{
    public static ContentBuilderResult Failed(IReadOnlyList<string> errors) =>
        new(false, string.Empty, errors, null, null);

    public static ContentBuilderResult Validated(string contentHash) =>
        new(true, contentHash, [], null, null);

    public static ContentBuilderResult Built(string contentHash, string clientArtifactPath, string serverArtifactPath) =>
        new(true, contentHash, [], clientArtifactPath, serverArtifactPath);
}
