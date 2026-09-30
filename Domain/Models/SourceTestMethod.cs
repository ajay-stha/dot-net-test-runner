namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// A test method found by scanning the target's C# source files. Used to recover class names
/// when <c>dotnet test</c> lists bare method names instead of fully qualified ones.
/// </summary>
/// <param name="ClassName">Fully qualified name of the class declaring the test.</param>
/// <param name="MethodName">Name of the test method.</param>
/// <param name="FullyQualifiedName">Class and method name joined with a period.</param>
public sealed record SourceTestMethod(string ClassName, string MethodName, string FullyQualifiedName);
