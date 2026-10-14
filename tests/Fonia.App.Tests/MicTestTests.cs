using System.Text;
using Fonia.Core.Audio;

namespace Fonia.App.Tests;

public class MicTestTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fonia-mictest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class Capture : IAudioCapture
    {
        public AudioFormat Format => AudioFormat.Speech;

        public event EventHandler<AudioDataEventArgs>? DataAvailable;

        public void StartCapture()
        {
        }

        public void StopCapture()
        {
        }

        public void Dispose()
        {
        }

        public void Feed(int milliseconds) =>
            DataAvailable?.Invoke(this, new AudioDataEventArgs(new byte[AudioFormat.Speech.BytesFor(TimeSpan.FromMilliseconds(milliseconds))]));
    }

    private sealed class Ptt : IPushToTalk
    {
        public bool IsPressed { get; private set; }

        public event EventHandler? Pressed;

        public event EventHandler? Released;

        public void StartWatching()
        {
        }

        public void StopWatching()
        {
        }

        public void Dispose()
        {
        }

        public void Press()
        {
            IsPressed = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        public void Release()
        {
            IsPressed = false;
            Released?.Invoke(this, EventArgs.Empty);
        }
    }

    private readonly Capture _capture = new();
    private readonly Ptt _ptt = new();
    private readonly StringWriter _output = new(new StringBuilder()) { NewLine = "\n" };
    private readonly StringWriter _error = new(new StringBuilder()) { NewLine = "\n" };

    private MicTest Create() => new(_capture, _ptt, "RControl", _output, _error);

    /// <summary>Waits until the test has started listening, which it announces on the output.</summary>
    private async Task WaitForPromptAsync()
    {
        while (!_output.ToString().Contains("Segure", StringComparison.Ordinal))
        {
            await Task.Delay(5, Ct);
        }
    }

    [Fact]
    public async Task It_saves_the_first_speech_as_a_wav_and_ends()
    {
        var path = Path.Combine(_directory, "nested", "fala.wav");
        var run = Create().RunAsync(path, Ct);
        await WaitForPromptAsync();

        _ptt.Press();
        _capture.Feed(1000);
        _ptt.Release();

        (await run).ShouldBe(0);
        var wav = await File.ReadAllBytesAsync(path, Ct);
        wav.Length.ShouldBe(44 + AudioFormat.Speech.BytesFor(TimeSpan.FromSeconds(1)));
        Encoding.ASCII.GetString(wav, 0, 4).ShouldBe("RIFF");
        _output.ToString().ShouldContain("RControl");
        _output.ToString().ShouldContain("1,0 s");
        _output.ToString().ShouldContain(path);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_slip_of_the_finger_is_explained_and_listening_goes_on()
    {
        var path = Path.Combine(_directory, "fala.wav");
        var run = Create().RunAsync(path, Ct);
        await WaitForPromptAsync();

        _ptt.Press();
        _capture.Feed(100);
        _ptt.Release();

        run.IsCompleted.ShouldBeFalse();
        _error.ToString().ShouldContain("curta demais");

        _ptt.Press();
        _capture.Feed(600);
        _ptt.Release();

        (await run).ShouldBe(0);
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public async Task Cancelling_ends_without_writing_anything()
    {
        var path = Path.Combine(_directory, "fala.wav");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = Create().RunAsync(path, cancellation.Token);
        await WaitForPromptAsync();

        await cancellation.CancelAsync();

        (await run).ShouldBe(0);
        File.Exists(path).ShouldBeFalse();
        _output.ToString().ShouldContain("Cancelado");
    }

    [Fact]
    public async Task A_file_that_cannot_be_written_is_an_error_not_a_crash()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "arquivo");
        await File.WriteAllTextAsync(blocker, "x", Ct);
        var path = Path.Combine(blocker, "fala.wav"); // "arquivo" is a file, so it cannot be a folder
        var run = Create().RunAsync(path, Ct);
        await WaitForPromptAsync();

        _ptt.Press();
        _capture.Feed(600);
        _ptt.Release();

        (await run).ShouldBe(1);
        _error.ToString().ShouldContain("Não foi possível gravar");
    }
}
