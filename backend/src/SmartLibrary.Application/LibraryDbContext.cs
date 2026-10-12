using Microsoft.EntityFrameworkCore;
using SmartLibrary.Domain;

namespace SmartLibrary.Application;

public interface ILibraryDbContext
{
    DbSet<AppUser> Users { get; }
    DbSet<Book> Books { get; }
    DbSet<BookCopy> Copies { get; }
    DbSet<Loan> Loans { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<Favorite> Favorites { get; }
    DbSet<Fine> Fines { get; }
    DbSet<AppNotification> Notifications { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SystemSetting> Settings { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
