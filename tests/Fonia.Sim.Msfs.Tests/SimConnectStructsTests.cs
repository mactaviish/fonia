using System.Reflection;
using SimConnect.NET;

namespace Fonia.Sim.Msfs.Tests;

/// <summary>
/// The SimVar names and units are strings the compiler cannot check, and a typo only shows up against a
/// real simulator. These tests pin the definitions that the spike validated against MSFS 2020.
/// </summary>
public class SimConnectStructsTests
{
    private static List<(string Field, string Name, string? Unit, SimConnectDataType? DataType)> Definitions<T>()
    {
        return [.. typeof(T)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(f => f.MetadataToken)
            .Select(f =>
            {
                var attribute = f.GetCustomAttribute<SimConnectAttribute>().ShouldNotBeNull($"{f.Name} has no [SimConnect] attribute");
                return (f.Name, attribute.Name, attribute.Unit, attribute.DataType);
            })];
    }

    [Fact]
    public void Motion_reads_the_validated_variables_in_the_validated_units()
    {
        Definitions<MotionStruct>().ShouldBe(
        [
            ("Latitude", "PLANE LATITUDE", "degrees", SimConnectDataType.FloatDouble),
            ("Longitude", "PLANE LONGITUDE", "degrees", SimConnectDataType.FloatDouble),
            ("AltitudeMsl", "PLANE ALTITUDE", "feet", SimConnectDataType.FloatDouble),
            ("AltitudeAgl", "PLANE ALT ABOVE GROUND", "feet", SimConnectDataType.FloatDouble),
            ("GroundSpeed", "GROUND VELOCITY", "knots", SimConnectDataType.FloatDouble),
            ("HeadingMagnetic", "PLANE HEADING DEGREES MAGNETIC", "degrees", SimConnectDataType.FloatDouble),
            ("OnGround", "SIM ON GROUND", "bool", SimConnectDataType.Integer32),
        ]);
    }

    [Fact]
    public void Identity_reads_text_variables_with_fixed_string_sizes()
    {
        Definitions<IdentityStruct>().ShouldBe(
        [
            ("AtcId", "ATC ID", null, SimConnectDataType.String32),
            ("AtcType", "ATC TYPE", null, SimConnectDataType.String32),
            ("AtcModel", "ATC MODEL", null, SimConnectDataType.String32),
            ("Title", "TITLE", null, SimConnectDataType.String128),
        ]);
    }

    [Fact]
    public void Radios_are_read_in_hz_never_in_mhz()
    {
        // The library's struct path returned wrong COM1 values for the MHz unit; Hz matched exactly.
        var definitions = Definitions<RadiosStruct>();

        definitions.ShouldBe(
        [
            ("Com1Active", "COM ACTIVE FREQUENCY:1", "Hz", SimConnectDataType.FloatDouble),
            ("Com1Standby", "COM STANDBY FREQUENCY:1", "Hz", SimConnectDataType.FloatDouble),
            ("Com2Active", "COM ACTIVE FREQUENCY:2", "Hz", SimConnectDataType.FloatDouble),
            ("Com2Standby", "COM STANDBY FREQUENCY:2", "Hz", SimConnectDataType.FloatDouble),
        ]);
        definitions.ShouldAllBe(d => d.Unit == "Hz");
    }

    [Fact]
    public void The_transponder_is_read_as_bco16()
    {
        Definitions<TransponderStruct>().ShouldBe(
        [
            ("Code", "TRANSPONDER CODE:1", "bco16", SimConnectDataType.Integer32),
        ]);
    }

    [Fact]
    public void Structs_convert_to_the_raw_data_the_assembler_expects()
    {
        new MotionStruct
        {
            Latitude = -27.1,
            Longitude = -52.6,
            AltitudeMsl = 5675,
            AltitudeAgl = 3637,
            GroundSpeed = 79.4,
            HeadingMagnetic = 21,
            OnGround = 1,
        }.ToData().ShouldBe(new MotionData(-27.1, -52.6, 5675, 3637, 79.4, 21, 1));

        new IdentityStruct { AtcId = "PR-FAB", AtcType = "Cessna", AtcModel = "C25C", Title = "CJ4" }
            .ToData().ShouldBe(new IdentityData("PR-FAB", "Cessna", "C25C", "CJ4"));

        new RadiosStruct { Com1Active = 1, Com1Standby = 2, Com2Active = 3, Com2Standby = 4 }
            .ToData().ShouldBe(new RadioData(1, 2, 3, 4));

        new TransponderStruct { Code = 0x1234 }.ToData().ShouldBe(new TransponderData(0x1234));
    }

    [Fact]
    public void Text_the_library_leaves_null_becomes_an_empty_string()
    {
        new IdentityStruct().ToData().ShouldBe(new IdentityData(string.Empty, string.Empty, string.Empty, string.Empty));
    }
}
