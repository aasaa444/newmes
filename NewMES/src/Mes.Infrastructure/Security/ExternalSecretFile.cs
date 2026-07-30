namespace Mes.Infrastructure.Security;

/// <summary>从容器挂载目录读取外部秘密；键禁止包含路径分隔符，避免目录穿越。</summary>
public static class ExternalSecretFile
{
    public static string? ReadOptional(string directory, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException("Secret keys cannot contain path separators.", nameof(key));
        }

        var path = Path.Combine(directory, key);
        if (!File.Exists(path))
        {
            return null;
        }

        var value = File.ReadAllText(path).TrimEnd('\r', '\n');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
