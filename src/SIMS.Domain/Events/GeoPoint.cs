using SIMS.Domain.Common;

namespace SIMS.Domain.Events;

/// <summary>
/// Toạ độ địa lý (WGS-84). Dùng cho địa điểm sự kiện và vị trí check-in.
/// Bán kính kiểm tra geofence lấy theo bản PHP: 5 km.
/// </summary>
public readonly struct GeoPoint : IEquatable<GeoPoint>
{
    public const double CheckinRadiusKm = 5d;

    private const double EarthRadiusKm = 6371d;

    public GeoPoint(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsInfinity(latitude) || latitude is < -90d or > 90d)
        {
            throw new DomainException("Vĩ độ phải nằm trong khoảng -90 đến 90.");
        }

        if (double.IsNaN(longitude) || double.IsInfinity(longitude) || longitude is < -180d or > 180d)
        {
            throw new DomainException("Kinh độ phải nằm trong khoảng -180 đến 180.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    /// <summary>Khoảng cách Haversine (km) giữa hai toạ độ.</summary>
    public double DistanceKmTo(GeoPoint other)
    {
        var dLat = ToRadians(other.Latitude - Latitude);
        var dLon = ToRadians(other.Longitude - Longitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(Latitude)) * Math.Cos(ToRadians(other.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusKm * c;
    }

    public bool IsWithin(GeoPoint other, double radiusKm = CheckinRadiusKm)
        => DistanceKmTo(other) <= radiusKm;

    public bool Equals(GeoPoint other)
        => Latitude.Equals(other.Latitude) && Longitude.Equals(other.Longitude);

    public override bool Equals(object? obj) => obj is GeoPoint other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Latitude, Longitude);

    public override string ToString()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Latitude:0.######},{Longitude:0.######}");

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
