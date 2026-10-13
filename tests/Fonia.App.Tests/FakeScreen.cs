using System.Text;

namespace Fonia.App.Tests;

/// <summary>
/// A tiny terminal for tests: it keeps the text as lines that the terminal wraps at the current width, and when the
/// window is resized it re-wraps everything (reflows) with the cursor staying at the same place in the text, as
/// Windows Terminal and modern consoles do. That reflow is what turned the one-line display into two lines when the
/// user shrank the window. It is both the output the app writes to and the <see cref="ITerminal"/> it asks about.
/// </summary>
internal sealed class FakeScreen : TextWriter, ITerminal
{
    private readonly object _gate = new();
    private readonly List<StringBuilder> _lines = [new StringBuilder()];
    private int _width;
    private int _line;   // which text line the cursor is in
    private int _offset; // where in that text line (characters from its start)

    public FakeScreen(int width)
    {
        _width = width;
        NewLine = "\n";
    }

    public override Encoding Encoding => Encoding.UTF8;

    public int Columns
    {
        get
        {
            lock (_gate)
            {
                return _width - 1;
            }
        }
    }

    public int WrapColumns
    {
        get
        {
            lock (_gate)
            {
                return _width;
            }
        }
    }

    public int CursorTop
    {
        get
        {
            lock (_gate)
            {
                return RowsBefore(_line) + (_offset / _width);
            }
        }
    }

    /// <summary>What is on the screen, one string per screen row, trailing spaces removed.</summary>
    public IReadOnlyList<string> Rows
    {
        get
        {
            lock (_gate)
            {
                var rows = new List<string>();

                foreach (var line in _lines)
                {
                    var text = line.ToString();

                    do
                    {
                        var take = Math.Min(_width, text.Length);
                        rows.Add(text[..take].TrimEnd());
                        text = text[take..];
                    }
                    while (text.Length > 0);
                }

                return rows;
            }
        }
    }

    /// <summary>The user drags the window edge: the terminal re-wraps the text and the cursor keeps its place in it.</summary>
    public void Resize(int width)
    {
        lock (_gate)
        {
            _width = width;
        }
    }

    public void SetCursor(int left, int top)
    {
        lock (_gate)
        {
            var row = 0;

            for (var i = 0; i < _lines.Count; i++)
            {
                var rows = RowCount(i);

                if (top < row + rows)
                {
                    _line = i;
                    _offset = ((top - row) * _width) + left;
                    return;
                }

                row += rows;
            }

            throw new ArgumentOutOfRangeException(nameof(top), top, "Below the end of the text.");
        }
    }

    public override void Write(char value)
    {
        lock (_gate)
        {
            switch (value)
            {
                case '\r':
                    _offset = _offset / _width * _width; // back to the start of the current screen row
                    break;
                case '\n':
                    _lines.Insert(_line + 1, new StringBuilder());
                    _line++;
                    _offset = 0;
                    break;
                default:
                    var text = _lines[_line];
                    text.Append(' ', Math.Max(0, _offset - text.Length));

                    if (_offset < text.Length)
                    {
                        text[_offset] = value;
                    }
                    else
                    {
                        text.Append(value);
                    }

                    _offset++;
                    break;
            }
        }
    }

    public override void Write(string? value)
    {
        foreach (var c in value ?? string.Empty)
        {
            Write(c);
        }
    }

    // Must be called with _gate held.
    private int RowCount(int line) => Math.Max(1, (_lines[line].Length + _width - 1) / _width);

    // Must be called with _gate held.
    private int RowsBefore(int line)
    {
        var rows = 0;

        for (var i = 0; i < line; i++)
        {
            rows += RowCount(i);
        }

        return rows;
    }
}
