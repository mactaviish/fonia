using Microsoft.Extensions.Logging;

namespace Fonia.Sim.Msfs;

/// <summary>Creates a fresh <see cref="SimConnectNetSession"/> each time, as the reconnection plan needs.</summary>
internal sealed class SimConnectNetSessionFactory(ILoggerFactory loggerFactory) : ISimConnectSessionFactory
{
    public ISimConnectSession Create()
    {
        return new SimConnectNetSession(loggerFactory.CreateLogger<SimConnectNetSession>());
    }
}
