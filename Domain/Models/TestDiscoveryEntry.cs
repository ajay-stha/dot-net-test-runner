namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// A single test discovered for a target, resolved into the class and method parts used to
/// build the test tree.
/// </summary>
/// <param name="ClassName">Fully qualified name of the class declaring the test.</param>
/// <param name="MethodName">Name of the test method.</param>
/// <param name="FullyQualifiedName">Name used to filter the test when running it.</param>
public sealed record TestDiscoveryEntry(string ClassName, string MethodName, string FullyQualifiedName);
