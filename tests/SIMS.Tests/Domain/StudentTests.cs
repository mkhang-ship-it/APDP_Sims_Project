using SIMS.Domain.Common;
using SIMS.Domain.Students;

namespace SIMS.Tests.Domain;

public sealed class StudentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Dob = new(2004, 5, 20);

    [Fact]
    public void Create_normalizes_code_and_email()
    {
        var student = Student.Create("  sv001  ", "  Nguyễn Văn A  ", "  A@Example.COM ", Dob, "  Công nghệ thông tin  ", Now);

        Assert.Equal("SV001", student.StudentCode);
        Assert.Equal("Nguyễn Văn A", student.FullName);
        Assert.Equal("a@example.com", student.Email);
        Assert.Equal("Công nghệ thông tin", student.Program);
        Assert.Equal(StudentStatus.Active, student.Status);
        Assert.NotEqual(Guid.Empty, student.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("SV 001")]
    [InlineData("SV-001")]
    [InlineData("SV00112345678901234567890")]
    public void Create_rejects_invalid_student_code(string code)
    {
        Assert.Throws<DomainException>(() => Student.Create(code, "Nguyễn Văn A", "a@example.com", Dob, "CNTT", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("khong-co-at")]
    [InlineData("a@b")]
    [InlineData("a b@example.com")]
    public void Create_rejects_invalid_email(string email)
    {
        Assert.Throws<DomainException>(() => Student.Create("SV001", "Nguyễn Văn A", email, Dob, "CNTT", Now));
    }

    [Fact]
    public void Create_rejects_future_date_of_birth()
    {
        var tomorrow = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);

        Assert.Throws<DomainException>(() => Student.Create("SV001", "Nguyễn Văn A", "a@example.com", tomorrow, "CNTT", Now));
    }

    [Fact]
    public void Create_rejects_default_date_of_birth()
    {
        Assert.Throws<DomainException>(() => Student.Create("SV001", "Nguyễn Văn A", "a@example.com", default, "CNTT", Now));
    }

    [Fact]
    public void Update_profile_changes_fields_but_keeps_identity()
    {
        var student = Student.Create("SV001", "Nguyễn Văn A", "a@example.com", Dob, "CNTT", Now);
        var id = student.Id;

        student.UpdateProfile("Trần Thị B", "b@example.com", new DateOnly(2005, 1, 2), "Kế toán", Now.AddDays(1));

        Assert.Equal(id, student.Id);
        Assert.Equal("SV001", student.StudentCode);
        Assert.Equal("Trần Thị B", student.FullName);
        Assert.Equal("Kế toán", student.Program);
    }

    [Fact]
    public void Deactivate_sets_inactive_status()
    {
        var student = Student.Create("SV001", "Nguyễn Văn A", "a@example.com", Dob, "CNTT", Now);

        student.Deactivate();

        Assert.Equal(StudentStatus.Inactive, student.Status);
    }

    [Fact]
    public void Restore_keeps_id_and_status()
    {
        var id = Guid.NewGuid();

        var student = Student.Restore(id, "sv002", "Trần Thị B", "b@example.com", Dob, "CNTT", StudentStatus.Inactive, Now);

        Assert.Equal(id, student.Id);
        Assert.Equal("SV002", student.StudentCode);
        Assert.Equal(StudentStatus.Inactive, student.Status);
    }
}
