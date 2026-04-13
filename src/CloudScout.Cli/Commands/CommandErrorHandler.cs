using System.CommandLine;

namespace CloudScout.Cli.Commands;

/// <summary>
/// Wraps command action delegates with consistent error handling so the CLI surfaces
/// expected failures as tidy messages rather than unhandled-exception stack dumps.
/// System.CommandLine v3 preview's invocation pipeline intercepts exceptions before they
/// reach Program.cs — catching inside the action is the only reliable hook for now.
/// </summary>
internal static class CommandErrorHandler
{
    public static Func<ParseResult, CancellationToken, Task<int>> Wrap(
        Func<ParseResult, CancellationToken, Task<int>> inner)
    {
        return async (parseResult, ct) =>
        {
            try
            {
                return await inner(parseResult, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Cancelled.");
                return 130; // conventional exit code for SIGINT
            }
            catch (InvalidOperationException ex)
            {
                // Expected user-facing errors (missing config, no cached account, etc.).
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 2;
            }
        };
    }
}
