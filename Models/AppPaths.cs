namespace Nuggi.Models;

internal sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        PythonExecutablePath = Path.Combine(rootDirectory, ".venv", "Scripts", "python.exe");
        RequirementsFilePath = Path.Combine(rootDirectory, "requirements.txt");
        WorkerScriptPath = Path.Combine(rootDirectory, "scripts", "nuggi_worker.py");
        PreviewCacheDirectory = Path.Combine(Path.GetTempPath(), "Nuggi", "preview-cache");
        MaskCacheDirectory = Path.Combine(PreviewCacheDirectory, "masks");
        MaskEditCacheDirectory = Path.Combine(PreviewCacheDirectory, "mask-edits");
        BundledModelDirectory = Path.Combine(rootDirectory, ".u2net");
    }

    public string RootDirectory { get; }

    public string PythonExecutablePath { get; }

    public string RequirementsFilePath { get; }

    public string WorkerScriptPath { get; }

    public string PreviewCacheDirectory { get; }

    public string MaskCacheDirectory { get; }

    public string MaskEditCacheDirectory { get; }

    public string BundledModelDirectory { get; }
}
