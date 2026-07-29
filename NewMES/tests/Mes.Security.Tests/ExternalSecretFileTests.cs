using Mes.Infrastructure.Security;

namespace Mes.Security.Tests;

public sealed class ExternalSecretFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"newmes-secret-tests-{Guid.NewGuid():N}");

    public ExternalSecretFileTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void SecretValueIsReadWithoutTrailingLineEnding()
    {
        File.WriteAllText(
            Path.Combine(_directory, "ConnectionStrings__MesDatabase"),
            "Server=sqlserver;Database=NewMes\r\n");

        var value = ExternalSecretFile.ReadOptional(
            _directory,
            "ConnectionStrings__MesDatabase");

        Assert.Equal("Server=sqlserver;Database=NewMes", value);
    }

    [Fact]
    public void SecretKeyContainingPathSeparatorIsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ExternalSecretFile.ReadOptional(_directory, "nested/secret"));

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void MissingSecretReturnsNull()
    {
        var value = ExternalSecretFile.ReadOptional(_directory, "missing-secret");

        Assert.Null(value);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
