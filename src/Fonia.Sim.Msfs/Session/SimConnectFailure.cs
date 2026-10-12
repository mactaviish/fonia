namespace Fonia.Sim.Msfs;

/// <summary>Tells a gone connection apart from a simulator that is merely busy.</summary>
internal static class SimConnectFailure
{
    // E_FAIL (0x80004005, -2147467259): what every SimConnect call returned once MSFS had been closed.
    private const int EFail = unchecked((int)0x80004005);
    private const string EFailDecimal = "-2147467259";
    private const string EFailHex = "0x80004005";

    /// <summary>
    /// True for the failures seen when MSFS went away: E_FAIL (by HResult or in the message, which is all the
    /// library's logs showed), or the library reporting that the client is closed or not connected. Timeouts and
    /// cancellations are false on purpose: MSFS also stops answering in menus and while loading a flight.
    /// </summary>
    public static bool IsDeadConnection(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions.Any(IsDeadConnection);
        }

        if (IsDeadSignal(exception))
        {
            return true;
        }

        return exception.InnerException is { } inner && IsDeadConnection(inner);
    }

    private static bool IsDeadSignal(Exception exception)
    {
        if (exception is ObjectDisposedException)
        {
            return true;
        }

        if (exception is OperationCanceledException or TimeoutException)
        {
            return false;
        }

        if (exception.HResult == EFail)
        {
            return true;
        }

        var message = exception.Message;

        return message.Contains(EFailDecimal, StringComparison.Ordinal)
            || message.Contains(EFailHex, StringComparison.OrdinalIgnoreCase)
            || (exception is InvalidOperationException && message.Contains("not connected", StringComparison.OrdinalIgnoreCase));
    }
}
