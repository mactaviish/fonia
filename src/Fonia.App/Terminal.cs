namespace Fonia.App;

/// <summary>
/// What the app needs to know about, and do to, the terminal to keep the state on a single line that is rewritten
/// in place. Everything is asked again on every update, because the user can resize the window at any moment.
/// </summary>
internal interface ITerminal
{
    /// <summary>How many characters a line can hold right now without wrapping and without being cut off; 0 if unknown.</summary>
    int Columns { get; }

    /// <summary>The width at which the terminal wraps text; 0 if unknown.</summary>
    int WrapColumns { get; }

    /// <summary>The screen row the cursor is on.</summary>
    int CursorTop { get; }

    void SetCursor(int left, int top);
}

/// <summary>The real console. Whatever it cannot find out it reports as unknown, never as an error.</summary>
internal sealed class ConsoleTerminal : ITerminal
{
    // One column less than the narrowest of the window and the buffer: writing in the last column makes some
    // terminals wrap to the next line.
    public int Columns => Measure(() => Math.Max(0, Math.Min(Console.WindowWidth, Console.BufferWidth) - 1));

    public int WrapColumns => Measure(() => Console.BufferWidth);

    public int CursorTop => Measure(() => Console.CursorTop);

    public void SetCursor(int left, int top)
    {
        try
        {
            Console.SetCursorPosition(left, top);
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException)
        {
            // The cursor cannot be placed there (the window changed under us): keep writing where it is.
        }
    }

    private static int Measure(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (IOException)
        {
            return 0; // no console to measure (output is not a terminal)
        }
    }
}
