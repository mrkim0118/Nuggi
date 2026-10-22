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

    public Task RemoveBackgroundAsync(
        string inputPath,
        string outputPath,
        string modelName,
        bool trim,
        string? maskPath = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            _paths.WorkerScriptPath,
            "remove",
            "--input",
            inputPath,
            "--output",
            outputPath,
            "--model",
            modelName,
        };

        if (!string.IsNullOrWhiteSpace(maskPath))
        {
            arguments.Add("--mask");
            arguments.Add(maskPath);
        }

        if (trim)
        {
            arguments.Add("--trim");
        }

        return RunPythonAsync(arguments, cancellationToken);
    }

    private async Task RunPythonAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
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
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var errorText = stderr.Length > 0 ? stderr.ToString().Trim() : stdout.ToString().Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText)
                ? $"Python worker exited with code {process.ExitCode}."
                : errorText);
        }
    }
}
