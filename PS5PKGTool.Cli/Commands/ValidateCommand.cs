using System.Text.Json;
using PS5PKGTool.Core.Services;

namespace PS5PKGTool.Cli;

internal static class ValidateCommand
{
    public static Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string? package = null;
        bool json = false;

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--json": json = true; break;
                case "-h" or "--help":
                    Console.WriteLine("Usage: ps5pkgtool validate <package.pkg> [--json]");
                    Console.WriteLine();
                    Console.WriteLine("Runs Sony debug package structural acceptance checks.");
                    return Task.FromResult(0);
                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"error: unknown option '{arg}'");
                        return Task.FromResult(1);
                    }
                    package ??= arg;
                    break;
            }
        }

        if (package is null)
        {
            Console.Error.WriteLine("error: validate requires a package path");
            return Task.FromResult(1);
        }

        if (!File.Exists(package))
        {
            Console.Error.WriteLine($"error: package not found: {package}");
            return Task.FromResult(1);
        }

        cancellationToken.ThrowIfCancellationRequested();

        SonyPackageAcceptanceReport report = SonyPackageAcceptanceValidator.Validate(package);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                isStructurallyReady = report.IsStructurallyReady,
                checks = report.Checks.Select(check => new
                {
                    name = check.Name,
                    state = check.State.ToString(),
                    message = check.Message,
                }),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            var rows = report.Checks
                .Select(check => new[] { Describe(check.State), check.Name, check.Message })
                .ToList();

            Format.Table(["STATE", "CHECK", "MESSAGE"], rows);
            Console.WriteLine();
            Console.WriteLine(report.IsStructurallyReady
                ? "Structurally ready."
                : "Not structurally ready.");
        }

        bool hasFailure = report.Checks.Any(check => check.State == SonyPackageCheckState.Fail);
        return Task.FromResult(hasFailure ? 1 : 0);
    }

    private static string Describe(SonyPackageCheckState state) => state switch
    {
        SonyPackageCheckState.Pass => "pass",
        SonyPackageCheckState.Warning => "warn",
        SonyPackageCheckState.Fail => "fail",
        _ => state.ToString().ToLowerInvariant(),
    };
}
