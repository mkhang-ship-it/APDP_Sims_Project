using SIMS.Domain.Students;
using SIMS.Infrastructure.Persistence;

namespace SIMS.Tests.Infrastructure;

public sealed class CsvStudentLinkingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sims-student-link-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task User_and_club_columns_round_trip_and_find_by_user()
    {
        var repository = new CsvStudentRepository(Path.Combine(_directory, "students.csv"));
        var userId = Guid.NewGuid();
        var clubId = Guid.NewGuid();
        var student = Student.Create(
            "SV001", "Nguyễn Văn A", "a@example.com", new DateOnly(2004, 1, 1), "CNTT", Now,
            userId: userId, clubId: clubId);

        await repository.AddAsync(student);

        var loaded = await repository.FindByIdAsync(student.Id);
        var byUser = await repository.FindByUserIdAsync(userId);

        Assert.Equal(userId, loaded!.UserId);
        Assert.Equal(clubId, loaded.ClubId);
        Assert.NotNull(byUser);
        Assert.Equal("SV001", byUser!.StudentCode);
    }
}
