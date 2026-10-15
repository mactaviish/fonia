using Fonia.Core.Pipeline;

namespace Fonia.App;

/// <summary>Passes everything on to the observer of the screen and keeps the exchanges that are cases. Tells the user when it does.</summary>
internal sealed class CaseRecordingObserver(IVoiceLoopObserver inner, CaseLog log, TextWriter output) : IVoiceLoopObserver
{
    public void OnHeard(string text) => inner.OnHeard(text);

    public void OnReplied(ExchangeResult result)
    {
        inner.OnReplied(result);

        var reasons = log.Record(result);

        if (reasons.Count > 0)
        {
            output.WriteLine($"  (guardado para melhoria: {string.Join(", ", reasons)})");
        }
    }

    public void OnAnnounced(AnnouncementResult result) => inner.OnAnnounced(result);

    public void OnChannelBusy() => inner.OnChannelBusy();

    public void OnTooShort(TimeSpan held) => inner.OnTooShort(held);

    public void OnFailed(Exception exception) => inner.OnFailed(exception);
}
