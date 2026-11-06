namespace CleanArch.Cli.Generators.Shared;

internal enum FieldTokenKind
{
    Name,
    Number,
    Symbol,
    Quoted,
    Unknown,
    End,
}

internal sealed record FieldToken(FieldTokenKind Kind, string Text, int Position);

internal sealed class FieldTokens
{
    private readonly string _text;

    private readonly List<FieldToken> _tokens = [];

    private int _position;

    private FieldTokens(string text) => _text = text;

    internal static List<FieldToken> Of(string text) => new FieldTokens(text).Read();

    private List<FieldToken> Read()
    {
        while (_position < _text.Length)
        {
            if (char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
            else
            {
                _tokens.Add(Next(_text[_position], _position));
            }
        }

        _tokens.Add(new FieldToken(FieldTokenKind.End, string.Empty, _text.Length));

        return _tokens;
    }

    private FieldToken Next(char current, int start)
    {
        if (char.IsAsciiLetter(current) || current == '_')
        {
            return Run(FieldTokenKind.Name, start, start, character => char.IsAsciiLetterOrDigit(character) || character == '_');
        }

        if (char.IsAsciiDigit(current) || (current == '-' && start + 1 < _text.Length && char.IsAsciiDigit(_text[start + 1])))
        {
            return Run(FieldTokenKind.Number, start, start + 1, character => char.IsAsciiDigit(character) || character == '.');
        }

        return current is '>' or '<' ? Comparison(start) : Punctuation(current, start);
    }

    private FieldToken Comparison(int start)
    {
        _position += start + 1 < _text.Length && _text[start + 1] == '=' ? 2 : 1;

        return new FieldToken(FieldTokenKind.Symbol, _text[start.._position], start);
    }

    private FieldToken Punctuation(char current, int start)
    {
        if (current == '\'' && Closing(start) is var closing and > 0)
        {
            _position = closing + 1;

            return new FieldToken(FieldTokenKind.Quoted, _text[(start + 1)..closing].Replace("''", "'", StringComparison.Ordinal), start);
        }

        _position++;

        return new FieldToken(current is ':' or ',' or '(' or ')' or '?' or '=' ? FieldTokenKind.Symbol : FieldTokenKind.Unknown, current.ToString(), start);
    }

    private FieldToken Run(FieldTokenKind kind, int start, int from, Func<char, bool> continues)
    {
        _position = from;

        while (_position < _text.Length && continues(_text[_position]))
        {
            _position++;
        }

        return new FieldToken(kind, _text[start.._position], start);
    }

    private int Closing(int opening)
    {
        int index = opening + 1;

        while (index < _text.Length)
        {
            if (_text[index] != '\'')
            {
                index++;
            }
            else if (index + 1 < _text.Length && _text[index + 1] == '\'')
            {
                index += 2;
            }
            else
            {
                return index;
            }
        }

        return -1;
    }
}
