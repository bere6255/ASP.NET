namespace PaymentsApi.Helpers;

public static class EnvironmentHelper
{
    public static void LoadDotEnv()
    {
        var searchRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            searchRoots.Add(directory.FullName);
            directory = directory.Parent;
        }

        foreach (var root in searchRoots)
        {
            var filePath = Path.Combine(root, ".env");
            if (!File.Exists(filePath))
                continue;

            foreach (var line in File.ReadLines(filePath))
            {
                var content = line.Trim();
                if (content.Length == 0 || content.StartsWith('#'))
                    continue;
                var separator = content.IndexOf('=');
                if (separator <= 0)
                    continue;
                var name = content[..separator].Trim();
                var value = content[(separator + 1)..].Trim().Trim('"');
                if (Environment.GetEnvironmentVariable(name) is null)
                    Environment.SetEnvironmentVariable(name, value);
            }

            return;
        }
    }
}
