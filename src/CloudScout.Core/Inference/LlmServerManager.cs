using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace CloudScout.Core.Inference;

/// <summary>
/// Manages the lifecycle of a llama-server child process. On first call to
/// <see cref="EnsureRunningAsync"/>, checks if the configured <see cref="LlmOptions.ServerUrl"/>
/// is already reachable. If not and <see cref="LlmOptions.AutoLaunch"/> is true, spawns
/// llama-server as a child process and polls until the health endpoint responds.
///
/// The process is killed when this service is disposed (typically at CLI exit via the DI
/// container teardown).
/// </summary>
public sealed class LlmServerManager : IAsyncDisposable
{
    private readonly LlmOptions _options;
    private readonly ILogger<LlmServerManager> _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _serverProcess;
    private bool _checked;

    public LlmServerManager(IOptions<LlmOptions> options, ILogger<LlmServerManager> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the LLM server is reachable. Called once before the first Tier 3 classification.
    /// If the server is already running (user started it manually, or a previous scan started it),
    /// this returns immediately. If not, and auto-launch is configured, spawns the server.
    /// </summary>
    public async Task EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        if (_checked) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_checked) return;
            _checked = true;

            if (string.IsNullOrWhiteSpace(_options.ServerUrl)) return;

            if (await IsServerReachableAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("LLM server already running at {Url}", _options.ServerUrl);
                return;
            }

            if (!_options.AutoLaunch)
            {
                _logger.LogWarning("LLM server at {Url} is not reachable and AutoLaunch is disabled. Tier 3 will be skipped.", _options.ServerUrl);
                return;
            }

            await LaunchServerAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns true if the server is currently reachable (responds to health/models endpoint).</summary>
    public bool IsServerLive => _serverProcess is { HasExited: false } || _checked;

    private async Task<bool> IsServerReachableAsync(CancellationToken ct)
    {
        try
        {
            var baseUrl = _options.ServerUrl.TrimEnd('/');
            var response = await _http.GetAsync($"{baseUrl}/v1/models", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task LaunchServerAsync(CancellationToken ct)
    {
        var modelPath = ResolveModelPath();
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            _logger.LogWarning("Cannot auto-launch llama-server: Llm:ModelPath is not configured.");
            return;
        }

        if (!File.Exists(modelPath))
        {
            _logger.LogWarning("Cannot auto-launch llama-server: model file not found at {Path}", modelPath);
            return;
        }

        // Parse the port from the ServerUrl so we pass it to llama-server's --port flag.
        if (!Uri.TryCreate(_options.ServerUrl, UriKind.Absolute, out var uri))
        {
            _logger.LogWarning("Cannot auto-launch llama-server: ServerUrl '{Url}' is not a valid URI.", _options.ServerUrl);
            return;
        }

        var port = uri.Port > 0 ? uri.Port : 8080;

        // Resolve the exe path to absolute — Process.Start with relative paths is unreliable
        // on Windows (doesn't check cwd for non-PATH executables with forward slashes).
        var resolvedExe = ResolveExePath(_options.ServerExePath);
        if (resolvedExe is null)
        {
            _logger.LogWarning("Cannot auto-launch llama-server: executable not found at '{Path}'. " +
                "Place llama-server.exe in the tools/ directory or set Llm:ServerExePath to the full path.",
                _options.ServerExePath);
            return;
        }

        var args = $"-m \"{modelPath}\" -c {_options.ContextSize} --port {port}";

        // Optional multimodal projector for vision input. If configured but missing, log a
        // warning and proceed without it — vision becomes unavailable but text classification
        // still works.
        var mmprojPath = ResolveMmprojPath();
        if (!string.IsNullOrWhiteSpace(_options.MmprojPath))
        {
            if (mmprojPath is not null)
            {
                args += $" --mmproj \"{mmprojPath}\"";
            }
            else
            {
                _logger.LogWarning("Llm:MmprojPath is set ('{Path}') but the file was not found. " +
                    "Vision input will be ignored.", _options.MmprojPath);
            }
        }

        _logger.LogInformation("Auto-launching: {Exe} {Args}", resolvedExe, args);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = resolvedExe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            _serverProcess = Process.Start(psi);
            if (_serverProcess is null)
            {
                _logger.LogWarning("Failed to start llama-server process.");
                return;
            }

            // Drain stdout/stderr to prevent buffer deadlocks. We don't need the output.
            _serverProcess.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) _logger.LogDebug("[llama-server] {Line}", e.Data);
            };
            _serverProcess.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) _logger.LogDebug("[llama-server] {Line}", e.Data);
            };
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            _logger.LogInformation("llama-server started (PID {Pid}). Waiting for readiness...", _serverProcess.Id);

            await WaitForReadyAsync(ct).ConfigureAwait(false);

            _logger.LogInformation("llama-server is ready at {Url}", _options.ServerUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to auto-launch llama-server. Tier 3 will be skipped. " +
                "Ensure '{Exe}' is on your PATH or set Llm:ServerExePath to the full path.",
                _options.ServerExePath);
        }
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        // Poll the health endpoint until the server responds or we time out.
        // Model loading for a 5GB Q8 can take 10-30 seconds on HDD/SSD.
        const int maxWaitSeconds = 120;
        const int pollIntervalMs = 1000;

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < maxWaitSeconds)
        {
            ct.ThrowIfCancellationRequested();

            if (_serverProcess is { HasExited: true })
            {
                _logger.LogWarning("llama-server exited unexpectedly with code {Code}", _serverProcess.ExitCode);
                return;
            }

            if (await IsServerReachableAsync(ct).ConfigureAwait(false))
                return;

            await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
        }

        _logger.LogWarning("llama-server did not become ready within {MaxWait}s. Tier 3 may fail.", maxWaitSeconds);
    }

    /// <summary>
    /// Resolves an exe path: if absolute and exists, use it. If relative, try cwd then exe dir.
    /// Returns null if the file can't be found anywhere.
    /// </summary>
    private static string? ResolveExePath(string exePath)
    {
        if (Path.IsPathRooted(exePath))
            return File.Exists(exePath) ? exePath : null;

        var fromCwd = Path.GetFullPath(exePath);
        if (File.Exists(fromCwd)) return fromCwd;

        var fromBase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, exePath));
        if (File.Exists(fromBase)) return fromBase;

        return null;
    }

    /// <summary>
    /// Resolves the multimodal projector path. Returns null when MmprojPath is empty (vision
    /// not requested) or when the file isn't found at any candidate location. Caller distinguishes
    /// "not requested" from "configured but missing" by checking <see cref="LlmOptions.MmprojPath"/>.
    /// </summary>
    private string? ResolveMmprojPath()
    {
        var path = _options.MmprojPath;
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (Path.IsPathRooted(path))
            return File.Exists(path) ? path : null;

        var fromCwd = Path.GetFullPath(path);
        if (File.Exists(fromCwd)) return fromCwd;

        var fromBase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        return File.Exists(fromBase) ? fromBase : null;
    }

    private string ResolveModelPath()
    {
        var path = _options.ModelPath;
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (Path.IsPathRooted(path)) return path;

        // Try relative to cwd first (where the user runs cloudscout from)
        var fromCwd = Path.GetFullPath(path);
        if (File.Exists(fromCwd)) return fromCwd;

        // Fall back to relative to exe
        var fromBase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        return File.Exists(fromBase) ? fromBase : fromCwd;
    }

    public async ValueTask DisposeAsync()
    {
        if (_serverProcess is not null && !_serverProcess.HasExited)
        {
            _logger.LogInformation("Shutting down llama-server (PID {Pid})...", _serverProcess.Id);
            try
            {
                _serverProcess.Kill(entireProcessTree: true);
                await _serverProcess.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error killing llama-server process");
            }
            _serverProcess.Dispose();
        }
        _http.Dispose();
        _gate.Dispose();
    }
}
