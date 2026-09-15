using System.Reflection;
using System.Runtime.InteropServices;

namespace PS5PKGTool.Cli;

internal static class VersionCommand
{
    public static int Run()
    {
        string version = typeof(VersionCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(VersionCommand).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        Console.WriteLine($"ps5pkgtool {version}");
        Console.WriteLine($"runtime    {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"platform   {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        return 0;
    }
}
