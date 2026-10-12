using SimConnect.NET;

namespace Fonia.Sim.Msfs;

// One struct per group of SimVars, in the layout SimConnect.NET fills. Names and units are the ones the
// spike validated against MSFS 2020 (docs/decisions/simconnect.md); SimConnectStructsTests pins them.
//
// The structs and their fields must be public: SimConnect.NET finds the fields by reflection and binds the
// generic request type with `dynamic`, which fails ("'object' does not contain a definition for
// 'OffsetBytes'") when the struct is internal. Only ToData() is internal.
//
// Every data type is declared on purpose. Left out, the library infers it from the variable name, and it
// inferred Integer32 for the COM1 frequencies, which is why a double read in MHz lost its decimals (122.8
// became 122). Hz happened to survive because those values are whole numbers.

#pragma warning disable CA1051 // Do not declare visible instance fields: the library requires public fields.

public struct MotionStruct
{
    [SimConnect("PLANE LATITUDE", "degrees", SimConnectDataType.FloatDouble)]
    public double Latitude;

    [SimConnect("PLANE LONGITUDE", "degrees", SimConnectDataType.FloatDouble)]
    public double Longitude;

    [SimConnect("PLANE ALTITUDE", "feet", SimConnectDataType.FloatDouble)]
    public double AltitudeMsl;

    [SimConnect("PLANE ALT ABOVE GROUND", "feet", SimConnectDataType.FloatDouble)]
    public double AltitudeAgl;

    [SimConnect("GROUND VELOCITY", "knots", SimConnectDataType.FloatDouble)]
    public double GroundSpeed;

    [SimConnect("PLANE HEADING DEGREES MAGNETIC", "degrees", SimConnectDataType.FloatDouble)]
    public double HeadingMagnetic;

    [SimConnect("SIM ON GROUND", "bool", SimConnectDataType.Integer32)]
    public int OnGround;

    internal readonly MotionData ToData() => new(Latitude, Longitude, AltitudeMsl, AltitudeAgl, GroundSpeed, HeadingMagnetic, OnGround);
}

public struct IdentityStruct
{
    [SimConnect("ATC ID", SimConnectDataType.String32)]
    public string AtcId;

    [SimConnect("ATC TYPE", SimConnectDataType.String32)]
    public string AtcType;

    [SimConnect("ATC MODEL", SimConnectDataType.String32)]
    public string AtcModel;

    [SimConnect("TITLE", SimConnectDataType.String128)]
    public string Title;

    internal readonly IdentityData ToData() => new(AtcId ?? string.Empty, AtcType ?? string.Empty, AtcModel ?? string.Empty, Title ?? string.Empty);
}

public struct RadiosStruct
{
    // Hz, never MHz: through the library's struct path the MHz unit returned wrong COM1 values.
    [SimConnect("COM ACTIVE FREQUENCY:1", "Hz", SimConnectDataType.FloatDouble)]
    public double Com1Active;

    [SimConnect("COM STANDBY FREQUENCY:1", "Hz", SimConnectDataType.FloatDouble)]
    public double Com1Standby;

    [SimConnect("COM ACTIVE FREQUENCY:2", "Hz", SimConnectDataType.FloatDouble)]
    public double Com2Active;

    [SimConnect("COM STANDBY FREQUENCY:2", "Hz", SimConnectDataType.FloatDouble)]
    public double Com2Standby;

    internal readonly RadioData ToData() => new(Com1Active, Com1Standby, Com2Active, Com2Standby);
}

public struct TransponderStruct
{
    [SimConnect("TRANSPONDER CODE:1", "bco16", SimConnectDataType.Integer32)]
    public int Code;

    internal readonly TransponderData ToData() => new(Code);
}
