using System.Diagnostics;
using System.Text;
using Nuggi.Models;

namespace Nuggi.Services;

internal sealed class NuggiPythonBridge
{
    private readonly AppPaths _paths;

    public NuggiPythonBridge(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task EnsureRuntimeReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.PythonExecutablePath))
        {
            throw new FileNotFoundException("Python virtual environment was not found.", _paths.PythonExecutablePath);
        }

        if (!File.Exists(_paths.WorkerScriptPath))
        {
            throw new FileNotFoundException("Python worker script was not found.", _paths.WorkerScriptPath);
        }

        await RunPythonAsync(["-c", "import rembg, PIL, onnxruntime"], cancellationToken);
    }

    public Task GenerateMaskAsync(
        string inputPath,
        string modelName,
        string maskOutputPath,
        CancellationToken cancellationToken = default)
    {
        return RunPythonAsync(
            [
                _paths.WorkerScriptPath,
                "mask",
                "--input",
                inputPath,
                "--output",
                maskOutputPath,
                "--model",
                modelName,
            ],
            cancellationToken);
    }

    // shortcut: each click starts a new worker and re-encodes the image (~3-5 s); keep one worker per open editor if clicks feel slow.
    public Task SelectObjectAsync(string inputPath, Point imagePoint, string maskOutputPath, CancellationToken cancellationToken = default)
    {
        return RunPythonAsync(
            [
                _paths.WorkerScriptPath,
                "select",
                "--input",
                inputPath,
                "--output",
                maskOutputPath,
                "--x",
                imagePoint.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--y",
                imagePoint.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ],
            cancellationToken);
    }

    public Task ConvertToPngAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        return RunPythonAsync([_paths.WorkerScriptPath, "convert", "--input", inputPath, "--output", outputPath], cancellationToken);
    }

    // Runs every job in one Python process so the model loads once. Each finished job reports "DONE	<i>" or "FAIL	<i>	<message>".
    public Task RunBatchAsync(
        string jobsFilePath,
        string modelName,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        return RunPythonAsync(
            [_paths.WorkerScriptPath, "batch", "--jobs", jobsFilePath, "--model", modelName],
            cancellationToken,
            onOutputLine);
    }

    private async Task RunPythonAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? onOutputLine = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _paths.PythonExecutablePath,
            WorkingDirectory = _paths.RootDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        process.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        // Ignore packages the user installed in their own Python profile; use only the shipped ones.
        process.StartInfo.Environment["PYTHONNOUSERSITE"] = "1";

        if (Directory.Exists(_paths.BundledModelDirectory))
        {
            process.StartInfo.Environment["U2NET_HOME"] = _paths.BundledModelDirectory;
        }

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                stdout.AppendLine(eventArgs.Data);
                onOutputLine?.Invoke(eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                stderr.AppendLine(eventArgs.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Python worker process could not be started.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            var errorText = stderr.Length > 0 ? stderr.ToString().Trim() : stdout.ToString().Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText)
                ? $"Python worker exited with code {process.ExitCode}."
                : errorText);
        }
    }
}
