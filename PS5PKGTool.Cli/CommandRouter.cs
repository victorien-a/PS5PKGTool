namespace PS5PKGTool.Cli;

/// <summary>
/// Entry point dispatch. Each verb is implemented in its own file under Commands/.
/// </summary>
internal static class CommandRouter
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        if (args.Length == 0 || IsHelpFlag(args[0]))
        {
            HelpText.WriteUsage();
            return args.Length == 0 ? 1 : 0;
        }

        string verb = args[0].ToLowerInvariant();
        string[] rest = args[1..];

        try
        {
            return verb switch
            {
                "scan" => await ScanCommand.RunAsync(rest, cancellation.Token),
                "info" => await InfoCommand.RunAsync(rest, cancellation.Token),
                "files" => await FilesCommand.RunAsync(rest, cancellation.Token),
                "extract" => await ExtractCommand.RunAsync(rest, cancellation.Token),
                "convert" => await ConvertCommand.RunAsync(rest, cancellation.Token),
                "split" => await SplitCommand.RunAsync(rest, cancellation.Token),
                "merge" => await MergeCommand.RunAsync(rest, cancellation.Token),
                "validate" => await ValidateCommand.RunAsync(rest, cancellation.Token),
                "build" => await BuildCommand.RunAsync(rest, cancellation.Token),
                "version" => VersionCommand.Run(),
                _ => UnknownVerb(verb),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static bool IsHelpFlag(string value) =>
        value is "-h" or "--help" or "help" or "-?";

    private static int UnknownVerb(string verb)
    {
        Console.Error.WriteLine($"error: unknown command '{verb}'");
        Console.Error.WriteLine("Run 'ps5pkgtool --help' to see available commands.");
        return 1;
    }
}
