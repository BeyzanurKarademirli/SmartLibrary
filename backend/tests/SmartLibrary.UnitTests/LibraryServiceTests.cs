using Microsoft.EntityFrameworkCore;
using SmartLibrary.Application;
using SmartLibrary.Domain;
using SmartLibrary.Infrastructure;

namespace SmartLibrary.UnitTests;

public sealed class LibraryServiceTests
{
    [Fact]
    public async Task Borrow_creates_fourteen_day_loan_and_marks_physical_copy_borrowed()
    {
        await using var db = CreateDatabase();
        var (member, copy) = await SeedMemberAndCopy(db);
        var service = new LibraryService(db);

        var loan = await service.BorrowAsync(member.Id, copy.Id, CancellationToken.None);

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Equal(14, (loan.DueAt - loan.BorrowedAt).Days);
        Assert.Equal(CopyStatus.Borrowed, copy.Status);
        Assert.Single(db.Loans);
    }

    [Fact]
    public async Task Borrow_rejects_a_copy_that_is_not_available()
    {
        await using var db = CreateDatabase();
        var (member, copy) = await SeedMemberAndCopy(db, CopyStatus.Maintenance);
        var service = new LibraryService(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BorrowAsync(member.Id, copy.Id, CancellationToken.None));

        Assert.Contains("ödünç verilemez", error.Message);
        Assert.Empty(db.Loans);
    }

    [Fact]
    public async Task Borrow_blocks_a_member_with_an_unpaid_fine()
    {
        await using var db = CreateDatabase();
        var (member, copy) = await SeedMemberAndCopy(db);
        db.Fines.Add(new Fine { UserId = member.Id, LoanId = Guid.NewGuid(), Amount = 5m, PaidAmount = 0m, Reason = "Gecikme" });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new LibraryService(db).BorrowAsync(member.Id, copy.Id, CancellationToken.None));

        Assert.Equal(CopyStatus.Available, copy.Status);
        Assert.Empty(db.Loans);
    }

    [Fact]
    public async Task Renew_allows_one_extension_only()
    {
        await using var db = CreateDatabase();
        var (member, copy) = await SeedMemberAndCopy(db);
        var loan = new Loan { UserId = member.Id, CopyId = copy.Id, DueAt = DateTime.UtcNow.AddDays(10) };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        var service = new LibraryService(db);

        var renewed = await service.RenewAsync(loan.Id, member.Id, CancellationToken.None);
        Assert.Equal(1, renewed.Renewals);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenewAsync(loan.Id, member.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Return_records_late_fee_and_releases_the_copy()
    {
        await using var db = CreateDatabase();
        var (member, copy) = await SeedMemberAndCopy(db, CopyStatus.Borrowed);
        var loan = new Loan { UserId = member.Id, CopyId = copy.Id, DueAt = DateTime.UtcNow.AddDays(-1.1) };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        await new LibraryService(db).ReturnAsync(loan.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(LoanStatus.Returned, loan.Status);
        Assert.Equal(CopyStatus.Available, copy.Status);
        Assert.Equal(10m, (await db.Fines.SingleAsync()).Amount);
    }

    private static LibraryDbContext CreateDatabase() => new(new DbContextOptionsBuilder<LibraryDbContext>()
        .UseInMemoryDatabase($"SmartLibrary-{Guid.NewGuid()}").Options);

    private static async Task<(AppUser Member, BookCopy Copy)> SeedMemberAndCopy(LibraryDbContext db, CopyStatus status = CopyStatus.Available)
    {
        var member = new AppUser { FullName = "Test Member", Email = $"{Guid.NewGuid():N}@example.test", PasswordHash = "not-used", Role = UserRole.Member };
        var book = new Book { Title = "Test Book", Isbn = Guid.NewGuid().ToString("N"), Authors = "Test Author" };
        var copy = new BookCopy { Book = book, Barcode = Guid.NewGuid().ToString("N"), Status = status };
        db.Users.Add(member);
        db.Books.Add(book);
        db.Copies.Add(copy);
        await db.SaveChangesAsync();
        return (member, copy);
    }
}
