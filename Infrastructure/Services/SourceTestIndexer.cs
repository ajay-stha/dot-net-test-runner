using System.IO;
using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Domain.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetTestRunner.Infrastructure.Services;

/// <summary>
/// Finds test methods by parsing a target's C# source files with Roslyn. Some test adapters
/// list bare method names, which cannot be grouped into classes; scanning source recovers the
/// declaring class for those tests.
/// </summary>
public sealed class SourceTestIndexer : ISourceTestIndexer
{
    /// <summary>
    /// Attribute name suffixes that mark a method as a test, across the common frameworks.
    /// </summary>
    private static readonly string[] _TestAttributeSuffixes =
    [
        "TestMethod",
        "DataTestMethod",
        "Fact",
        "Theory",
        "Test",
        "TestCase",
        "TestCaseSource"
    ];

    /// <summary>
    /// Path fragments identifying build output, which must not be scanned.
    /// </summary>
    private static readonly string[] _ExcludedPathFragments =
    [
        "\\obj\\",
        "\\bin\\"
    ];

    /// <inheritdoc />
    public IReadOnlyList<SourceTestMethod> BuildIndex(string targetPath)
    {
        var discoveredMethods = new List<SourceTestMethod>();

        foreach (var projectFile in ResolveTargetProjectFiles(targetPath))
        {
            var projectDirectory = Path.GetDirectoryName(projectFile);

            if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                continue;
            }

            foreach (var sourceFile in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                if (IsExcluded(sourceFile))
                {
                    continue;
                }

                discoveredMethods.AddRange(ExtractTestsFromSourceFile(sourceFile));
            }
        }

        return discoveredMethods;
    }

    /// <summary>
    /// Lists the project files belonging to a target, expanding a solution into its projects.
    /// </summary>
    /// <param name="targetPath">Path to the <c>.sln</c> or <c>.csproj</c>.</param>
    /// <returns>The distinct project files that exist on disk.</returns>
    private static List<string> ResolveTargetProjectFiles(string targetPath)
    {
        if (targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return [targetPath];
        }

        if (!targetPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || !File.Exists(targetPath))
        {
            return [];
        }

        var solutionDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var projectFiles = new List<string>();

        foreach (var line in File.ReadLines(targetPath))
        {
            var relativePath = ReadProjectReference(line);

            if (relativePath is null)
            {
                continue;
            }

            var absolutePath = Path.GetFullPath(Path.Combine(solutionDirectory, relativePath));

            if (File.Exists(absolutePath))
            {
                projectFiles.Add(absolutePath);
            }
        }

        return projectFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Reads the project path from a solution file <c>Project(...)</c> line.
    /// </summary>
    /// <param name="line">Line from the solution file.</param>
    /// <returns>The relative project path, or <see langword="null"/> when the line has none.</returns>
    private static string? ReadProjectReference(string line)
    {
        if (!line.Contains(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = line.Split(',');

        if (parts.Length < 2)
        {
            return null;
        }

        var relativePath = parts[1].Trim().Trim('"').Replace('\\', Path.DirectorySeparatorChar);

        return relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? relativePath
            : null;
    }

    private static bool IsExcluded(string sourceFile)
    {
        foreach (var fragment in _ExcludedPathFragments)
        {
            if (sourceFile.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts every test method declared in a single source file.
    /// </summary>
    /// <param name="sourceFile">Path to the C# file to parse.</param>
    /// <returns>The test methods declared in the file.</returns>
    private static List<SourceTestMethod> ExtractTestsFromSourceFile(string sourceFile)
    {
        var methods = new List<SourceTestMethod>();
        var sourceText = File.ReadAllText(sourceFile);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);
        var root = syntaxTree.GetRoot();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!HasTestAttribute(method))
            {
                continue;
            }

            var declaringType = GetDeclaringTypePath(method);

            if (declaringType is null)
            {
                continue;
            }

            var namespacePath = GetNamespacePath(method);
            var fullyQualifiedClass = string.IsNullOrWhiteSpace(namespacePath)
                ? declaringType
                : string.Concat(namespacePath, ".", declaringType);

            var methodName = method.Identifier.Text;

            methods.Add(new SourceTestMethod(
                fullyQualifiedClass,
                methodName,
                string.Concat(fullyQualifiedClass, ".", methodName)));
        }

        return methods;
    }

    private static bool HasTestAttribute(MethodDeclarationSyntax method)
    {
        foreach (var attribute in method.AttributeLists.SelectMany(static list => list.Attributes))
        {
            var name = attribute.Name.ToString();

            foreach (var suffix in _TestAttributeSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the dotted type name for a method, including any enclosing nested types.
    /// </summary>
    /// <param name="method">Method whose declaring type is needed.</param>
    /// <returns>The type path, or <see langword="null"/> when the method is not in a type.</returns>
    private static string? GetDeclaringTypePath(MethodDeclarationSyntax method)
    {
        var typeChain = method.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Reverse()
            .Select(static type => type.Identifier.Text)
            .ToList();

        return typeChain.Count > 0 ? string.Join('.', typeChain) : null;
    }

    private static string GetNamespacePath(SyntaxNode node)
    {
        var namespaces = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(static declaration => declaration.Name.ToString());

        return string.Join('.', namespaces);
    }
}
