using SIMS.Infrastructure.Security;

namespace SIMS.Tests.Infrastructure;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_uses_pbkdf2_sha256_with_100k_iterations_and_16_byte_salt()
    {
        var encoded = _hasher.Hash("Admin@12345");
        var parts = encoded.Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("100000", parts[1]);

        var salt = Convert.FromBase64String(parts[2]);
        var key = Convert.FromBase64String(parts[3]);
        Assert.Equal(16, salt.Length);
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void Verify_accepts_correct_password_and_rejects_wrong_one()
    {
        var encoded = _hasher.Hash("Admin@12345");

        Assert.True(_hasher.Verify("Admin@12345", encoded));
        Assert.False(_hasher.Verify("admin@12345", encoded));
        Assert.False(_hasher.Verify("", encoded));
    }

    [Fact]
    public void Hash_uses_random_salt_so_two_hashes_differ()
    {
        var first = _hasher.Hash("Admin@12345");
        var second = _hasher.Hash("Admin@12345");

        Assert.NotEqual(first, second);
        Assert.True(_hasher.Verify("Admin@12345", first));
        Assert.True(_hasher.Verify("Admin@12345", second));
    }

    [Fact]
    public void Verify_rejects_tampered_or_weak_hashes()
    {
        var encoded = _hasher.Hash("Admin@12345");
        var parts = encoded.Split('$');

        var tampered = string.Join('$', parts[0], parts[1], parts[2], Convert.ToBase64String(new byte[32]));
        var weakIterations = string.Join('$', parts[0], "1000", parts[2], parts[3]);

        Assert.False(_hasher.Verify("Admin@12345", tampered));
        Assert.False(_hasher.Verify("Admin@12345", weakIterations));
        Assert.False(_hasher.Verify("Admin@12345", "not-a-hash"));
        Assert.False(_hasher.Verify("Admin@12345", "pbkdf2-sha256$100000$%%%$%%%"));
    }
}
