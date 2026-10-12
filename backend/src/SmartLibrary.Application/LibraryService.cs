using Microsoft.EntityFrameworkCore;
using SmartLibrary.Domain;

namespace SmartLibrary.Application;

public sealed class LibraryService(ILibraryDbContext db)
{
    public async Task<Loan> BorrowAsync(Guid memberId, Guid copyId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == memberId && x.IsActive, ct)
            ?? throw new InvalidOperationException("Aktif üye bulunamadı.");
        if (user.Role != UserRole.Member) throw new InvalidOperationException("Ödünç işlemi yalnızca üyeler için yapılabilir.");
        var activeCount = await db.Loans.CountAsync(x => x.UserId == memberId && x.Status == LoanStatus.Active, ct);
        var maxLoans = await SettingAsync("MaxActiveLoans", 5, ct);
        if (activeCount >= maxLoans) throw new InvalidOperationException($"Bir üye en fazla {maxLoans} aktif kitap ödünç alabilir.");
        if (await db.Fines.AnyAsync(x => x.UserId == memberId && x.Amount > x.PaidAmount, ct))
            throw new InvalidOperationException("Ödenmemiş ceza varken yeni ödünç verilemez.");

        var copy = await db.Copies.SingleOrDefaultAsync(x => x.Id == copyId, ct)
            ?? throw new InvalidOperationException("Kitap kopyası bulunamadı.");
        if (copy.Status != CopyStatus.Available) throw new InvalidOperationException("Bu kopya şu anda ödünç verilemez.");
        copy.Status = CopyStatus.Borrowed;
        var loanDays = await SettingAsync("LoanPeriodDays", 14, ct);
        var loan = new Loan { UserId = memberId, CopyId = copyId, DueAt = DateTime.UtcNow.AddDays(loanDays) };
        db.Loans.Add(loan);
        db.AuditLogs.Add(new AuditLog { UserId = memberId, Action = "LoanCreated", Entity = "Loan", EntityId = loan.Id.ToString() });
        await db.SaveChangesAsync(ct);
        return loan;
    }

    public async Task<Loan> ReturnAsync(Guid loanId, Guid actorId, CancellationToken ct)
    {
        var loan = await db.Loans.Include(x => x.Copy).SingleOrDefaultAsync(x => x.Id == loanId, ct)
            ?? throw new InvalidOperationException("Ödünç kaydı bulunamadı.");
        if (loan.Status != LoanStatus.Active) throw new InvalidOperationException("Bu ödünç kaydı zaten kapatılmış.");
        var now = DateTime.UtcNow;
        loan.ReturnedAt = now;
        loan.Status = LoanStatus.Returned;
        loan.Copy!.Status = CopyStatus.Available;
        var lateDays = Math.Max(0, (int)Math.Ceiling((now - loan.DueAt).TotalDays));
        var dailyFine = await SettingAsync("FinePerDay", 5, ct);
        if (lateDays > 0) db.Fines.Add(new Fine { LoanId = loan.Id, UserId = loan.UserId, Amount = lateDays * dailyFine, Reason = $"{lateDays} gün gecikme" });
        await PromoteReservationAsync(loan.Copy.BookId, ct);
        db.AuditLogs.Add(new AuditLog { UserId = actorId, Action = "LoanReturned", Entity = "Loan", EntityId = loan.Id.ToString() });
        await db.SaveChangesAsync(ct);
        return loan;
    }

    public async Task<Loan> RenewAsync(Guid loanId, Guid memberId, CancellationToken ct, bool staffOverride = false)
    {
        var loan = await db.Loans.SingleOrDefaultAsync(x => x.Id == loanId && (staffOverride || x.UserId == memberId), ct)
            ?? throw new InvalidOperationException("Üyeye ait ödünç bulunamadı.");
        if (loan.Status != LoanStatus.Active || loan.DueAt < DateTime.UtcNow) throw new InvalidOperationException("Süresi geçen veya kapanmış kayıt uzatılamaz.");
        if (loan.Renewals >= 1) throw new InvalidOperationException("Bu ödünç için uzatma hakkı kullanılmış.");
        var bookId = await db.Copies.Where(c => c.Id == loan.CopyId).Select(c => c.BookId).SingleAsync(ct);
        var hasQueue = await db.Reservations.AnyAsync(x => x.BookId == bookId && (x.Status == ReservationStatus.Waiting || x.Status == ReservationStatus.ReadyForPickup), ct);
        if (hasQueue) throw new InvalidOperationException("Kitap için bekleyen rezervasyon olduğundan süre uzatılamaz.");
        loan.DueAt = loan.DueAt.AddDays(await SettingAsync("LoanPeriodDays", 14, ct));
        loan.Renewals++;
        await db.SaveChangesAsync(ct);
        return loan;
    }

    public async Task<Reservation> ReserveAsync(Guid memberId, Guid bookId, CancellationToken ct)
    {
        if (!await db.Books.AnyAsync(x => x.Id == bookId, ct)) throw new InvalidOperationException("Kitap bulunamadı.");
        if (await db.Reservations.AnyAsync(x => x.UserId == memberId && x.BookId == bookId && (x.Status == ReservationStatus.Waiting || x.Status == ReservationStatus.ReadyForPickup), ct))
            throw new InvalidOperationException("Bu kitap için zaten aktif rezervasyonunuz var.");
        var reservation = new Reservation { UserId = memberId, BookId = bookId };
        db.Reservations.Add(reservation);
        // The queue query runs against the database, so persist the new waiting row first.
        await db.SaveChangesAsync(ct);
        await PromoteReservationAsync(bookId, ct);
        await db.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task RefreshReservationQueuesAsync(CancellationToken ct)
    {
        var bookIds = await db.Reservations.Where(x => x.Status == ReservationStatus.Waiting || (x.Status == ReservationStatus.ReadyForPickup && x.PickupExpiresAt < DateTime.UtcNow))
            .Select(x => x.BookId).Distinct().ToListAsync(ct);
        foreach (var bookId in bookIds)
        {
            while (await db.Reservations.AnyAsync(x => x.BookId == bookId && x.Status == ReservationStatus.Waiting, ct)
                && await db.Copies.AnyAsync(x => x.BookId == bookId && x.Status == CopyStatus.Available, ct))
            {
                await PromoteReservationAsync(bookId, ct);
                await db.SaveChangesAsync(ct);
            }
        }
    }

    public async Task CancelReservationAsync(Guid reservationId, Guid memberId, CancellationToken ct)
    {
        var reservation = await db.Reservations.SingleOrDefaultAsync(x => x.Id == reservationId && x.UserId == memberId, ct)
            ?? throw new InvalidOperationException("Üyeye ait rezervasyon bulunamadı.");
        if (reservation.Status is not (ReservationStatus.Waiting or ReservationStatus.ReadyForPickup))
            throw new InvalidOperationException("Bu rezervasyon artık iptal edilemez.");
        if (reservation.Status == ReservationStatus.ReadyForPickup)
        {
            var copy = await db.Copies.FirstOrDefaultAsync(x => x.BookId == reservation.BookId && x.Status == CopyStatus.Reserved, ct);
            if (copy is not null) copy.Status = CopyStatus.Available;
        }
        reservation.Status = ReservationStatus.Cancelled;
        await PromoteReservationAsync(reservation.BookId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<BookCopy> SetCopyStatusAsync(Guid copyId, CopyStatus status, Guid actorId, CancellationToken ct)
    {
        if (status is not (CopyStatus.Available or CopyStatus.Lost or CopyStatus.Damaged or CopyStatus.Maintenance))
            throw new InvalidOperationException("Kopya durumu yalnızca Uygun, Kayıp, Hasarlı veya Bakımda olarak değiştirilebilir.");
        var copy = await db.Copies.SingleOrDefaultAsync(x => x.Id == copyId, ct)
            ?? throw new InvalidOperationException("Kitap kopyası bulunamadı.");
        if (copy.Status is CopyStatus.Borrowed or CopyStatus.Reserved)
            throw new InvalidOperationException("Ödünçteki veya rezervasyona ayrılmış kopyanın durumu buradan değiştirilemez.");
        if (await db.Loans.AnyAsync(x => x.CopyId == copyId && x.Status == LoanStatus.Active, ct))
            throw new InvalidOperationException("Aktif ödünç bulunan kopyanın durumu değiştirilemez.");
        copy.Status = status;
        db.AuditLogs.Add(new AuditLog { UserId = actorId, Action = $"CopyStatusChanged:{status}", Entity = "BookCopy", EntityId = copyId.ToString() });
        if (status == CopyStatus.Available) await PromoteReservationAsync(copy.BookId, ct);
        await db.SaveChangesAsync(ct);
        return copy;
    }

    public async Task<Loan> CheckoutReservationAsync(Guid reservationId, Guid actorId, CancellationToken ct)
    {
        var reservation = await db.Reservations.SingleOrDefaultAsync(x => x.Id == reservationId, ct)
            ?? throw new InvalidOperationException("Rezervasyon bulunamadı.");
        if (reservation.Status != ReservationStatus.ReadyForPickup || reservation.PickupExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("Rezervasyon hazır değil veya teslim alma süresi dolmuş.");
        var member = await db.Users.SingleOrDefaultAsync(x => x.Id == reservation.UserId && x.IsActive && x.Role == UserRole.Member, ct)
            ?? throw new InvalidOperationException("Aktif üye bulunamadı.");
        if (await db.Loans.CountAsync(x => x.UserId == member.Id && x.Status == LoanStatus.Active, ct) >= await SettingAsync("MaxActiveLoans", 5, ct))
            throw new InvalidOperationException("Üyenin aktif ödünç sınırı dolu.");
        if (await db.Fines.AnyAsync(x => x.UserId == member.Id && x.Amount > x.PaidAmount, ct))
            throw new InvalidOperationException("Üyenin ödenmemiş cezası bulunuyor.");
        var copy = await db.Copies.FirstOrDefaultAsync(x => x.BookId == reservation.BookId && x.Status == CopyStatus.Reserved, ct)
            ?? throw new InvalidOperationException("Rezervasyon için ayrılmış kopya bulunamadı.");
        copy.Status = CopyStatus.Borrowed;
        reservation.Status = ReservationStatus.Fulfilled;
        var loan = new Loan { UserId = member.Id, CopyId = copy.Id, DueAt = DateTime.UtcNow.AddDays(await SettingAsync("LoanPeriodDays", 14, ct)) };
        db.Loans.Add(loan);
        db.AuditLogs.Add(new AuditLog { UserId = actorId, Action = "ReservationCheckedOut", Entity = "Loan", EntityId = loan.Id.ToString() });
        await db.SaveChangesAsync(ct);
        return loan;
    }

    private async Task PromoteReservationAsync(Guid bookId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await db.Reservations.Where(x => x.BookId == bookId && x.Status == ReservationStatus.ReadyForPickup && x.PickupExpiresAt < now).ToListAsync(ct);
        foreach (var reservation in expired)
        {
            reservation.Status = ReservationStatus.Expired;
            var heldCopy = await db.Copies.FirstOrDefaultAsync(x => x.BookId == bookId && x.Status == CopyStatus.Reserved, ct);
            if (heldCopy is not null) heldCopy.Status = CopyStatus.Available;
        }
        var next = await db.Reservations.Where(x => x.BookId == bookId && x.Status == ReservationStatus.Waiting).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        var copy = await db.Copies.Where(x => x.BookId == bookId && x.Status == CopyStatus.Available).OrderBy(x => x.Barcode).FirstOrDefaultAsync(ct);
        if (next is null || copy is null) return;
        next.Status = ReservationStatus.ReadyForPickup;
        next.PickupExpiresAt = DateTime.UtcNow.AddDays(await SettingAsync("ReservationPickupDays", 2, ct));
        copy.Status = CopyStatus.Reserved;
        var pickupDays = await SettingAsync("ReservationPickupDays", 2, ct);
        db.Notifications.Add(new AppNotification { UserId = next.UserId, Title = "Rezervasyon hazır", Message = $"Ayırttığınız kitap teslim alınmaya hazır. {pickupDays} gün içinde kütüphaneden alabilirsiniz." });
    }

    private async Task<int> SettingAsync(string key, int fallback, CancellationToken ct)
    {
        var value = await db.Settings.Where(x => x.Key == key).Select(x => x.Value).SingleOrDefaultAsync(ct);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
