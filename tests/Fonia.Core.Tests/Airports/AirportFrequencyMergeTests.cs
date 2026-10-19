using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Airports;

// The simulator's frequencies in an airport of the airport data. The checks against the real frequencies showed the simulator right where the
// airport data was wrong or empty, so its kinds of unit replace the airport data's; what it does not give stays; and of the several frequencies
// of a unit, only one is named when passing a flight on.
public class AirportFrequencyMergeTests
{
    private static AirportFrequency F(AirportFrequencyType type, int kHz, string name = "x", bool primary = true) => new(type, new Frequency(kHz), name, primary);

    private static Airport Ours(params AirportFrequency[] frequencies) =>
        TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)]) with { Frequencies = frequencies };

    private static AirportFrequency[] Of(AirportFrequencyType type, params int[] kHz) => [.. kHz.Select(k => F(type, k))];

    private static IReadOnlyList<(AirportFrequencyType Type, int KHz, bool Primary)> Merged(Airport airport) =>
        [.. airport.Frequencies.Select(f => (f.Type, f.Frequency.KHz, f.Primary))];

    [Fact]
    public void The_simulators_frequency_replaces_the_one_of_the_airport_data_for_the_same_unit()
    {
        var merged = AirportFrequencyMerge.Apply(Ours(F(AirportFrequencyType.Tower, 118550)), [F(AirportFrequencyType.Tower, 118150)]);

        Merged(merged).ShouldBe([(AirportFrequencyType.Tower, 118150, true)]);
    }

    [Fact]
    public void A_unit_the_airport_data_lacks_is_added()
    {
        var merged = AirportFrequencyMerge.Apply(Ours(F(AirportFrequencyType.Tower, 118150)), [F(AirportFrequencyType.Tower, 118150), F(AirportFrequencyType.Delivery, 121050), F(AirportFrequencyType.Atis, 127675)]);

        Merged(merged).ShouldBe([(AirportFrequencyType.Tower, 118150, true), (AirportFrequencyType.Atis, 127675, true), (AirportFrequencyType.Delivery, 121050, true)]);
    }

    [Fact]
    public void What_the_simulator_does_not_say_stays_as_the_airport_data_has_it()
    {
        var ours = Ours(
            F(AirportFrequencyType.Tower, 118550),
            F(AirportFrequencyType.Afis, 131950),
            F(AirportFrequencyType.Operations, 122500),
            F(AirportFrequencyType.Other, 110700));

        var merged = AirportFrequencyMerge.Apply(ours, [F(AirportFrequencyType.Ground, 121700)]);

        Merged(merged).ShouldBe(
        [
            (AirportFrequencyType.Tower, 118550, true),
            (AirportFrequencyType.Afis, 131950, true),
            (AirportFrequencyType.Operations, 122500, true),
            (AirportFrequencyType.Other, 110700, true),
            (AirportFrequencyType.Ground, 121700, true),
        ]);
    }

    [Fact]
    public void An_airport_is_never_left_without_the_unit_the_airport_data_had_because_the_simulator_gave_none_of_it()
    {
        var merged = AirportFrequencyMerge.Apply(Ours(F(AirportFrequencyType.Tower, 118400)), [F(AirportFrequencyType.Radio, 125500)]);

        Merged(merged).ShouldBe([(AirportFrequencyType.Tower, 118400, true)]);
    }

    [Fact]
    public void The_advisory_and_other_frequencies_of_the_simulator_are_not_taken()
    {
        var merged = AirportFrequencyMerge.Apply(
            Ours(F(AirportFrequencyType.Tower, 118150)),
            [F(AirportFrequencyType.Radio, 131825), F(AirportFrequencyType.Other, 125000), F(AirportFrequencyType.Afis, 120000)]);

        Merged(merged).ShouldBe([(AirportFrequencyType.Tower, 118150, true)]);
    }

    [Fact]
    public void Of_several_frequencies_the_one_the_airport_data_had_is_the_one_named()
    {
        // The Controle of Curitiba has six sectors in the simulator; the airport data names one of them.
        var merged = AirportFrequencyMerge.Apply(Ours(F(AirportFrequencyType.Approach, 119950)), Of(AirportFrequencyType.Approach, 119700, 119950, 120650, 120950));

        Merged(merged).ShouldBe(
        [
            (AirportFrequencyType.Approach, 119950, true),
            (AirportFrequencyType.Approach, 119700, false),
            (AirportFrequencyType.Approach, 120650, false),
            (AirportFrequencyType.Approach, 120950, false),
        ]);
        merged.FrequencyOf(AirportFrequencyType.Approach)!.Frequency.ShouldBe(new Frequency(119950));
    }

    [Fact]
    public void Of_several_sectors_with_none_the_airport_data_had_no_Controle_is_named()
    {
        var merged = AirportFrequencyMerge.Apply(Ours(), Of(AirportFrequencyType.Approach, 119600, 120250, 120900));

        merged.Frequencies.Count.ShouldBe(3);
        merged.Frequencies.ShouldAllBe(f => !f.Primary);
        merged.FrequencyOf(AirportFrequencyType.Approach).ShouldBeNull();
    }

    [Fact]
    public void A_single_Controle_the_airport_data_did_not_have_is_the_one_named()
    {
        var merged = AirportFrequencyMerge.Apply(Ours(F(AirportFrequencyType.Approach, 119800)), [F(AirportFrequencyType.Approach, 120250)]);

        Merged(merged).ShouldBe([(AirportFrequencyType.Approach, 120250, true)]);
    }

    [Fact]
    public void Of_several_towers_the_airport_datas_is_named_and_otherwise_the_first_the_simulator_lists()
    {
        var towers = Of(AirportFrequencyType.Tower, 118400, 132750, 135200);

        AirportFrequencyMerge.Apply(Ours(F(AirportFrequencyType.Tower, 132750)), towers).FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(new Frequency(132750));
        AirportFrequencyMerge.Apply(Ours(), towers).FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(new Frequency(118400));
        AirportFrequencyMerge.Apply(Ours(F(AirportFrequencyType.Tower, 118550)), towers).FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(new Frequency(118400));
    }

    [Fact]
    public void Every_frequency_of_a_unit_still_answers_when_tuned_even_if_it_is_not_the_one_named()
    {
        var merged = AirportFrequencyMerge.Apply(Ours(), [F(AirportFrequencyType.Approach, 119600), F(AirportFrequencyType.Approach, 120250), F(AirportFrequencyType.Tower, 118150)]);

        FrequencyResolver.UnitOn(merged, new Frequency(119600)).ShouldBe(ServiceUnit.Approach);
        FrequencyResolver.UnitOn(merged, new Frequency(120250)).ShouldBe(ServiceUnit.Approach);
        FrequencyResolver.UnitOn(merged, new Frequency(118150)).ShouldBe(ServiceUnit.Tower);
        FrequencyResolver.UnitOn(merged, new Frequency(118550)).ShouldBe(ServiceUnit.None);
    }

    [Fact]
    public void The_same_frequency_twice_in_the_simulator_is_one()
    {
        var merged = AirportFrequencyMerge.Apply(Ours(), [F(AirportFrequencyType.Ground, 121700), F(AirportFrequencyType.Ground, 121700, "again")]);

        merged.Frequencies.Count.ShouldBe(1);
    }

    [Fact]
    public void Without_frequencies_from_the_simulator_the_airport_is_the_same_object()
    {
        var airport = Ours(F(AirportFrequencyType.Tower, 118550));

        AirportFrequencyMerge.Apply(airport, []).ShouldBeSameAs(airport);
    }

    [Fact]
    public void A_frequency_that_is_not_named_is_not_listed_by_the_airport_for_passing_a_flight_on()
    {
        var merged = Ours(F(AirportFrequencyType.Tower, 118150, primary: false), F(AirportFrequencyType.Tower, 118400));

        merged.FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(new Frequency(118400));
        merged.HasFrequency(AirportFrequencyType.Tower).ShouldBeTrue();
        Ours(F(AirportFrequencyType.Tower, 118150, primary: false)).HasFrequency(AirportFrequencyType.Tower).ShouldBeFalse();
    }
}
