namespace PlusOneTrainer.Core;

/// <summary>A small, deterministic masked-pattern matcher used for runtime signatures.</summary>
public sealed class MaskedBytePattern
{
    private readonly byte?[] _bytes;

    public int Length => _bytes.Length;

    private MaskedBytePattern(byte?[] bytes)
    {
        if (bytes.Length == 0)
            throw new ArgumentException("A pattern cannot be empty.", nameof(bytes));
        _bytes = bytes;
    }

    public static MaskedBytePattern Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte?[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] is "?" or "??")
            {
                bytes[i] = null;
                continue;
            }
            if (tokens[i].Length != 2 || !byte.TryParse(tokens[i], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"Invalid byte-pattern token: {tokens[i]}");
            bytes[i] = value;
        }
        return new MaskedBytePattern(bytes);
    }

    public IReadOnlyList<int> FindAll(ReadOnlySpan<byte> source)
    {
        if (source.Length < _bytes.Length)
            return [];
        var matches = new List<int>();
        for (var start = 0; start <= source.Length - _bytes.Length; start++)
        {
            var match = true;
            for (var i = 0; i < _bytes.Length; i++)
            {
                if (_bytes[i].HasValue && _bytes[i]!.Value != source[start + i])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                matches.Add(start);
        }
        return matches;
    }
}

public sealed record AdvancedPauseSignature(
    string Id,
    MaskedBytePattern SearchPattern,
    int PatchOffset,
    byte[] OriginalBytes,
    byte[] EnabledBytes);
