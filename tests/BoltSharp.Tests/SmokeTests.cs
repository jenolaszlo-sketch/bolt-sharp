using System.Reflection;

using Xunit;

namespace BoltSharp.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void CoreLibrary_AssemblyLoads()
    {
        // Proves the protocol core builds and is referenceable from the test project.
        // No protocol behaviour exists yet (B0-B is scaffolding only).
        var assembly = Assembly.Load("BoltSharp");

        Assert.NotNull(assembly);
        Assert.Equal("BoltSharp", assembly.GetName().Name);
    }
}
