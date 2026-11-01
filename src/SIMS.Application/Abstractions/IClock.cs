namespace SIMS.Application.Abstractions;

/// <summary>Đồng hồ hệ thống — tách ra để test được thời gian.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
