using System.Runtime.InteropServices;
using SimConnect.NET;
using SimConnect.NET.Events;

namespace Fonia.Sim.Msfs;

/// <summary>
/// An <see cref="IFacilityTransport"/> on top of SimConnect.NET's connection: its client opens and holds the connection and hands over the raw
/// messages; the two calls the library keeps to itself are made by <see cref="NativeFacilities"/> with the client's handle. Not testable without
/// MSFS: the reading of the messages is kept to the copying of the fields, and everything else is in <see cref="MsfsFacilityReader"/>.
/// </summary>
internal sealed class SimConnectNetFacilityTransport : IFacilityTransport
{
    private const string ApplicationName = "Fonia layout";

    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);

    // Offsets in SIMCONNECT_RECV_FACILITY_DATA (confirmed against the library's own structure): the header is 12 bytes.
    private const int RequestIdOffset = 12;
    private const int TypeOffset = 24;
    private const int IsListItemOffset = 28;
    private const int ItemIndexOffset = 32;
    private const int ListSizeOffset = 36;
    private const int DataOffset = 40;

    // SIMCONNECT_RECV_EXCEPTION: dwException, dwSendID, dwIndex after the header.
    private const int ExceptionCodeOffset = 12;
    private const int ExceptionSendIdOffset = 16;
    private const int ExceptionIndexOffset = 20;

    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SimConnectClient? _client;

    public event Action<FacilityMessage>? MessageReceived;

    public bool? IsMsfs2024 => _client?.IsMSFS2024;

    public async Task OpenAsync(CancellationToken ct)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("The transport is already open.");
        }

        NativeFacilities.EnsureResolver();

        var client = new SimConnectClient(ApplicationName) { AutoReconnectEnabled = false };
        client.RawMessageReceived += OnRawMessage;
        _client = client;

        try
        {
            await client.ConnectAsync(cancellationToken: ct);
            await _opened.Task.WaitAsync(OpenTimeout, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            throw new FacilityReadException($"MSFS did not answer in {OpenTimeout.TotalSeconds:0} s. Is a flight loaded?", ex);
        }
        catch (Exception ex)
        {
            throw new FacilityReadException("MSFS is not reachable. Is it running?", ex);
        }
    }

    public int Define(uint defineId, IReadOnlyList<string> fields)
    {
        var handle = Handle();

        foreach (var field in fields)
        {
            var code = Call(() => NativeFacilities.AddToFacilityDefinition(handle, defineId, field));

            if (code != 0)
            {
                return code;
            }
        }

        return 0;
    }

    public int Request(uint defineId, uint requestId, string icao)
    {
        var handle = Handle();

        return Call(() => NativeFacilities.RequestFacilityData(handle, defineId, requestId, icao, null));
    }

    public async ValueTask DisposeAsync()
    {
        var client = _client;
        _client = null;

        if (client is null)
        {
            return;
        }

        client.RawMessageReceived -= OnRawMessage;

        try
        {
            // Disposing can hang on a dead connection: never let it block the end of the command.
            await client.DisposeAsync().AsTask().WaitAsync(DisposeTimeout);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or ObjectDisposedException)
        {
            // Nothing to do: the process ends right after.
        }
    }

    private IntPtr Handle() => _client?.Handle ?? throw new InvalidOperationException("The transport is not open.");

    // A missing or wrong SimConnect.dll shows up here, at the first call, as an exception and not as a code.
    private static int Call(Func<int> call)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new FacilityReadException($"SimConnect.dll could not be used: {ex.Message}", ex);
        }
    }

    private void OnRawMessage(object? sender, RawSimConnectMessageEventArgs e)
    {
        try
        {
            switch (e.MessageId)
            {
                case SimConnectRecvId.Open:
                    _opened.TrySetResult();
                    break;
                case SimConnectRecvId.FacilityData:
                    Publish(ReadData(e.DataPointer));
                    break;
                case SimConnectRecvId.FacilityDataEnd:
                    Publish(new FacilityEndMessage((uint)Marshal.ReadInt32(e.DataPointer, RequestIdOffset)));
                    break;
                case SimConnectRecvId.Exception:
                    Publish(new FacilityExceptionMessage(
                        (uint)Marshal.ReadInt32(e.DataPointer, ExceptionCodeOffset),
                        (uint)Marshal.ReadInt32(e.DataPointer, ExceptionSendIdOffset),
                        (uint)Marshal.ReadInt32(e.DataPointer, ExceptionIndexOffset)));
                    break;
                case SimConnectRecvId.FacilityMinimalList:
                    Publish(new FacilityAmbiguousMessage());
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // This runs on the library's thread: whatever goes wrong here must not take it down. The request that was waiting times out and says so.
        }
    }

    // The buffer is valid only during the callback: the fields are copied out of it here.
    private static FacilityDataMessage ReadData(IntPtr message)
    {
        var size = Marshal.ReadInt32(message, 0);
        var data = new byte[Math.Max(0, size - DataOffset)];

        if (data.Length > 0)
        {
            Marshal.Copy(message + DataOffset, data, 0, data.Length);
        }

        return new FacilityDataMessage(
            (uint)Marshal.ReadInt32(message, RequestIdOffset),
            KindOf((SimConnectFacilityDataType)(uint)Marshal.ReadInt32(message, TypeOffset)),
            Marshal.ReadInt32(message, IsListItemOffset) != 0,
            (uint)Marshal.ReadInt32(message, ItemIndexOffset),
            (uint)Marshal.ReadInt32(message, ListSizeOffset),
            data);
    }

    private static FacilityKind KindOf(SimConnectFacilityDataType type) => type switch
    {
        SimConnectFacilityDataType.Airport => FacilityKind.Airport,
        SimConnectFacilityDataType.Runway => FacilityKind.Runway,
        SimConnectFacilityDataType.Frequency => FacilityKind.Frequency,
        SimConnectFacilityDataType.TaxiPoint => FacilityKind.TaxiPoint,
        SimConnectFacilityDataType.TaxiParking => FacilityKind.TaxiParking,
        SimConnectFacilityDataType.TaxiPath => FacilityKind.TaxiPath,
        SimConnectFacilityDataType.TaxiName => FacilityKind.TaxiName,
        _ => FacilityKind.Other,
    };

    private void Publish(FacilityMessage message) => MessageReceived?.Invoke(message);
}
