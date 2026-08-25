namespace ParentalTrack.Domain;

/// <summary>
/// Spherical-earth distance helpers. Accurate to roughly 0.5% — ample for a track drawn on a map,
/// and far cheaper than an ellipsoidal (Vincenty) solution.
/// </summary>
public static class GeoMath
{
    /// <summary>Mean earth radius in metres (IUGG).</summary>
    public const double EarthRadiusMeters = 6_371_008.8;

    private const double DegreesToRadians = Math.PI / 180d;

    /// <summary>
    /// Great-circle distance between two WGS-84 coordinates using the haversine formula.
    /// </summary>
    /// <param name="lat1">Latitude of the first point in degrees.</param>
    /// <param name="lon1">Longitude of the first point in degrees.</param>
    /// <param name="lat2">Latitude of the second point in degrees.</param>
    /// <param name="lon2">Longitude of the second point in degrees.</param>
    /// <returns>Distance in metres; zero when the two points coincide.</returns>
    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = lat1 * DegreesToRadians;
        var phi2 = lat2 * DegreesToRadians;
        var deltaPhi = (lat2 - lat1) * DegreesToRadians;
        var deltaLambda = (lon2 - lon1) * DegreesToRadians;

        var sinHalfDeltaPhi = Math.Sin(deltaPhi / 2);
        var sinHalfDeltaLambda = Math.Sin(deltaLambda / 2);

        var a = (sinHalfDeltaPhi * sinHalfDeltaPhi)
                + (Math.Cos(phi1) * Math.Cos(phi2) * sinHalfDeltaLambda * sinHalfDeltaLambda);

        // Clamp guards against a marginally-out-of-range value from floating point rounding.
        var c = 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0d, 1d)));

        return EarthRadiusMeters * c;
    }

    /// <summary>
    /// Sums the haversine distance between consecutive points of a track.
    /// </summary>
    /// <param name="points">Track points in travel order.</param>
    /// <returns>Total length in metres; zero for a null, empty or single-point track.</returns>
    public static double PathLengthMeters(IReadOnlyList<(double Lat, double Lon)> points)
    {
        if (points is null || points.Count < 2)
        {
            return 0d;
        }

        var total = 0d;
        for (var i = 1; i < points.Count; i++)
        {
            var previous = points[i - 1];
            var current = points[i];
            total += HaversineMeters(previous.Lat, previous.Lon, current.Lat, current.Lon);
        }

        return total;
    }
}
