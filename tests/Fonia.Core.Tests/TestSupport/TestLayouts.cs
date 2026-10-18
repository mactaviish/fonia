using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Core.Tests.TestSupport;

/// <summary>
/// Airport layouts written by hand for the tests: never captures from a simulator. The toy airport is the case the taxi route exists for: the
/// taxiway from the apron to the holding point of runway 15 crosses runway 11/29.
/// </summary>
internal static class TestLayouts
{
    // Nodes (the numbers are the ids):
    //   1 apron (parking)   2 hold short of 11/29, west   3 where taxiway A crosses 11/29   4 hold short of 11/29, east
    //   5 hold short of 15  6 threshold 11   7 threshold 29   8 threshold 15   9 threshold 33
    //
    //   1 --A-- 2 --A-- 3 --A-- 4 --B-- 5 --B-- 8 ==15/33== 9        (3 is on the runway 11/29: 6 ==11/29== 3 ==11/29== 7)
    public static AirportLayout Toy() =>
        new(
            "SBXX",
            [
                new LayoutRunway(
                    new LayoutRunwayEnd("11", new GeoPoint(-25.5000, -49.1100), 110.5),
                    new LayoutRunwayEnd("29", new GeoPoint(-25.5040, -49.1000), 290.5),
                    1800,
                    45),
                new LayoutRunway(
                    new LayoutRunwayEnd("15", new GeoPoint(-25.5050, -49.1020), 150.2),
                    new LayoutRunwayEnd("33", new GeoPoint(-25.5100, -49.0990), 330.2),
                    2200,
                    45),
            ],
            [
                new TaxiNode(1, new GeoPoint(-25.5010, -49.1060), TaxiNodeKind.Parking),
                new TaxiNode(2, new GeoPoint(-25.5015, -49.1050), TaxiNodeKind.HoldShort),
                new TaxiNode(3, new GeoPoint(-25.5020, -49.1050), TaxiNodeKind.Normal),
                new TaxiNode(4, new GeoPoint(-25.5025, -49.1050), TaxiNodeKind.HoldShort),
                new TaxiNode(5, new GeoPoint(-25.5045, -49.1030), TaxiNodeKind.HoldShort),
                new TaxiNode(6, new GeoPoint(-25.5000, -49.1100), TaxiNodeKind.Normal),
                new TaxiNode(7, new GeoPoint(-25.5040, -49.1000), TaxiNodeKind.Normal),
                new TaxiNode(8, new GeoPoint(-25.5050, -49.1020), TaxiNodeKind.Normal),
                new TaxiNode(9, new GeoPoint(-25.5100, -49.0990), TaxiNodeKind.Normal),
            ],
            [
                new TaxiEdge(1, 2, "A", TaxiEdgeKind.Taxiway, null),
                new TaxiEdge(2, 3, "A", TaxiEdgeKind.Taxiway, null),
                new TaxiEdge(3, 4, "A", TaxiEdgeKind.Taxiway, null),
                new TaxiEdge(4, 5, "B", TaxiEdgeKind.Taxiway, null),
                new TaxiEdge(5, 8, "B", TaxiEdgeKind.Taxiway, null),
                new TaxiEdge(6, 3, null, TaxiEdgeKind.Runway, "11/29"),
                new TaxiEdge(3, 7, null, TaxiEdgeKind.Runway, "11/29"),
                new TaxiEdge(8, 9, null, TaxiEdgeKind.Runway, "15/33"),
            ],
            [new Parking("PATIO 1", 1, new GeoPoint(-25.5010, -49.1060), 1, ParkingKind.Ramp)],
            [
                new AirportFrequency(AirportFrequencyType.Ground, new Frequency(121900), "SOLO"),
                new AirportFrequency(AirportFrequencyType.Tower, new Frequency(118275), "TORRE"),
                new AirportFrequency(AirportFrequencyType.Approach, new Frequency(119950), "CONTROLE"),
                new AirportFrequency(AirportFrequencyType.Atis, new Frequency(127800), "ATIS"),
            ]);

    /// <summary>What a scenery without taxiways gives: the runways and nothing else.</summary>
    public static AirportLayout RunwaysOnly() => Toy() with { Nodes = [], Edges = [], Parkings = [], Frequencies = [] };
}
