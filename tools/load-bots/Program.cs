using Divinity.LoadBots;

LoadBotOptions options;
try
{
    options = LoadBotOptions.Parse(args);
}
catch (LoadBotUsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var report = await new LoadBotRunner().RunAsync(options, CancellationToken.None);

LoadBotConsole.Write(report);

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    await LoadBotConsole.WriteJsonAsync(report, options.ReportPath, CancellationToken.None);
}

return report.Success ? 0 : 1;
