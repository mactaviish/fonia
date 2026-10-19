namespace Fonia.Sim.Msfs.Tests;

// The schemas are the contract with the simulator: the field names of the SDK and the order of the bytes. What the SDK says is written here
// again on purpose, so that a change to either side shows up.
public class FacilitySchemasTests
{
    [Theory]
    [InlineData("AIRPORT", 100)]
    [InlineData("RUNWAY", 56)]
    [InlineData("FREQUENCY", 72)]
    [InlineData("TAXI_POINT", 16)]
    [InlineData("TAXI_PATH", 28)]
    [InlineData("TAXI_NAME", 32)]
    [InlineData("TAXI_PARKING", 40)]
    public void Each_item_has_the_size_the_fields_of_the_sdk_add_up_to(string name, int size)
    {
        FacilitySchema.All.Single(s => s.Name == name).Size.ShouldBe(size);
    }

    [Fact]
    public void The_data_made_by_hand_has_the_size_of_the_schema()
    {
        FacilityTestData.Airport().Length.ShouldBe(FacilitySchema.Airport.Size);
        FacilityTestData.Runway().Length.ShouldBe(FacilitySchema.Runway.Size);
        FacilityTestData.Frequency(6, 118_275_000, "TORRE").Length.ShouldBe(FacilitySchema.Frequency.Size);
        FacilityTestData.TaxiPoint(1, 0, 0).Length.ShouldBe(FacilitySchema.TaxiPoint.Size);
        FacilityTestData.TaxiPath(1, 0, 0, 1, 0).Length.ShouldBe(FacilitySchema.TaxiPath.Size);
        FacilityTestData.TaxiName("A").Length.ShouldBe(FacilitySchema.TaxiName.Size);
        FacilityTestData.Parking().Length.ShouldBe(FacilitySchema.TaxiParking.Size);
    }

    [Fact]
    public void The_definition_of_a_child_opens_the_airport_then_the_child_and_closes_them_in_reverse()
    {
        FacilitySchema.Runway.Definition().ShouldBe(
        [
            "OPEN AIRPORT",
            "OPEN RUNWAY",
            "LATITUDE",
            "LONGITUDE",
            "ALTITUDE",
            "HEADING",
            "LENGTH",
            "WIDTH",
            "SURFACE",
            "PRIMARY_NUMBER",
            "PRIMARY_DESIGNATOR",
            "SECONDARY_NUMBER",
            "SECONDARY_DESIGNATOR",
            "CLOSE RUNWAY",
            "CLOSE AIRPORT",
        ]);
    }

    [Fact]
    public void The_definition_of_the_airport_itself_has_no_child()
    {
        var definition = FacilitySchema.Airport.Definition();

        definition[0].ShouldBe("OPEN AIRPORT");
        definition[^1].ShouldBe("CLOSE AIRPORT");
        definition.ShouldNotContain(d => d.StartsWith("OPEN ", StringComparison.Ordinal) && d != "OPEN AIRPORT");
        definition.ShouldContain("N_TAXI_PATHS");
    }

    [Fact]
    public void Every_part_of_an_airport_is_asked_once_and_the_airport_first()
    {
        FacilitySchema.All.Select(s => s.Name).ShouldBe(["AIRPORT", "RUNWAY", "FREQUENCY", "TAXI_POINT", "TAXI_PATH", "TAXI_NAME", "TAXI_PARKING"]);
    }

    [Fact]
    public void Text_stops_at_the_first_zero_and_is_trimmed()
    {
        var data = new FacilityBytes().Text("  Gate A  ", 32).ToArray();

        FacilityParser.TaxiName(0, data).Name.ShouldBe("Gate A");
    }

    [Fact]
    public void Text_that_fills_its_whole_size_is_read_whole()
    {
        var data = new FacilityBytes().Text(new string('B', 32), 32).ToArray();

        FacilityParser.TaxiName(0, data).Name.ShouldBe(new string('B', 32));
    }

    [Fact]
    public void Reading_past_the_end_of_the_data_is_a_format_error()
    {
        Should.Throw<FormatException>(() => FacilityParser.Runway(new byte[10]));
    }

    [Fact]
    public void The_frequency_type_has_the_name_the_sdk_gives_it_and_the_number_when_it_has_none()
    {
        new RawFrequency(6, 118_275_000, "TORRE").TypeName.ShouldBe("TOWER");
        new RawFrequency(9, 119_950_000, "DEP").TypeName.ShouldBe("DEPARTURE");
        new RawFrequency(99, 118_000_000, "?").TypeName.ShouldBe("99");
        new RawFrequency(6, 118_275_000, "TORRE").KiloHertz.ShouldBe(118275);
    }
}
