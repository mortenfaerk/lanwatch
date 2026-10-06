namespace LanWatch.Shared.Parsing;

/// <summary>Forward-only cursor over a log line.</summary>
internal ref struct LineReader(ReadOnlySpan<char> line)
{
    private readonly ReadOnlySpan<char> _line = line;
    private int _pos;

    public readonly ReadOnlySpan<char> Rest => _pos < _line.Length ? _line[_pos..] : [];

    public void SkipSpaces()
    {
        while (_pos < _line.Length && _line[_pos] == ' ') _pos++;
    }

    /// <summary>Skips exactly one space if present, so empty tokens (double spaces) stay meaningful.</summary>
    public void SkipOneSpace()
    {
        if (_pos < _line.Length && _line[_pos] == ' ') _pos++;
    }

    public ReadOnlySpan<char> ReadToken()
    {
        var start = _pos;
        while (_pos < _line.Length && _line[_pos] != ' ') _pos++;
        return _line[start.._pos];
    }

    public bool TryExpect(char c)
    {
        if (_pos < _line.Length && _line[_pos] == c)
        {
            _pos++;
            return true;
        }
        return false;
    }

    public bool TryReadBracketed(out ReadOnlySpan<char> value)
    {
        value = default;
        if (!TryExpect('[')) return false;
        var end = _line[_pos..].IndexOf(']');
        if (end < 0) return false;
        value = _line.Slice(_pos, end);
        _pos += end + 1;
        return true;
    }

    /// <summary>Reads a double-quoted value. nginx escapes quotes inside values as <c>\x22</c>, so no unescaping is needed.</summary>
    public bool TryReadQuoted(out ReadOnlySpan<char> value)
    {
        value = default;
        if (!TryExpect('"')) return false;
        var end = _line[_pos..].IndexOf('"');
        if (end < 0) return false;
        value = _line.Slice(_pos, end);
        _pos += end + 1;
        return true;
    }
}
