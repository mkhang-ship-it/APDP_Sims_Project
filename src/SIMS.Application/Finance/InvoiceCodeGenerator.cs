using System.Security.Cryptography;
using SIMS.Application.Abstractions;

namespace SIMS.Application.Finance;

/// <summary>Sinh mã INV-{năm}-{6 số} bằng RNG mật mã (không đoán được thứ tự).</summary>
public sealed class RandomInvoiceCodeGenerator : IInvoiceCodeGenerator
{
    public string Next(int year)
        => $"INV-{year:D4}-{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
}
