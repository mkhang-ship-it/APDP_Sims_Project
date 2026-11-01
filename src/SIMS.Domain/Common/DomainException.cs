namespace SIMS.Domain.Common;

/// <summary>
/// Lỗi vi phạm quy tắc nghiệp vụ. Tầng Application/Web sẽ map sang HTTP 400.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
