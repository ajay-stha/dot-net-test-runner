using System.IO;
using System.Xml.Linq;
using DotNetTestRunner.Application.Abstractions;

namespace DotNetTestRunner.Infrastructure.Services;

/// <summary>
/// Reads build configurations from a project file and locates the target to load at startup.
/// </summary>
public sealed class TestTargetService : ITestTargetService
{
    private const string CONFIGURATIONS_ELEMENT_NAME = "Configurations";

    /// <summary>
    /// Target file names looked for when no usable target has been persisted.
    /// </summary>
    private static readonly string[] _DefaultTargetFileNames =
    [
        "CSM.UI.Tests.sln",
        "CSM.UI.Tests.csproj"
    ];

    /// <inheritdoc />
    public IReadOnlyList<string> ReadConfigurations(string targetPath)
    {
        if (!targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        try
        {
            var document = XDocument.Load(targetPath);

            return document.Descendants()
                .Where(static element => element.Name.LocalName == CONFIGURATIONS_ELEMENT_NAME)
                .SelectMany(static element => element.Value.Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            // An unreadable or malformed project file must not block target selection;
            // the caller falls back to its default configurations.
            return [];
        }
    }

    /// <inheritdoc />
    public string? ResolveInitialTargetPath(string? persistedTargetPath)
    {
        if (!string.IsNullOrWhiteSpace(persistedTargetPath) && File.Exists(persistedTargetPath))
        {
            return persistedTargetPath;
        }

        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            foreach (var fileName in _DefaultTargetFileNames)
            {
                var candidatePath = Path.Combine(currentDirectory.FullName, fileName);

                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }
}
