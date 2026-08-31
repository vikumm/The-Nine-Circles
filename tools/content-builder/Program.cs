using Divinity.ContentBuilder;

var command = args.FirstOrDefault() ?? "build";
if (command is not ("build" or "validate"))
{
    PrintUsage();
    return 2;
}

var contentRoot = GetOption(args, "--content-root") ?? "content";
var outputRoot = GetOption(args, "--output-root") ?? "tools/content-builder/artifacts";
var options = new ContentBuilderOptions(
    Path.GetFullPath(contentRoot),
    Path.GetFullPath(outputRoot),
    WriteArtifacts: command == "build");

var result = await ContentBuilder.RunAsync(options);
if (!result.Success)
{
    Console.Error.WriteLine("Content validation failed:");
    foreach (var error in result.Errors)
    {
        Console.Error.WriteLine($"- {error}");
    }

    return 1;
}

Console.WriteLine($"Content hash: {result.ContentHash}");
if (result.ClientArtifactPath is not null)
{
    Console.WriteLine($"Unity visual artifact: {result.ClientArtifactPath}");
}

if (result.ServerArtifactPath is not null)
{
    Console.WriteLine($"Server authoritative artifact: {result.ServerArtifactPath}");
}

return 0;

static string? GetOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.Ordinal))
        {
            return args[index + 1];
        }
    }

    return null;
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  content-builder build [--content-root content] [--output-root tools/content-builder/artifacts]");
    Console.Error.WriteLine("  content-builder validate [--content-root content]");
}
