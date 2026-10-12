using Microsoft.EntityFrameworkCore;
using SmartLibrary.Application;
using SmartLibrary.Domain;

namespace SmartLibrary.Infrastructure;

public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options), ILibraryDbContext
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookCopy> Copies => Set<BookCopy>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Fine> Fines => Set<Fine>();
    public DbSet<AppNotification> Notifications => Set<AppNotification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> Settings => Set<SystemSetting>();
    public DbSet<CatalogEntry> CatalogEntries => Set<CatalogEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        model.Entity<AppUser>().Property(x => x.Email).HasMaxLength(320);
        model.Entity<Book>().HasIndex(x => x.Isbn);
        model.Entity<Book>().Property(x => x.Title).HasMaxLength(300);
        model.Entity<BookCopy>().HasIndex(x => x.Barcode).IsUnique();
        model.Entity<BookCopy>().HasOne(x => x.Book).WithMany(x => x.Copies).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Loan>().HasOne(x => x.Copy).WithMany().HasForeignKey(x => x.CopyId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Loan>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Loan>().HasIndex(x => new { x.UserId, x.Status });
        model.Entity<Reservation>().HasIndex(x => new { x.BookId, x.Status, x.CreatedAt });
        model.Entity<Favorite>().HasKey(x => new { x.UserId, x.BookId });
        model.Entity<Fine>().Property(x => x.Amount).HasPrecision(10, 2);
        model.Entity<Fine>().Property(x => x.PaidAmount).HasPrecision(10, 2);
        model.Entity<AuditLog>().HasIndex(x => x.CreatedAt);
        model.Entity<SystemSetting>().HasKey(x => x.Key);
        model.Entity<SystemSetting>().Property(x => x.Key).HasMaxLength(100);
        model.Entity<SystemSetting>().Property(x => x.Value).HasMaxLength(500);
        model.Entity<CatalogEntry>().HasIndex(x => new { x.Kind, x.Name }).IsUnique();
        model.Entity<CatalogEntry>().Property(x => x.Kind).HasMaxLength(30);
        model.Entity<CatalogEntry>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<BookAuthor>().HasKey(x => new { x.BookId, x.AuthorEntryId });
        model.Entity<BookAuthor>().HasOne(x => x.Book).WithMany(x => x.AuthorEntries).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<BookAuthor>().HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorEntryId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Book>().HasOne(x => x.CategoryEntry).WithMany().HasForeignKey(x => x.CategoryEntryId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Book>().HasOne(x => x.PublisherEntry).WithMany().HasForeignKey(x => x.PublisherEntryId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<BookCopy>().HasOne(x => x.ShelfEntry).WithMany().HasForeignKey(x => x.ShelfEntryId).OnDelete(DeleteBehavior.Restrict);
    }
}
