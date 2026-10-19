namespace Fonia.Sim.Msfs;

/// <summary>Which child of an airport a facility message carries.</summary>
internal enum FacilityKind
{
    Airport,
    Runway,
    Frequency,
    TaxiPoint,
    TaxiParking,
    TaxiPath,
    TaxiName,
    Other,
}

/// <summary>What the simulator sends back to a request for an airport, decoded just enough to be routed.</summary>
internal abstract record FacilityMessage;

/// <summary>One item (the airport itself, a runway, a taxi point...): <paramref name="Data"/> is its fields in the order they were defined.</summary>
internal sealed record FacilityDataMessage(uint RequestId, FacilityKind Kind, bool IsListItem, uint ItemIndex, uint ListSize, byte[] Data) : FacilityMessage;

/// <summary>The request is complete: everything that was asked has been sent.</summary>
internal sealed record FacilityEndMessage(uint RequestId) : FacilityMessage;

/// <summary>The simulator refused something. <paramref name="Index"/> is the position of the offending parameter in the call that failed.</summary>
internal sealed record FacilityExceptionMessage(uint Code, uint SendId, uint Index) : FacilityMessage;

/// <summary>The code matched more than one facility; the simulator sent a list to choose from instead of data.</summary>
internal sealed record FacilityAmbiguousMessage : FacilityMessage;

/// <summary>
/// The conversation with the simulator about facilities, apart from the library that holds it: the reader works against this and the tests
/// feed it messages made by hand. A definition is a list of field names ("OPEN AIRPORT", "OPEN RUNWAY", "LATITUDE", ...).
/// </summary>
internal interface IFacilityTransport : IAsyncDisposable
{
    /// <summary>Called from the library's own threads, with a message already copied out of its buffer.</summary>
    event Action<FacilityMessage>? MessageReceived;

    /// <summary>True when the simulator is MSFS 2024, false for 2020; null when it is not known.</summary>
    bool? IsMsfs2024 { get; }

    /// <exception cref="FacilityReadException">The simulator cannot be reached.</exception>
    Task OpenAsync(CancellationToken ct);

    /// <returns>The code the call returned: 0 is accepted. The simulator may still refuse later, with a <see cref="FacilityExceptionMessage"/>.</returns>
    int Define(uint defineId, IReadOnlyList<string> fields);

    /// <returns>The code the call returned: 0 is accepted.</returns>
    int Request(uint defineId, uint requestId, string icao);
}
