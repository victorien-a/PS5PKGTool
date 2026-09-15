namespace PS5PKGTool.Cli;

internal static class HelpText
{
    public static void WriteUsage()
    {
        Console.WriteLine("""
            PS5 PKG Tool - command line interface

            Usage:
              ps5pkgtool <command> [options]

            Commands:
              scan <path>...        Scan folders for PS5 dumps, packages and images
              info <path>           Show detailed metadata for a single item
              files <path>          List the files inside a dump, package or image
              extract <pkg> <dir>   Extract a Sony .pkg into a directory
              convert <pkg> <out>   Convert a Sony .pkg into another image format
              split <pkg> <dir>     Split a package into verifiable pieces
              merge <manifest> <out> Rebuild a package from split pieces
              validate <pkg>        Run acceptance checks against a package
              build <dir> <out.pkg> Build a debug package from a dump
              version               Show version and runtime information

            Common options:
              --json                Emit machine readable JSON instead of a table
              -h, --help            Show help for a command

            Examples:
              ps5pkgtool scan ~/PS5 --recursive
              ps5pkgtool info ~/PS5/CUSA00000
              ps5pkgtool files ~/PS5/game.pkg
              ps5pkgtool extract ~/PS5/game.pkg ~/out
            """);
    }
}
