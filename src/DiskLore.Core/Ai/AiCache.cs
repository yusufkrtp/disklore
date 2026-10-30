using DiskLore.Core.Explanations;

namespace DiskLore.Core.Ai;

public sealed class AiCache
{
    private readonly string _filePath;

    public AiCache(string filePath)
    {
        _filePath = filePath;
    }

    // Lookup by normalized path.
    public Explanation? Get(string fullPath) => throw new NotImplementedException();

    public void Set(string fullPath, Explanation e) => throw new NotImplementedException();

    public void Clear() => throw new NotImplementedException();

    public int Count => throw new NotImplementedException();
}
