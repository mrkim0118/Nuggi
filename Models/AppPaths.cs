namespace Nuggi.Models;

internal sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        // Release packages ship a portable Python in "python"; development uses the .venv.
        var bundledPython = Path.Combine(rootDirectory, "python", "python.exe");
        PythonExecutablePath = File.Exists(bundledPython)
            ? bundledPython
            : Path.Combine(rootDirectory, ".venv", "Scripts", "python.exe");
        WorkerScriptPath = Path.Combine(rootDirectory, "scripts", "nuggi_worker.py");
        PreviewCacheDirectory = Path.Combine(Path.GetTempPath(), "Nuggi", "preview-cache");
        MaskCacheDirectory = Path.Combine(PreviewCacheDirectory, "masks");
        BundledModelDirectory = Path.Combine(rootDirectory, ".u2net");
    }

    public string RootDirectory { get; }

    public string PythonExecutablePath { get; }

    public string WorkerScriptPath { get; }

    public string PreviewCacheDirectory { get; }

    public string MaskCacheDirectory { get; }

    public string BundledModelDirectory { get; }
}
