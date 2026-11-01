using SIMS.Application.Abstractions;

namespace SIMS.Infrastructure.Time;

/// <summary>Đồng hồ hệ thống, luôn trả UTC.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
