using System.Reflection;
using System.Text.RegularExpressions;

namespace DragWin;

public static partial class BuildIdentity
{
    private const string MetadataKey = "GitBuildDescription";

    public static string Current
    {
        get
        {
            Assembly assembly = typeof(BuildIdentity).Assembly;
            string productVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";
            return GetDisplayVersion(productVersion);
        }
    }

    public static string GetDisplayVersion(string productVersion)
    {
        string? gitDescription = typeof(BuildIdentity).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == MetadataKey)
            ?.Value;

        return Normalize(gitDescription, productVersion);
    }

    public static string Normalize(string? gitDescription, string productVersion)
    {
        string fallback = $"v{productVersion.Split('+', 2)[0]}";
        if (string.IsNullOrWhiteSpace(gitDescription))
        {
            return fallback;
        }

        string description = gitDescription.Trim();
        if (!description.EndsWith("-dirty", StringComparison.OrdinalIgnoreCase))
        {
            description = ExactTagSuffix().Replace(description, string.Empty);
        }

        return description.StartsWith('v')
            ? description
            : $"git-{description}";
    }

    [GeneratedRegex(@"-0-g[0-9a-f]+$", RegexOptions.IgnoreCase)]
    private static partial Regex ExactTagSuffix();
}
