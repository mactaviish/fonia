using Fonia.Core.Audio;

namespace Fonia.Core.Tests.Audio;

public class PolledPushToTalkTests
{
    private bool _down;
    private int _pressed;
    private int _released;

    private PolledPushToTalk Create()
    {
        var ptt = new PolledPushToTalk(() => _down);
        ptt.Pressed += (_, _) => _pressed++;
        ptt.Released += (_, _) => _released++;
        return ptt;
    }

    [Fact]
    public void It_raises_Pressed_once_when_the_key_goes_down_and_Released_once_when_it_comes_up()
    {
        using var ptt = Create();

        ptt.Poll();
        _down = true;
        ptt.Poll();
        ptt.Poll();
        ptt.Poll();
        ptt.IsPressed.ShouldBeTrue();
        _down = false;
        ptt.Poll();
        ptt.Poll();

        _pressed.ShouldBe(1);
        _released.ShouldBe(1);
        ptt.IsPressed.ShouldBeFalse();
    }

    [Fact]
    public void A_key_already_down_when_polling_starts_counts_as_a_press()
    {
        _down = true;
        using var ptt = Create();

        ptt.Poll();

        _pressed.ShouldBe(1);
    }

    [Fact]
    public void Each_press_raises_its_own_events()
    {
        using var ptt = Create();

        for (var i = 0; i < 3; i++)
        {
            _down = true;
            ptt.Poll();
            _down = false;
            ptt.Poll();
        }

        _pressed.ShouldBe(3);
        _released.ShouldBe(3);
    }

    [Fact]
    public void Disposing_while_pressed_releases_so_that_a_recording_never_hangs()
    {
        var ptt = Create();
        _down = true;
        ptt.Poll();

        ptt.Dispose();

        _released.ShouldBe(1);
        ptt.IsPressed.ShouldBeFalse();
    }

    [Fact]
    public async Task Started_it_polls_by_itself_until_disposed()
    {
        var pressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _down = true;
        using var ptt = new PolledPushToTalk(() => _down, TimeSpan.FromMilliseconds(5));
        ptt.Pressed += (_, _) => pressed.TrySetResult();

        ptt.StartWatching();

        await pressed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_handler_may_dispose_from_the_polling_thread_without_deadlocking()
    {
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _down = true;
        var ptt = new PolledPushToTalk(() => _down, TimeSpan.FromMilliseconds(5));
        ptt.Pressed += (_, _) =>
        {
            ptt.Dispose();
            disposed.TrySetResult();
        };

        ptt.StartWatching();

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Poll_after_dispose_does_nothing()
    {
        var ptt = Create();
        ptt.Dispose();
        _down = true;

        ptt.Poll();

        _pressed.ShouldBe(0);
    }
}
