using Fonia.Core.Sim;
using Fonia.Sim.Mock;
using Fonia.Sim.Msfs;
using Microsoft.Extensions.Logging;

namespace Fonia.App;

/// <summary>Where the data comes from: an adapter, plus the optional work that feeds it (the mock's taxi, a replay).</summary>
/// <param name="Drive">Work to run once the adapter is connected, or <see langword="null"/> when the adapter feeds itself (MSFS).</param>
/// <param name="EndsWhenDriveCompletes">True when the app should exit once <paramref name="Drive"/> finishes (a replay reaching its end).</param>
internal sealed record SimSource(ISimAdapter Adapter, Func<CancellationToken, Task>? Drive, bool EndsWhenDriveCompletes, string Description);

internal interface ISimSourceFactory
{
    /// <param name="replay">The opened recording, when the source is a replay.</param>
    SimSource Create(AppOptions options, Stream? replay);
}

/// <summary>Builds the real sources: MSFS, the mock taxi demo and the replay of a recording.</summary>
internal sealed class SimSourceFactory(ILoggerFactory loggerFactory, TimeProvider timeProvider) : ISimSourceFactory
{
    public SimSource Create(AppOptions options, Stream? replay)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Source switch
        {
            SimSourceKind.Msfs => new SimSource(
                new MsfsSimAdapter(loggerFactory, timeProvider),
                Drive: null,
                EndsWhenDriveCompletes: false,
                Description: "Microsoft Flight Simulator 2020"),
            SimSourceKind.Mock => CreateMock(),
            SimSourceKind.Replay => CreateReplay(options, replay ?? throw new ArgumentException("A replay source needs the opened recording.", nameof(replay))),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Source, "Unknown source."),
        };
    }

    // A taxi that repeats forever, stamped with the current time, so the app can be shown without a simulator.
    private SimSource CreateMock()
    {
        var sim = new MockSimAdapter(timeProvider);

        return new SimSource(
            sim,
            ct => RunTaxiDemoAsync(sim, ct),
            EndsWhenDriveCompletes: false,
            Description: "simulador de teste (mock): um taxi simulado");
    }

    private SimSource CreateReplay(AppOptions options, Stream recording)
    {
        var sim = new MockSimAdapter(timeProvider);
        var speed = double.IsPositiveInfinity(options.Speed) ? "sem esperar" : $"velocidade {options.Speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}x";

        return new SimSource(
            sim,
            ct => sim.ReplayAsync(recording, options.Speed, ct),
            EndsWhenDriveCompletes: true,
            Description: $"reprodução de '{Path.GetFileName(options.ReplayPath)}' ({speed})");
    }

    private async Task RunTaxiDemoAsync(MockSimAdapter sim, CancellationToken ct)
    {
        var script = MockAircraft.TaxiSequence(30);

        for (var i = 0; !ct.IsCancellationRequested; i++)
        {
            sim.SetState(script[i % script.Count] with { Timestamp = timeProvider.GetUtcNow() });
            await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, ct);
        }
    }
}
