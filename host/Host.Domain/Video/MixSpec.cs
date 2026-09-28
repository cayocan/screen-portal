namespace ScreenPortal.Host.Domain.Video;

public readonly record struct MixGroup(int Fps, int Count);

/// <summary>"60x1,15x3" = 1 stream a 60 fps + 3 streams a 15 fps (cenário da FocusPolicy, brief §8.8).</summary>
public static class MixSpec
{
    public static IReadOnlyList<MixGroup> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("mix vazio");
        var groups = new List<MixGroup>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('x');
            if (pieces.Length != 2 || !int.TryParse(pieces[0], out int fps) || !int.TryParse(pieces[1], out int count)
                || fps <= 0 || fps > 120 || count <= 0 || count > 16)
                throw new FormatException($"grupo inválido: '{part}' (use <fps>x<quantidade>)");
            groups.Add(new MixGroup(fps, count));
        }
        return groups;
    }

    public static int TotalStreams(IReadOnlyList<MixGroup> groups) => groups.Sum(g => g.Count);
}
