namespace SmartLibrary.Domain;

public enum UserRole { Admin, Librarian, Member }
public enum CopyStatus { Available, Borrowed, Reserved, Lost, Damaged, Maintenance }
public enum LoanStatus { Active, Returned, Lost }
public enum ReservationStatus { Waiting, ReadyForPickup, Fulfilled, Cancelled, Expired }

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Member;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Book
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Isbn { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string Category { get; set; } = "";
    public string Publisher { get; set; } = "";
    public int PublishedYear { get; set; }
    public int? PageCount { get; set; }
    public string Authors { get; set; } = "";
    public Guid? CategoryEntryId { get; set; }
    public CatalogEntry? CategoryEntry { get; set; }
    public Guid? PublisherEntryId { get; set; }
    public CatalogEntry? PublisherEntry { get; set; }
    public List<BookAuthor> AuthorEntries { get; set; } = [];
    public List<BookCopy> Copies { get; set; } = [];
}

public sealed class BookCopy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookId { get; set; }
    public Book? Book { get; set; }
    public string Barcode { get; set; } = "";
    public string Shelf { get; set; } = "";
    public Guid? ShelfEntryId { get; set; }
    public CatalogEntry? ShelfEntry { get; set; }
    public CopyStatus Status { get; set; } = CopyStatus.Available;
}

public sealed class Loan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public Guid CopyId { get; set; }
    public BookCopy? Copy { get; set; }
    public DateTime BorrowedAt { get; set; } = DateTime.UtcNow;
    public DateTime DueAt { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public DateTime? DueReminderSentAt { get; set; }
    public int Renewals { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Active;
}

public sealed class Reservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Waiting;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PickupExpiresAt { get; set; }
}

public sealed class Favorite { public Guid UserId { get; set; } public Guid BookId { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public sealed class Fine { public Guid Id { get; set; } = Guid.NewGuid(); public Guid LoanId { get; set; } public Guid UserId { get; set; } public decimal Amount { get; set; } public decimal PaidAmount { get; set; } public string Reason { get; set; } = ""; public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public sealed class AppNotification { public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public string Title { get; set; } = ""; public string Message { get; set; } = ""; public bool IsRead { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; public DateTime? EmailSentAt { get; set; } }
public sealed class AuditLog { public long Id { get; set; } public Guid? UserId { get; set; } public string Action { get; set; } = ""; public string Entity { get; set; } = ""; public string EntityId { get; set; } = ""; public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public sealed class SystemSetting { public string Key { get; set; } = ""; public string Value { get; set; } = ""; public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; }
public sealed class CatalogEntry { public Guid Id { get; set; } = Guid.NewGuid(); public string Kind { get; set; } = ""; public string Name { get; set; } = ""; public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public sealed class BookAuthor { public Guid BookId { get; set; } public Book? Book { get; set; } public Guid AuthorEntryId { get; set; } public CatalogEntry? Author { get; set; } }
