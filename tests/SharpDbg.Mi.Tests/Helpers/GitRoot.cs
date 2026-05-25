namespace SharpDbg.Mi.Tests.Helpers;

public static class GitRoot
{
    private static string? _root;

    public static string Get()
    {
        if (_root is not null) return _root;
        var dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        _root = string.IsNullOrEmpty(dir)
            ? throw new InvalidOperationException("Could not find git root")
            : dir;
        return _root;
    }

    public static string Join(params string[] parts) =>
        Path.Combine([Get(), .. parts]);
}
