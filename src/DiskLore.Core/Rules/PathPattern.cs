namespace DiskLore.Core.Rules;

public sealed class PathPattern
{
    public PathPattern(string pattern)
    {
        Pattern = pattern;
    }

    public string Pattern { get; }

    public bool IsMatch(string fullPath) => throw new NotImplementedException();
}
