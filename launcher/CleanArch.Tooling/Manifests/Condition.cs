namespace CleanArch.Tooling.Manifests;

public sealed class Condition
{
    public const string True = "true";
    public const string False = "false";
    public const char ValueSeparator = '|';

    private readonly INode _root;

    private Condition(string text, INode root, IReadOnlySet<string> symbols)
    {
        Text = text;
        _root = root;
        Symbols = symbols;
    }

    public string Text { get; }

    public IReadOnlySet<string> Symbols { get; }

    public static Condition Parse(string text)
    {
        var parser = new Parser(Tokenize(text), text);
        INode root = parser.Disjunction();
        parser.ExpectEnd();

        return new Condition(text, root, parser.Symbols);
    }

    public bool IsTrue(Func<string, string?> resolve) => IsTruthy(_root.Evaluate(resolve));

    public override string ToString() => Text;

    public static bool IsTruthy(string? value) => string.Equals(value, True, StringComparison.OrdinalIgnoreCase);

    public static string Of(bool value) => value ? True : False;

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int index = 0;
        while (index < text.Length)
        {
            index = char.IsWhiteSpace(text[index]) ? index + 1 : Read(text, index, tokens);
        }

        return tokens;
    }

    private static int Read(string text, int index, List<Token> tokens)
    {
        char current = text[index];
        if (index + 1 < text.Length && text.Substring(index, 2) is "==" or "!=" or "<=" or ">=" or "&&" or "||")
        {
            tokens.Add(new Token(TokenKind.Operator, text.Substring(index, 2)));
            return index + 2;
        }

        if (current is '(' or ')' or '!' or '<' or '>')
        {
            tokens.Add(new Token(TokenKind.Operator, current.ToString()));
            return index + 1;
        }

        if (current == '"')
        {
            return ReadLiteral(text, index, tokens);
        }

        if (IsWordCharacter(current))
        {
            return ReadWord(text, index, tokens);
        }

        throw new FormatException($"The condition '{text}' holds '{current}', which this launcher does not read.");
    }

    private static int ReadLiteral(string text, int index, List<Token> tokens)
    {
        int end = text.IndexOf('"', index + 1);
        if (end < 0)
        {
            throw new FormatException($"The condition '{text}' opens a string it never closes.");
        }

        tokens.Add(new Token(TokenKind.Literal, text[(index + 1)..end]));
        return end + 1;
    }

    private static int ReadWord(string text, int index, List<Token> tokens)
    {
        int end = index;
        while (end < text.Length && IsWordCharacter(text[end]))
        {
            end++;
        }

        string word = text[index..end];
        tokens.Add(word is True or False || char.IsDigit(word[0])
            ? new Token(TokenKind.Literal, word)
            : new Token(TokenKind.Symbol, word));
        return end;
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private enum TokenKind
    {
        Operator,
        Literal,
        Symbol,
    }

    private sealed record Token(TokenKind Kind, string Text);

    private interface INode
    {
        string? Evaluate(Func<string, string?> resolve);
    }

    private sealed record LiteralNode(string Value) : INode
    {
        public string? Evaluate(Func<string, string?> resolve) => Value;
    }

    private sealed record SymbolNode(string Name) : INode
    {
        public string? Evaluate(Func<string, string?> resolve) => resolve(Name);
    }

    private sealed record NotNode(INode Operand) : INode
    {
        public string? Evaluate(Func<string, string?> resolve) => Of(!IsTruthy(Operand.Evaluate(resolve)));
    }

    private sealed record BinaryNode(string Operator, INode Left, INode Right) : INode
    {
        public string? Evaluate(Func<string, string?> resolve) => Operator switch
        {
            "&&" => Of(IsTruthy(Left.Evaluate(resolve)) && IsTruthy(Right.Evaluate(resolve))),
            "||" => Of(IsTruthy(Left.Evaluate(resolve)) || IsTruthy(Right.Evaluate(resolve))),
            "==" => Of(ShareAValue(Left.Evaluate(resolve), Right.Evaluate(resolve))),
            "!=" => Of(!ShareAValue(Left.Evaluate(resolve), Right.Evaluate(resolve))),
            _ => Of(Orders(Left.Evaluate(resolve), Right.Evaluate(resolve))),
        };

        private bool Orders(string? left, string? right) =>
            left is not null && right is not null && string.Compare(left, right, StringComparison.OrdinalIgnoreCase) switch
            {
                < 0 => Operator is "<" or "<=",
                0 => Operator is "<=" or ">=",
                > 0 => Operator is ">" or ">=",
            };

        private static bool ShareAValue(string? left, string? right) =>
            left is null || right is null
                ? left == right
                : left.Split(ValueSeparator).Intersect(right.Split(ValueSeparator), StringComparer.OrdinalIgnoreCase).Any();
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly string _text;
        private readonly HashSet<string> _symbols = new(StringComparer.Ordinal);
        private int _position;

        public Parser(List<Token> tokens, string text)
        {
            _tokens = tokens;
            _text = text;
        }

        public IReadOnlySet<string> Symbols => _symbols;

        public INode Disjunction()
        {
            INode left = Conjunction();
            while (Accept("||"))
            {
                left = new BinaryNode("||", left, Conjunction());
            }

            return left;
        }

        public void ExpectEnd()
        {
            if (_position < _tokens.Count)
            {
                throw new FormatException($"The condition '{_text}' goes on after its end, at '{_tokens[_position].Text}'.");
            }
        }

        private INode Conjunction()
        {
            INode left = Comparison();
            while (Accept("&&"))
            {
                left = new BinaryNode("&&", left, Comparison());
            }

            return left;
        }

        private INode Comparison()
        {
            INode left = Unary();
            while (Peek() is { Kind: TokenKind.Operator, Text: "==" or "!=" or "<" or "<=" or ">" or ">=" } token)
            {
                _position++;
                left = new BinaryNode(token.Text, left, Unary());
            }

            return left;
        }

        private INode Unary()
        {
            if (Accept("!"))
            {
                return new NotNode(Unary());
            }

            if (Accept("("))
            {
                INode inner = Disjunction();
                if (!Accept(")"))
                {
                    throw new FormatException($"The condition '{_text}' opens a parenthesis it never closes.");
                }

                return inner;
            }

            Token token = Peek() ?? throw new FormatException($"The condition '{_text}' ends where an operand belongs.");
            _position++;

            switch (token.Kind)
            {
                case TokenKind.Literal:
                    return new LiteralNode(token.Text);
                case TokenKind.Symbol:
                    _symbols.Add(token.Text);
                    return new SymbolNode(token.Text);
                default:
                    throw new FormatException($"The condition '{_text}' holds '{token.Text}' where an operand belongs.");
            }
        }

        private Token? Peek() => _position < _tokens.Count ? _tokens[_position] : null;

        private bool Accept(string text)
        {
            if (Peek() is { Kind: TokenKind.Operator } token && token.Text == text)
            {
                _position++;
                return true;
            }

            return false;
        }
    }
}
