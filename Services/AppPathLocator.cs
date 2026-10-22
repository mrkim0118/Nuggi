using Nuggi.Models;

namespace Nuggi.Services;

internal static class AppPathLocator
{
    public static AppPaths Locate()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            var requirements = Path.Combine(currentDirectory.FullName, "requirements.txt");
            var workerScript = Path.Combine(currentDirectory.FullName, "scripts", "nuggi_worker.py");

            if (File.Exists(requirements) && File.Exists(workerScript))
            {
                return new AppPaths(currentDirectory.FullName);
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException("Nuggi project root could not be located.");
    }
}
