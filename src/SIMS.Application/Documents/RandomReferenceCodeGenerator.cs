using System.Security.Cryptography;
using SIMS.Application.Abstractions;

namespace SIMS.Application.Documents;

/// <summary>Sinh mã tham chiếu {prefix}-{6 số} bằng RNG mật mã.</summary>
public sealed class RandomReferenceCodeGenerator : IReferenceCodeGenerator
{
    public string Next(string prefix)
        => $"{prefix.ToUpperInvariant()}-{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
}
