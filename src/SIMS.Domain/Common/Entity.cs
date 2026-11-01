namespace SIMS.Domain.Common;

/// <summary>
/// Base type cho mọi entity nghiệp vụ. Domain không phụ thuộc hạ tầng
/// (không System.IO, không System.Net.Http) — xem SIMS.Domain.csproj.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}
