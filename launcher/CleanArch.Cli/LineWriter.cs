using System.Text;

namespace CleanArch.Cli;

internal sealed class LineWriter(Action<string>? relay, StringBuilder? captured) : TextWriter
{
    private readonly StringBuilder _line = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (value == '\n')
        {
            Complete();
        }
        else if (value != '\r')
        {
            _line.Append(value);
        }
    }

    public override void Flush()
    {
        if (_line.Length > 0)
        {
            Complete();
        }
    }

    private void Complete()
    {
        string text = _line.ToString();
        _line.Clear();
        captured?.AppendLine(text);
        relay?.Invoke(text);
    }
}
