using DiskLore.Core.Explanations;

namespace DiskLore.Core.Ai;

public static class AiResponseParser
{
    // Parses the model output into path -> explanation, applying the safety clamp.
    public static IReadOnlyDictionary<string, Explanation> Parse(string output)
        => throw new NotImplementedException();
}
