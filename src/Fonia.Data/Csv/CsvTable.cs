using System.Text;

namespace Fonia.Data;

/// <summary>A data file is not valid (for example, a quoted field that never closes).</summary>
public sealed class DataFormatException : Exception
{
    public DataFormatException(string message, int lineNumber)
        : base($"Invalid data at line {lineNumber}: {message}")
    {
        LineNumber = lineNumber;
    }

    /// <summary>1-based line of the file where the problem was found.</summary>
    public int LineNumber { get; }
}

/// <summary>One data row of a <see cref="CsvTable"/>, read by column name.</summary>
public sealed class CsvRow
{
    private readonly Dictionary<string, int> _columns;
    private readonly List<string> _fields;

    internal CsvRow(Dictionary<string, int> columns, List<string> fields, int lineNumber)
    {
        _columns = columns;
        _fields = fields;
        LineNumber = lineNumber;
    }

    /// <summary>1-based line of the file where the row starts.</summary>
    public int LineNumber { get; }

    /// <summary>The field of <paramref name="column"/>, or an empty string if the file has no such column or the row is too short.</summary>
    public string this[string column]
    {
        get
        {
            return _columns.TryGetValue(column, out var index) && index < _fields.Count ? _fields[index] : string.Empty;
        }
    }

    public bool Has(string column) => _columns.ContainsKey(column);
}

/// <summary>
/// A small RFC 4180 reader: a header line, then rows; fields may be quoted (commas, quotes written as two quotes and line
/// breaks allowed inside); any line ending; blank lines skipped. Written by hand because the data files are simple and a
/// package would be the heaviest part of the app.
/// </summary>
public sealed class CsvTable
{
    private CsvTable(IReadOnlyList<string> columns, IReadOnlyList<CsvRow> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<CsvRow> Rows { get; }

    /// <summary>Reads UTF-8 (with or without a byte order mark).</summary>
    public static CsvTable Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);
        return Read(reader);
    }

    public static CsvTable Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var records = new List<(int Line, List<string> Fields)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;
        var rowHasContent = false;
        var line = 1;
        var rowStartLine = 1;
        var first = true;

        void EndRow()
        {
            if (rowHasContent)
            {
                fields.Add(field.ToString());
                records.Add((rowStartLine, fields));
                fields = [];
            }

            field.Clear();
            fieldWasQuoted = false;
            rowHasContent = false;
            line++;
            rowStartLine = line;
        }

        int next;

        while ((next = reader.Read()) >= 0)
        {
            var c = (char)next;

            if (first)
            {
                first = false;

                if (c == '﻿')
                {
                    continue; // a byte order mark that survived decoding
                }
            }

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0 && !fieldWasQuoted:
                    inQuotes = true;
                    fieldWasQuoted = true;
                    rowHasContent = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldWasQuoted = false;
                    rowHasContent = true;
                    break;
                case '\r':
                    if (reader.Peek() == '\n')
                    {
                        reader.Read();
                    }

                    EndRow();
                    break;
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(c);
                    rowHasContent = true;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new DataFormatException("a quoted field is never closed.", rowStartLine);
        }

        if (rowHasContent)
        {
            EndRow();
        }

        if (records.Count == 0)
        {
            throw new DataFormatException("the file is empty (it has no header).", 1);
        }

        var columns = records[0].Fields;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < columns.Count; i++)
        {
            index.TryAdd(columns[i], i);
        }

        var rows = records.Skip(1).Select(record => new CsvRow(index, record.Fields, record.Line)).ToList();
        return new CsvTable(columns, rows);
    }
}
