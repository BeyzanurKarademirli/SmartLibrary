using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartLibrary.Domain;
using SmartLibrary.Infrastructure;

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string recipient, string subject, string body, CancellationToken ct);
}

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    private readonly string _host = configuration["Smtp:Host"] ?? "";
    private readonly int _port = int.TryParse(configuration["Smtp:Port"], out var port) ? port : 587;
    private readonly string _username = configuration["Smtp:Username"] ?? "";
    private readonly string _password = (configuration["Smtp:Password"] ?? "").Replace(" ", "");
    private readonly string _fromAddress = configuration["Smtp:FromAddress"] ?? "";
    private readonly string _fromName = configuration["Smtp:FromName"] ?? "SmartLibrary";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_host) && !string.IsNullOrWhiteSpace(_username)
        && !string.IsNullOrWhiteSpace(_password) && !string.IsNullOrWhiteSpace(_fromAddress);

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
    {
        if (!IsConfigured) return;
        using var message = new MailMessage
        {
            From = new MailAddress(_fromAddress, _fromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };
        message.To.Add(new MailAddress(recipient));
        using var client = new SmtpClient(_host, _port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_username, _password),
            Timeout = 20000
        };
        await client.SendMailAsync(message, ct);
    }
}

public sealed class EmailNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IEmailSender sender,
    ILogger<EmailNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (sender.IsConfigured)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
                    await SendReservationNotificationsAsync(db, stoppingToken);
                    await SendDueRemindersAsync(db, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "E-posta bildirim döngüsü başarısız oldu."); }
            }

            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task SendReservationNotificationsAsync(LibraryDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        var pending = await db.Notifications.Where(x => x.EmailSentAt == null && x.CreatedAt >= cutoff)
            .OrderBy(x => x.CreatedAt).Take(50).ToListAsync(ct);
        foreach (var notification in pending)
        {
            var email = await db.Users.Where(x => x.Id == notification.UserId && x.IsActive).Select(x => x.Email).SingleOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(email)) continue;
            try
            {
                await sender.SendAsync(email, notification.Title, notification.Message, ct);
                notification.EmailSentAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "Üye bildirimi e-posta ile gönderilemedi. NotificationId: {NotificationId}", notification.Id); }
        }
    }

    private async Task SendDueRemindersAsync(LibraryDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var upcoming = await db.Loans.Where(x => x.Status == LoanStatus.Active && x.DueReminderSentAt == null
                && x.DueAt > now && x.DueAt <= now.AddDays(2))
            .Include(x => x.User).Include(x => x.Copy).ThenInclude(x => x!.Book)
            .OrderBy(x => x.DueAt).Take(50).ToListAsync(ct);
        foreach (var loan in upcoming)
        {
            if (loan.User is null || !loan.User.IsActive || loan.Copy?.Book is null) continue;
            try
            {
                var remaining = Math.Max(1, (int)Math.Ceiling((loan.DueAt - DateTime.UtcNow).TotalDays));
                await sender.SendAsync(loan.User.Email, "Ödünç süreniz yaklaşıyor",
                    $"Merhaba {loan.User.FullName},\n\n{loan.Copy.Book.Title} kitabını {remaining} gün içinde iade etmeniz gerekiyor. Son iade tarihi: {loan.DueAt.ToLocalTime():dd.MM.yyyy HH:mm}.\n\nSmartLibrary",
                    ct);
                loan.DueReminderSentAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "Ödünç hatırlatma e-postası gönderilemedi. LoanId: {LoanId}", loan.Id); }
        }
    }
}
