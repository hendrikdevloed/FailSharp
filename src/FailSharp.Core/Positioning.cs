namespace FailSharp.Core;

/// <summary>A runway as reported by the sim's facility data: its center point, elevation, the true heading of its primary end, and the end numbers/designators.</summary>
public sealed record RunwayInfo(
    double Latitude,
    double Longitude,
    double ElevationFt,
    double HeadingTrue,
    double LengthFt,
    int PrimaryNumber,
    int PrimaryDesignator,
    int SecondaryNumber,
    int SecondaryDesignator);

public sealed record AirportInfo(string Ident, double Latitude, double Longitude, double ElevationFt, IReadOnlyList<RunwayInfo> Runways);

/// <summary>One landing direction of a runway: landing on <see cref="Name"/> means flying <see cref="LandingHeadingTrue"/> and touching down past the threshold.</summary>
public sealed record RunwayEnd(
    RunwayInfo Runway,
    string Name,
    double LandingHeadingTrue,
    double ThresholdLatitude,
    double ThresholdLongitude,
    double HeadwindKts,
    double CrosswindKts);

/// <summary>A landing direction to stick to when the wind allows: matched by end name first, else by heading.</summary>
public sealed record RunwayReference(double LandingHeadingTrue, string? Name, string Source);

/// <summary>The chosen end and why ("ATC", "kept", "wind", "longest").</summary>
public sealed record RunwaySelection(RunwayEnd End, string Reason);

public enum PositionTarget
{
    Overhead,
    Downwind,
    Base,
    Final,
}

public sealed record Placement(double Latitude, double Longitude, double AltitudeFtMsl, double HeadingTrue);

public static class Geo
{
    public const double EarthRadiusNm = 3440.065;
    public const double FeetPerNm = 6076.12;
    public const double FeetPerMeter = 3.28084;

    public static double NormalizeDegrees(double degrees)
    {
        var d = degrees % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    public static double DistanceNm(double lat1, double lon1, double lat2, double lon2)
    {
        var p1 = ToRad(lat1);
        var p2 = ToRad(lat2);
        var dp = p2 - p1;
        var dl = ToRad(lon2 - lon1);
        var a = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * EarthRadiusNm * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>Great-circle destination from a start point along an initial true bearing.</summary>
    public static (double Latitude, double Longitude) Destination(double lat, double lon, double bearingTrue, double distanceNm)
    {
        var delta = distanceNm / EarthRadiusNm;
        var theta = ToRad(bearingTrue);
        var p1 = ToRad(lat);
        var l1 = ToRad(lon);

        var p2 = Math.Asin(Math.Sin(p1) * Math.Cos(delta) + Math.Cos(p1) * Math.Sin(delta) * Math.Cos(theta));
        var l2 = l1 + Math.Atan2(Math.Sin(theta) * Math.Sin(delta) * Math.Cos(p1), Math.Cos(delta) - Math.Sin(p1) * Math.Sin(p2));

        var lonDeg = (ToDeg(l2) + 540.0) % 360.0 - 180.0;
        return (ToDeg(p2), lonDeg);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    private static double ToDeg(double rad) => rad * 180.0 / Math.PI;
}

/// <summary>
/// Picks the runway end most likely in use for a given wind, and computes standard traffic-pattern
/// positions relative to it: downwind abeam the runway midpoint, base and final on a 3° glidepath
/// so the requested height above the field matches a normal approach picture.
/// </summary>
public static class PatternPlanner
{
    /// <summary>Lateral distance between the runway centerline and the downwind/base legs.</summary>
    public const double PatternOffsetNm = 1.0;

    public const double GlidepathDegrees = 3.0;

    /// <summary>Below this the wind is treated as light/variable: with no reference to stick to, the longest runway wins.</summary>
    public const double CalmWindKts = 5.0;

    /// <summary>Ends whose headwind is within this margin of the best one are considered equally good; the longest of them wins.</summary>
    public const double HeadwindToleranceKts = 3.0;

    public const int DesignatorWater = 4;

    public static IEnumerable<RunwayEnd> Ends(RunwayInfo runway, double windFromTrue, double windKts)
    {
        var halfLengthNm = runway.LengthFt / 2.0 / Geo.FeetPerNm;
        var primaryHeading = Geo.NormalizeDegrees(runway.HeadingTrue);
        var secondaryHeading = Geo.NormalizeDegrees(runway.HeadingTrue + 180.0);

        // Landing in the primary direction, the threshold is at the far side of the center when looking along that heading's reciprocal.
        var primaryThreshold = Geo.Destination(runway.Latitude, runway.Longitude, secondaryHeading, halfLengthNm);
        var secondaryThreshold = Geo.Destination(runway.Latitude, runway.Longitude, primaryHeading, halfLengthNm);

        yield return MakeEnd(runway, EndName(runway.PrimaryNumber, runway.PrimaryDesignator, primaryHeading), primaryHeading, primaryThreshold, windFromTrue, windKts);
        yield return MakeEnd(runway, EndName(runway.SecondaryNumber, runway.SecondaryDesignator, secondaryHeading), secondaryHeading, secondaryThreshold, windFromTrue, windKts);
    }

    /// <summary>A reference runway stays in use until its tailwind exceeds this...</summary>
    public const double MaxTailwindKts = 5.0;

    /// <summary>...or until another end offers at least this much more headwind.</summary>
    public const double SwitchMarginKts = 10.0;

    /// <summary>A reference landing heading matches a runway end within this many degrees.</summary>
    public const double ReferenceHeadingToleranceDeg = 15.0;

    /// <summary>Returns the most likely landing runway end, or null if the airport has no usable (non-water) runway.</summary>
    public static RunwayEnd? SelectLandingEnd(IEnumerable<RunwayInfo> runways, double windFromTrue, double windKts) =>
        ChooseLandingEnd(runways, windFromTrue, windKts)?.End;

    /// <summary>
    /// Chooses the landing end with hysteresis. <paramref name="references"/> are tried in order (e.g. the
    /// sim ATC's runway, then the end used last time here); the first one that matches an end of this
    /// airport is kept as long as the wind tolerates it — tailwind at most <see cref="MaxTailwindKts"/>
    /// and no other end better by <see cref="SwitchMarginKts"/> of headwind. That keeps light/variable
    /// winds and gusty near-crosswinds from flipping the pattern on every click.
    /// </summary>
    public static RunwaySelection? ChooseLandingEnd(IEnumerable<RunwayInfo> runways, double windFromTrue, double windKts, params RunwayReference?[] references)
    {
        var usable = runways.Where(r => r.PrimaryDesignator != DesignatorWater && r.LengthFt > 0).ToList();
        if (usable.Count == 0)
        {
            return null;
        }

        var ends = usable.SelectMany(r => Ends(r, windFromTrue, windKts)).ToList();
        var best = ends.Max(e => e.HeadwindKts);

        foreach (var reference in references)
        {
            if (reference is null)
            {
                continue;
            }

            var match = FindEnd(ends, reference);
            if (match is null)
            {
                continue; // e.g. the ATC runway belongs to another airport; try the next reference
            }

            if (-match.HeadwindKts <= MaxTailwindKts && match.HeadwindKts >= best - SwitchMarginKts)
            {
                return new RunwaySelection(match, reference.Source);
            }

            break; // the wind clearly overrides the reference
        }

        if (windKts < CalmWindKts)
        {
            // Calm and nothing to stick to: the longest runway, in whichever direction still has the (tiny) headwind.
            return new RunwaySelection(ends.OrderByDescending(e => e.Runway.LengthFt).ThenByDescending(e => e.HeadwindKts).First(), "longest");
        }

        var chosen = ends
            .Where(e => e.HeadwindKts >= best - HeadwindToleranceKts)
            .OrderByDescending(e => e.Runway.LengthFt)
            .ThenByDescending(e => e.HeadwindKts)
            .First();
        return new RunwaySelection(chosen, "wind");
    }

    private static RunwayEnd? FindEnd(IReadOnlyList<RunwayEnd> ends, RunwayReference reference)
    {
        if (reference.Name is { Length: > 0 } name && ends.FirstOrDefault(e => e.Name == name) is { } byName)
        {
            return byName;
        }

        return ends
            .Where(e => AngleDifference(e.LandingHeadingTrue, reference.LandingHeadingTrue) <= ReferenceHeadingToleranceDeg)
            .OrderBy(e => AngleDifference(e.LandingHeadingTrue, reference.LandingHeadingTrue))
            .ThenByDescending(e => e.Runway.LengthFt)
            .FirstOrDefault();
    }

    private static double AngleDifference(double a, double b)
    {
        var d = Geo.NormalizeDegrees(a - b);
        return d > 180.0 ? 360.0 - d : d;
    }

    /// <summary>Distance from the threshold at which a 3° glidepath passes the given height.</summary>
    public static double GlidepathDistanceNm(double heightFt) =>
        heightFt / Math.Tan(GlidepathDegrees * Math.PI / 180.0) / Geo.FeetPerNm;

    public static Placement Place(PositionTarget target, AirportInfo airport, RunwayEnd end, double heightAglFt, bool rightHandPattern)
    {
        var h = end.LandingHeadingTrue;
        var reciprocal = Geo.NormalizeDegrees(h + 180.0);

        // Direction from the centerline towards the pattern side (left of the landing direction for standard left traffic).
        var patternSide = Geo.NormalizeDegrees(h + (rightHandPattern ? 90.0 : -90.0));
        var runwayElevation = end.Runway.ElevationFt;

        switch (target)
        {
            case PositionTarget.Overhead:
                return new Placement(airport.Latitude, airport.Longitude, airport.ElevationFt + heightAglFt, h);

            case PositionTarget.Downwind:
            {
                var (lat, lon) = Geo.Destination(end.Runway.Latitude, end.Runway.Longitude, patternSide, PatternOffsetNm);
                return new Placement(lat, lon, runwayElevation + heightAglFt, reciprocal);
            }

            case PositionTarget.Base:
            {
                // Base + final together roughly fly the 3° glidepath distance for this height.
                var alongNm = Math.Max(0.5, GlidepathDistanceNm(heightAglFt) - PatternOffsetNm);
                var onCenterline = Geo.Destination(end.ThresholdLatitude, end.ThresholdLongitude, reciprocal, alongNm);
                var (lat, lon) = Geo.Destination(onCenterline.Latitude, onCenterline.Longitude, patternSide, PatternOffsetNm);
                var baseHeading = Geo.NormalizeDegrees(patternSide + 180.0); // flying back towards the centerline
                return new Placement(lat, lon, runwayElevation + heightAglFt, baseHeading);
            }

            case PositionTarget.Final:
            {
                var (lat, lon) = Geo.Destination(end.ThresholdLatitude, end.ThresholdLongitude, reciprocal, GlidepathDistanceNm(heightAglFt));
                return new Placement(lat, lon, runwayElevation + heightAglFt, h);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

    public static string EndName(int number, int designator, double headingTrue)
    {
        // Facility data numbers 1..36 are real runway numbers; larger values encode compass names (N, NE, ...).
        if (number is < 1 or > 36)
        {
            number = (int)Math.Round(Geo.NormalizeDegrees(headingTrue) / 10.0);
            if (number == 0)
            {
                number = 36;
            }
        }

        var baseName = number.ToString("00");

        var suffix = designator switch
        {
            1 => "L",
            2 => "R",
            3 => "C",
            _ => string.Empty,
        };

        return baseName + suffix;
    }

    private static RunwayEnd MakeEnd(RunwayInfo runway, string name, double heading, (double Latitude, double Longitude) threshold, double windFromTrue, double windKts)
    {
        var angle = (windFromTrue - heading) * Math.PI / 180.0;
        return new RunwayEnd(runway, name, heading, threshold.Latitude, threshold.Longitude, windKts * Math.Cos(angle), windKts * Math.Sin(angle));
    }
}
