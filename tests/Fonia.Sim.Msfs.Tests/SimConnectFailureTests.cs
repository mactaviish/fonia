using SimConnect.NET;

namespace Fonia.Sim.Msfs.Tests;

public class SimConnectFailureTests
{
    // The message SimConnect.NET produced in the real logs when MSFS had been closed.
    private const string DeadConnectionMessage =
        "request (RequestId=20020, DefinitionId=10008, ObjectId=0) failed: -2147467259 - SimConnect error: -2147467259";

    [Fact]
    public void The_failure_seen_when_msfs_was_closed_means_the_connection_is_dead()
    {
        SimConnectFailure.IsDeadConnection(new SimConnectException(DeadConnectionMessage)).ShouldBeTrue();
    }

    [Fact]
    public void The_hex_form_of_the_same_error_also_means_dead()
    {
        SimConnectFailure.IsDeadConnection(new SimConnectException("SimConnect error 0x80004005")).ShouldBeTrue();
    }

    [Fact]
    public void A_dead_connection_is_recognised_inside_a_wrapper_exception()
    {
        var wrapped = new InvalidDataException("probe failed", new SimConnectException(DeadConnectionMessage));

        SimConnectFailure.IsDeadConnection(wrapped).ShouldBeTrue();
        SimConnectFailure.IsDeadConnection(new AggregateException(new SimConnectException(DeadConnectionMessage))).ShouldBeTrue();
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))] // the library says it is not connected
    [InlineData(typeof(ObjectDisposedException))]
    public void A_client_the_library_considers_closed_is_dead(Type exceptionType)
    {
        var exception = exceptionType == typeof(ObjectDisposedException)
            ? new ObjectDisposedException("client")
            : (Exception)new InvalidOperationException("Not connected to SimConnect");

        SimConnectFailure.IsDeadConnection(exception).ShouldBeTrue();
    }

    [Fact]
    public void A_timeout_is_not_a_dead_connection()
    {
        // MSFS answers nothing while it is in a menu or loading a flight: that is "not responding", not "gone".
        SimConnectFailure.IsDeadConnection(new TimeoutException()).ShouldBeFalse();
        SimConnectFailure.IsDeadConnection(new OperationCanceledException()).ShouldBeFalse();
        SimConnectFailure.IsDeadConnection(new TaskCanceledException()).ShouldBeFalse();
    }

    [Fact]
    public void Other_simconnect_errors_are_not_a_dead_connection()
    {
        SimConnectFailure.IsDeadConnection(new SimConnectException("SimConnect server request failed: UnrecognizedId - Unknown ID")).ShouldBeFalse();
        SimConnectFailure.IsDeadConnection(new SimConnectException("something else")).ShouldBeFalse();
    }
}
