using FailSharp.Core;
using Xunit;

namespace FailSharp.Core.Tests;

public class PositioningTests
{
    // A north/south runway 18/36 (primary heading 360) and an east/west 09/27 at a field at 50N 4E, elevation 100 ft.
    private static readonly RunwayInfo Rwy36 = new(50.0, 4.0, 100, 0.0, 6000, 36, 0, 18, 0);
    private static readonly RunwayInfo Rwy09 = new(50.0, 4.0, 100, 90.0, 3000, 9, 0, 27, 0);
    private static readonly AirportInfo Field = new("TEST", 50.0, 4.0, 100, new[] { Rwy36, Rwy09 });

    [Fact]
    public void SelectLandingEnd_PicksEndWithMostHeadwind()
    {
        var end = PatternPlanner.SelectLandingEnd(Field.Runways, windFromTrue: 270, windKts: 15);

        Assert.NotNull(end);
        Assert.Equal("27", end!.Name);
        Assert.Equal(270, end.LandingHeadingTrue, 3);
    }

    [Fact]
    public void SelectLandingEnd_PrefersLongerRunwayWhenHeadwindsAreSimilar()
    {
        // Wind from 315: 36 and 27 get the same headwind, the longer 18/36 should win.
        var end = PatternPlanner.SelectLandingEnd(Field.Runways, windFromTrue: 315, windKts: 20);

        Assert.Equal("36", end!.Name);
    }

    [Fact]
    public void SelectLandingEnd_CalmWind_PicksLongestRunway()
    {
        var end = PatternPlanner.SelectLandingEnd(Field.Runways, windFromTrue: 90, windKts: 1);

        Assert.Equal(6000, end!.Runway.LengthFt);
    }

    [Fact]
    public void SelectLandingEnd_IgnoresWaterRunways()
    {
        var water = Rwy09 with { PrimaryDesignator = PatternPlanner.DesignatorWater };

        Assert.Null(PatternPlanner.SelectLandingEnd(new[] { water }, 90, 10));
    }

    private static readonly RunwayReference Kept18 = new(180, "18", "kept");

    [Theory]
    [InlineData(10, 4)]   // light and variable, now slightly favouring 36
    [InlineData(80, 20)]  // strong near-crosswind, a few knots of tail on 18
    [InlineData(100, 20)] // ...and the gust swings to the other side
    public void ChooseLandingEnd_KeepsPreviousEndWhileWindTolerates(double windFrom, double windKts)
    {
        var selection = PatternPlanner.ChooseLandingEnd(new[] { Rwy36 }, windFrom, windKts, Kept18);

        Assert.Equal("18", selection!.End.Name);
        Assert.Equal("kept", selection.Reason);
    }

    [Fact]
    public void ChooseLandingEnd_SwitchesWhenTailwindTooStrong()
    {
        var selection = PatternPlanner.ChooseLandingEnd(new[] { Rwy36 }, windFromTrue: 0, windKts: 8, Kept18);

        Assert.Equal("36", selection!.End.Name);
        Assert.Equal("wind", selection.Reason);
    }

    [Fact]
    public void ChooseLandingEnd_SwitchesWhenAnotherRunwayIsMuchBetter()
    {
        // Previous 09 now has 20 kt of pure crosswind; 36 gets the full 20 kt as headwind.
        var selection = PatternPlanner.ChooseLandingEnd(Field.Runways, 0, 20, new RunwayReference(90, "09", "kept"));

        Assert.Equal("36", selection!.End.Name);
    }

    [Fact]
    public void ChooseLandingEnd_AtcRunwayWinsInLightWind_MatchedByHeading()
    {
        var atc = new RunwayReference(92, null, "ATC");

        var selection = PatternPlanner.ChooseLandingEnd(Field.Runways, 270, 3, atc, Kept18);

        Assert.Equal("09", selection!.End.Name);
        Assert.Equal("ATC", selection.Reason);
    }

    [Fact]
    public void ChooseLandingEnd_AtcRunwayFromAnotherAirport_FallsBackToPrevious()
    {
        var atc = new RunwayReference(135, null, "ATC"); // no 13/31 here

        var selection = PatternPlanner.ChooseLandingEnd(Field.Runways, 0, 2, atc, Kept18);

        Assert.Equal("18", selection!.End.Name);
    }

    [Fact]
    public void ChooseLandingEnd_NoReference_LightWind_PicksLongest()
    {
        var selection = PatternPlanner.ChooseLandingEnd(Field.Runways, 90, 4);

        Assert.Equal(6000, selection!.End.Runway.LengthFt);
        Assert.Equal("longest", selection.Reason);
    }

    [Fact]
    public void Final_IsOnExtendedCenterlineAtGlidepathDistance()
    {
        var end = PatternPlanner.SelectLandingEnd(new[] { Rwy36 }, windFromTrue: 0, windKts: 10)!;

        var p = PatternPlanner.Place(PositionTarget.Final, Field, end, 800, rightHandPattern: false);

        Assert.Equal(0, p.HeadingTrue, 3);
        Assert.Equal(900, p.AltitudeFtMsl, 3);
        Assert.Equal(4.0, p.Longitude, 6); // on centerline
        Assert.True(p.Latitude < end.ThresholdLatitude); // south of the threshold, i.e. before it
        var distance = Geo.DistanceNm(p.Latitude, p.Longitude, end.ThresholdLatitude, end.ThresholdLongitude);
        Assert.Equal(PatternPlanner.GlidepathDistanceNm(800), distance, 2);
        Assert.InRange(distance, 2.4, 2.6);
    }

    [Fact]
    public void Downwind_LeftTraffic_IsAbeamMidfieldOnTheLeftFlyingReciprocal()
    {
        var end = PatternPlanner.SelectLandingEnd(new[] { Rwy36 }, 0, 10)!;

        var p = PatternPlanner.Place(PositionTarget.Downwind, Field, end, 1000, rightHandPattern: false);

        Assert.Equal(180, p.HeadingTrue, 3);
        Assert.True(p.Longitude < 4.0); // west = left of a northbound landing
        Assert.Equal(50.0, p.Latitude, 3);
        Assert.Equal(PatternPlanner.PatternOffsetNm, Geo.DistanceNm(p.Latitude, p.Longitude, 50.0, 4.0), 3);
    }

    [Fact]
    public void Base_RightTraffic_IsOnTheRightFlyingTowardsCenterline()
    {
        var end = PatternPlanner.SelectLandingEnd(new[] { Rwy36 }, 0, 10)!;

        var p = PatternPlanner.Place(PositionTarget.Base, Field, end, 1000, rightHandPattern: true);

        Assert.Equal(270, p.HeadingTrue, 3); // east of centerline, flying west
        Assert.True(p.Longitude > 4.0);
        Assert.True(p.Latitude < end.ThresholdLatitude);
        Assert.Equal(1100, p.AltitudeFtMsl, 3);
    }

    [Fact]
    public void Overhead_IsAboveAirportReferencePoint()
    {
        var end = PatternPlanner.SelectLandingEnd(Field.Runways, 270, 10)!;

        var p = PatternPlanner.Place(PositionTarget.Overhead, Field, end, 2000, false);

        Assert.Equal(50.0, p.Latitude, 6);
        Assert.Equal(4.0, p.Longitude, 6);
        Assert.Equal(2100, p.AltitudeFtMsl, 3);
    }

    [Theory]
    [InlineData(9, 1, 90, "09L")]
    [InlineData(27, 2, 270, "27R")]
    [InlineData(36, 0, 0, "36")]
    [InlineData(37, 0, 2, "36")] // compass-named end: falls back to heading
    public void EndName_FormatsNumberAndDesignator(int number, int designator, double heading, string expected) =>
        Assert.Equal(expected, PatternPlanner.EndName(number, designator, heading));
}
