using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SmartLibrary.Application;
using SmartLibrary.Domain;
using SmartLibrary.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Library") ?? "Host=localhost;Port=5432;Database=smartlibrary;Username=smartlibrary;Password=smartlibrary";
builder.Services.AddDbContext<LibraryDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<SmtpEmailSender>());
builder.Services.AddHostedService<EmailNotificationWorker>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ILibraryDbContext>(sp => sp.GetRequiredService<LibraryDbContext>());
builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Description = "Bearer {token}" });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>() });
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration["FrontendOrigin"] ?? "http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o => o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }))
    .AddPolicy("assistant", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true }))
    .AddPolicy("email-test", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(2), QueueLimit = 0, AutoReplenishment = true })));
var jwtKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtKey) && builder.Environment.IsDevelopment()) jwtKey = "SmartLibrary-Development-Key-Replace-For-Production-2026";
if (string.IsNullOrWhiteSpace(jwtKey) && builder.Environment.IsEnvironment("Testing")) jwtKey = "Integration-Test-Signing-Key-At-Least-32-Bytes";
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Jwt:SigningKey en az 32 byte olmalı; üretimde secret olarak tanımlayın.");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = "SmartLibrary", ValidateAudience = true, ValidAudience = "SmartLibrary.Web", ValidateIssuerSigningKey = true, IssuerSigningKey = key, ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), RoleClaimType = ClaimTypes.Role });
builder.Services.AddAuthorization(o => { o.AddPolicy("Staff", p => p.RequireRole("Admin", "Librarian")); o.AddPolicy("Admin", p => p.RequireRole("Admin")); o.AddPolicy("Member", p => p.RequireRole("Member")); });

var app = builder.Build();
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (InvalidOperationException ex) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (DbUpdateException) { ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = "Kayıt veritabanı kısıtlarıyla çakışıyor." }); }
    catch (Exception ex) { app.Logger.LogError(ex, "Unhandled API error. TraceId: {TraceId}", ctx.TraceIdentifier); ctx.Response.StatusCode = 500; await ctx.Response.WriteAsJsonAsync(new { error = "Beklenmeyen bir hata oluştu.", traceId = ctx.TraceIdentifier }); }
});
app.UseCors(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
await InitializeAsync(app);
await using (var queueScope = app.Services.CreateAsyncScope())
    await queueScope.ServiceProvider.GetRequiredService<LibraryService>().RefreshReservationQueuesAsync(CancellationToken.None);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/admin/email/test", async (ClaimsPrincipal principal, IEmailSender sender, ILogger<Program> logger, CancellationToken ct) =>
{
    var recipient = principal.FindFirstValue(ClaimTypes.Email);
    if (string.IsNullOrWhiteSpace(recipient)) return Results.BadRequest(new { error = "Hesabında e-posta adresi yok." });
    if (!sender.IsConfigured) return Results.Problem("SMTP yapılandırması eksik.", statusCode: StatusCodes.Status503ServiceUnavailable);
    try
    {
        await sender.SendAsync(recipient, "SmartLibrary e-posta testi", "E-posta gönderimi başarıyla yapılandırıldı.", ct);
        return Results.Ok(new { message = $"Test e-postası {recipient} adresine gönderildi." });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogError(ex, "SMTP test e-postası gönderilemedi.");
        return Results.Problem("E-posta gönderilemedi. SMTP ayarlarını ve uygulama şifresini kontrol edin.", statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireAuthorization("Admin").RequireRateLimiting("email-test");

app.MapPost("/api/assistant/chat", async (AssistantChatInput input, ClaimsPrincipal principal, LibraryDbContext db, IHttpClientFactory clients, IConfiguration config, ILogger<Program> logger, CancellationToken ct) =>
{
    var message = input.Message?.Trim() ?? "";
    if (message.Length is < 2 or > 1000) return Results.BadRequest(new { error = "Mesaj 2 ile 1000 karakter arasında olmalı." });
    var apiKey = config["Gemini:ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey)) return Results.Problem("AI servisi henüz yapılandırılmadı.", statusCode: StatusCodes.Status503ServiceUnavailable);

    var language = input.Language == "en" ? "English" : "Turkish";
    var query = message.Length > 80 ? message[..80] : message;
    var catalog = await db.Books.AsNoTracking()
        .Where(x => EF.Functions.ILike(x.Title, $"%{query}%") || EF.Functions.ILike(x.Authors, $"%{query}%") || EF.Functions.ILike(x.Category, $"%{query}%"))
        .OrderBy(x => x.Title).Take(8)
        .Select(x => new { x.Title, x.Authors, x.Category, available = x.Copies.Count(c => c.Status == CopyStatus.Available), total = x.Copies.Count })
        .ToListAsync(ct);
    var bookContext = catalog.Count == 0 ? "No exact catalog matches." : string.Join("\n", catalog.Select(x => $"{x.Title} — {x.Authors}; {x.Category}; available copies {x.available}/{x.total}"));
    var contents = new List<object>();
    foreach (var turn in (input.History ?? []).TakeLast(8))
    {
        if (turn.Role is "user" or "model" && !string.IsNullOrWhiteSpace(turn.Text))
            contents.Add(new { role = turn.Role, parts = new[] { new { text = turn.Text[..Math.Min(700, turn.Text.Length)] } } });
    }
    contents.Add(new { role = "user", parts = new[] { new { text = $"Member question: {message}\n\nRelevant catalog data (only state facts supported here):\n{bookContext}" } } });
    var payload = new
    {
        systemInstruction = new { parts = new[] { new { text = $"You are SmartLibrary's library assistant. Reply in {language}. Be concise and friendly. Answer library process questions. For book availability, use only the provided catalog data; if no match is shown, ask the member to search the catalog. Never claim to change loans or reservations; guide the member to the app." } } },
        contents,
        generationConfig = new { maxOutputTokens = 500, temperature = 0.4 }
    };
    using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(config["Gemini:Model"] ?? "gemini-3.8-flash")}:generateContent");
    request.Headers.Add("x-goog-api-key", apiKey);
    request.Content = System.Net.Http.Json.JsonContent.Create(payload);
    try
    {
        using var response = await clients.CreateClient("gemini").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gemini chat request failed with status {StatusCode} for user {UserId}.", (int)response.StatusCode, principal.FindFirstValue(ClaimTypes.NameIdentifier));
            return Results.Problem(response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? "AI ücretsiz kullanım sınırına ulaştı. Biraz sonra yeniden dene." : "AI yanıt veremedi. Biraz sonra yeniden dene.", statusCode: response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? 429 : 502);
        }
        using var json = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var reply = json.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        return Results.Ok(new { reply });
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        logger.LogError(ex, "Gemini chat request failed.");
        return Results.Problem("AI servisine ulaşılamadı. Biraz sonra yeniden dene.", statusCode: 502);
    }
}).RequireAuthorization().RequireRateLimiting("assistant");

var auth = app.MapGroup("/api/auth");
auth.MapPost("/register", async (RegisterRequest input, LibraryDbContext db, IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Length > 120 || !input.Email.Contains('@') || input.Password.Length < 10) return Results.BadRequest(new { error = "Ad, geçerli e-posta ve en az 10 karakterli parola girin." });
    var email = input.Email.Trim().ToLowerInvariant(); if (await db.Users.AnyAsync(x => x.Email == email, ct)) return Results.Conflict(new { error = "Bu e-posta zaten kayıtlı." });
    var user = new AppUser { FullName = input.FullName.Trim(), Email = email }; user.PasswordHash = hasher.HashPassword(user, input.Password); db.Users.Add(user); await db.SaveChangesAsync(ct);
    return Results.Ok(new AuthResponse(Token(user, key), UserDto.From(user)));
}).AllowAnonymous().RequireRateLimiting("auth");
auth.MapPost("/login", async (LoginRequest input, LibraryDbContext db, IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
{
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == input.Email.Trim().ToLowerInvariant() && x.IsActive, ct);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    return Results.Ok(new AuthResponse(Token(user, key), UserDto.From(user)));
}).AllowAnonymous().RequireRateLimiting("auth");
auth.MapGet("/me", async (ClaimsPrincipal principal, LibraryDbContext db, CancellationToken ct) =>
{
    var userId = Id(principal);
    var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
    return account is null ? Results.NotFound() : Results.Ok(UserDto.From(account));
}).RequireAuthorization();

var books = app.MapGroup("/api/books");
books.MapGet("/", async (string? q, string? category, int? page, int? pageSize, LibraryDbContext db, CancellationToken ct) =>
{
    var query = db.Books.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToLower(); query = query.Where(b => b.Title.ToLower().Contains(term) || b.Authors.ToLower().Contains(term) || b.Isbn.Contains(term)); }
    if (!string.IsNullOrWhiteSpace(category)) query = query.Where(b => b.Category == category);
    var size = Math.Clamp(pageSize ?? 12, 1, 50); var current = Math.Max(page ?? 1, 1); var total = await query.CountAsync(ct);
    var items = await query.OrderBy(b => b.Title).Skip((current - 1) * size).Take(size).Select(b => new BookDto(b.Id,b.Isbn,b.Title,b.Description,b.CoverUrl,b.Category,b.Publisher,b.PublishedYear,b.PageCount,b.Authors,b.Copies.Count(c => c.Status == CopyStatus.Available),b.Copies.Count)).ToListAsync(ct);
    return Results.Ok(new { items, total, page = current, pageSize = size });
}).AllowAnonymous();
books.MapGet("/{id:guid}", async (Guid id, LibraryDbContext db, CancellationToken ct) =>
{
    var book = await db.Books.AsNoTracking().Include(x => x.Copies).SingleOrDefaultAsync(x => x.Id == id, ct);
    return book is null ? Results.NotFound() : Results.Ok(new { book = BookDto.From(book), copies = book.Copies.Select(c => new { c.Id, c.Barcode, c.Shelf, status = c.Status.ToString() }) });
}).AllowAnonymous();
books.MapPost("/", async (BookInput input, ClaimsPrincipal principal, LibraryDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Title)) return Results.BadRequest(new { error = "Kitap adı zorunludur." });
    if (input.PageCount is < 1 or > 10000) return Results.BadRequest(new { error = "Sayfa sayısı 1 ile 10000 arasında olmalıdır." });
    var book = input.ToEntity(); db.Books.Add(book); await SyncBookCatalogAsync(db, book, ct); db.AuditLogs.Add(new AuditLog { UserId = Id(principal), Action = "BookCreated", Entity = "Book", EntityId = book.Id.ToString() }); await db.SaveChangesAsync(ct);
    return Results.Created($"/api/books/{book.Id}", BookDto.From(book));
}).RequireAuthorization("Staff");
books.MapPut("/{id:guid}", async (Guid id, BookInput input, LibraryDbContext db, CancellationToken ct) =>
{
    if (input.PageCount is < 1 or > 10000) return Results.BadRequest(new { error = "Sayfa sayısı 1 ile 10000 arasında olmalıdır." });
    var book = await db.Books.Include(x => x.AuthorEntries).SingleOrDefaultAsync(x => x.Id == id, ct); if (book is null) return Results.NotFound(); input.Apply(book); await SyncBookCatalogAsync(db, book, ct); await db.SaveChangesAsync(ct); return Results.Ok(BookDto.From(book));
}).RequireAuthorization("Staff");
books.MapPost("/{id:guid}/copies", async (Guid id, CopyInput input, LibraryDbContext db, CancellationToken ct) =>
{
    if (!await db.Books.AnyAsync(b => b.Id == id, ct)) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(input.Barcode) || await db.Copies.AnyAsync(c => c.Barcode == input.Barcode.Trim(), ct)) return Results.Conflict(new { error = "Barkod boş olamaz ve başka bir kopyada kullanılamaz." });
    var copy = new BookCopy { BookId = id, Barcode = input.Barcode.Trim(), Shelf = input.Shelf.Trim() }; db.Copies.Add(copy); copy.ShelfEntryId = (await EnsureCatalogEntryAsync(db, "shelves", copy.Shelf, ct))?.Id; await db.SaveChangesAsync(ct); return Results.Created($"/api/copies/{copy.Id}", copy);
}).RequireAuthorization("Staff");
books.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal p, LibraryDbContext db, CancellationToken ct) =>
{
    var book = await db.Books.Include(x => x.Copies).SingleOrDefaultAsync(x => x.Id == id, ct); if (book is null) return Results.NotFound();
    var copyIds = book.Copies.Select(x => x.Id).ToArray();
    if (await db.Loans.AnyAsync(x => copyIds.Contains(x.CopyId), ct) || await db.Reservations.AnyAsync(x => x.BookId == id, ct)) return Results.Conflict(new { error = "Ödünç veya rezervasyon geçmişi olan kitap silinemez. Kayıt geçmişini korumak için silme engellendi." });
    db.Favorites.RemoveRange(db.Favorites.Where(x => x.BookId == id)); db.Copies.RemoveRange(book.Copies); db.Books.Remove(book); db.AuditLogs.Add(new AuditLog { UserId = Id(p), Action = "BookDeleted", Entity = "Book", EntityId = id.ToString() }); await db.SaveChangesAsync(ct); return Results.NoContent();
}).RequireAuthorization("Admin");
app.MapDelete("/api/staff/copies/{id:guid}", async (Guid id, ClaimsPrincipal p, LibraryDbContext db, CancellationToken ct) =>
{
    var copy = await db.Copies.SingleOrDefaultAsync(x => x.Id == id, ct); if (copy is null) return Results.NotFound();
    if (copy.Status != CopyStatus.Available || await db.Loans.AnyAsync(x => x.CopyId == id, ct)) return Results.Conflict(new { error = "Yalnızca hiç ödünç geçmişi olmayan uygun kopyalar silinebilir." });
    db.Copies.Remove(copy); db.AuditLogs.Add(new AuditLog { UserId = Id(p), Action = "CopyDeleted", Entity = "BookCopy", EntityId = id.ToString() }); await db.SaveChangesAsync(ct); return Results.NoContent();
}).RequireAuthorization("Admin");
app.MapPut("/api/staff/copies/{id:guid}/status", async (Guid id, CopyStatusInput input, ClaimsPrincipal p, LibraryService service, CancellationToken ct) =>
{
    if (!Enum.TryParse<CopyStatus>(input.Status, true, out var status)) return Results.BadRequest(new { error = "Geçersiz kopya durumu." });
    var copy = await service.SetCopyStatusAsync(id, status, Id(p), ct); return Results.Ok(new { copy.Id, status = copy.Status.ToString() });
}).RequireAuthorization("Staff");

var member = app.MapGroup("/api/member").RequireAuthorization("Member");
member.MapGet("/loans", async (ClaimsPrincipal principal, LibraryDbContext db, CancellationToken ct) => { var userId = Id(principal); return Results.Ok(await db.Loans.AsNoTracking().Where(x => x.UserId == userId).Include(x => x.Copy).ThenInclude(x => x!.Book).OrderByDescending(x => x.BorrowedAt).Select(x => new { x.Id, x.BorrowedAt, x.DueAt, x.ReturnedAt, x.Renewals, status = x.Status.ToString(), book = x.Copy!.Book!.Title, barcode = x.Copy.Barcode }).ToListAsync(ct)); });
member.MapGet("/reservations", async (ClaimsPrincipal p, LibraryDbContext db, CancellationToken ct) => { var userId = Id(p); return Results.Ok(await db.Reservations.Where(x => x.UserId == userId).Join(db.Books, r => r.BookId, b => b.Id, (r,b) => new { r.Id, r.BookId, book = b.Title, status = r.Status.ToString(), r.CreatedAt, r.PickupExpiresAt }).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)); });
member.MapPost("/loans/{id:guid}/renew", async (Guid id, ClaimsPrincipal p, LibraryService service, CancellationToken ct) => Results.Ok(await service.RenewAsync(id, Id(p), ct)));
member.MapPost("/reservations/{bookId:guid}", async (Guid bookId, ClaimsPrincipal p, LibraryService service, CancellationToken ct) => Results.Ok(await service.ReserveAsync(Id(p), bookId, ct)));
member.MapDelete("/reservations/{id:guid}", async (Guid id, ClaimsPrincipal p, LibraryService service, CancellationToken ct) => { await service.CancelReservationAsync(id, Id(p), ct); return Results.NoContent(); });
member.MapPost("/favorites/{bookId:guid}", async (Guid bookId, ClaimsPrincipal p, LibraryDbContext db, CancellationToken ct) =>
{
    var userId = Id(p); if (!await db.Books.AnyAsync(x => x.Id == bookId, ct)) return Results.NotFound();
    if (!await db.Favorites.AnyAsync(x => x.UserId == userId && x.BookId == bookId, ct)) { db.Favorites.Add(new Favorite { UserId = userId, BookId = bookId }); await db.SaveChangesAsync(ct); } return Results.NoContent();
});
member.MapDelete("/favorites/{bookId:guid}", async (Guid bookId, ClaimsPrincipal p, LibraryDbContext db, CancellationToken ct) =>
{
    var row = await db.Favorites.FindAsync([Id(p), bookId], ct); if (row is null) return Results.NotFound(); db.Favorites.Remove(row); await db.SaveChangesAsync(ct); return Results.NoContent();
});
member.MapGet("/favorites", async (ClaimsPrincipal p, LibraryDbContext db, CancellationToken ct) => { var userId = Id(p); return Results.Ok(await db.Favorites.Where(x => x.UserId == userId).Join(db.Books, f => f.BookId, b => b.Id, (f,b) => new { f.BookId, f.CreatedAt, b.Title, b.Authors, b.CoverUrl }).ToListAsync(ct)); });
member.MapGet("/fines", async (ClaimsPrincipal p, LibraryDbContext db, CancellationToken ct) => { var userId = Id(p); return Results.Ok(await db.Fines.Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.Amount, x.PaidAmount, balance = x.Amount-x.PaidAmount, x.Reason, x.CreatedAt }).ToListAsync(ct)); });
member.MapGet("/notifications", async (ClaimsPrincipal p, LibraryDbContext db, CancellationToken ct) => { var userId = Id(p); return Results.Ok(await db.Notifications.Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct)); });
member.MapPost("/notifications/{id:guid}/read", async (Guid id, ClaimsPrincipal p, LibraryDbContext db, CancellationToken ct) => { var userId = Id(p); var row = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct); if (row is null) return Results.NotFound(); row.IsRead = true; await db.SaveChangesAsync(ct); return Results.NoContent(); });

var staff = app.MapGroup("/api/staff").RequireAuthorization("Staff");
var catalogKinds = new HashSet<string>(["authors", "categories", "publishers", "shelves"], StringComparer.OrdinalIgnoreCase);
staff.MapGet("/books", async (LibraryDbContext db, CancellationToken ct) => Results.Ok(await db.Books.AsNoTracking().OrderBy(x => x.Title).Select(b => new { book = new BookDto(b.Id,b.Isbn,b.Title,b.Description,b.CoverUrl,b.Category,b.Publisher,b.PublishedYear,b.PageCount,b.Authors,b.Copies.Count(c => c.Status == CopyStatus.Available),b.Copies.Count), copies = b.Copies.OrderBy(c => c.Barcode).Select(c => new { c.Id,c.Barcode,c.Shelf,status = c.Status.ToString() }).ToList() }).ToListAsync(ct)));
staff.MapGet("/copies/by-barcode", async (string barcode, LibraryDbContext db, CancellationToken ct) => { var copy = await db.Copies.AsNoTracking().Where(x => x.Barcode == barcode.Trim()).Select(x => new { x.Id, x.Barcode, status = x.Status.ToString() }).SingleOrDefaultAsync(ct); return copy is null ? Results.NotFound(new { error = "Barkod bulunamadı." }) : Results.Ok(copy); });
staff.MapGet("/reservations", async (LibraryDbContext db, CancellationToken ct) =>
{
    var rows = await db.Reservations.AsNoTracking().Where(r => r.Status == ReservationStatus.Waiting || r.Status == ReservationStatus.ReadyForPickup)
        .Join(db.Books, r => r.BookId, b => b.Id, (r,b) => new { Reservation = r, Book = b })
        .Join(db.Users, x => x.Reservation.UserId, u => u.Id, (x,u) => new { x.Reservation.Id, x.Reservation.UserId, x.Reservation.BookId, x.Book.Title, member = u.FullName, email = u.Email, x.Reservation.Status, x.Reservation.CreatedAt, x.Reservation.PickupExpiresAt })
        .OrderBy(x => x.CreatedAt).ToListAsync(ct);
    return Results.Ok(rows.Select(x => new { x.Id, x.UserId, x.BookId, book = x.Title, x.member, x.email, status = x.Status.ToString(), x.CreatedAt, x.PickupExpiresAt }));
});
staff.MapPost("/loans", async (LoanInput input, LibraryService service, CancellationToken ct) => Results.Ok(await service.BorrowAsync(input.MemberId, input.CopyId, ct)));
staff.MapPost("/loans/{id:guid}/return", async (Guid id, ClaimsPrincipal p, LibraryService service, CancellationToken ct) => Results.Ok(await service.ReturnAsync(id, Id(p), ct)));
staff.MapPost("/loans/{id:guid}/renew", async (Guid id, ClaimsPrincipal p, LibraryService service, CancellationToken ct) => Results.Ok(await service.RenewAsync(id, Id(p), ct, staffOverride: true)));
staff.MapPost("/reservations/{id:guid}/checkout", async (Guid id, ClaimsPrincipal p, LibraryService service, CancellationToken ct) => Results.Ok(await service.CheckoutReservationAsync(id, Id(p), ct)));
staff.MapGet("/loans", async (LibraryDbContext db, CancellationToken ct) => Results.Ok(await db.Loans.AsNoTracking().Include(x => x.User).Include(x => x.Copy).ThenInclude(x => x!.Book).OrderByDescending(x => x.BorrowedAt).Take(200).Select(x => new { x.Id, member = x.User!.FullName, email = x.User.Email, book = x.Copy!.Book!.Title, barcode = x.Copy.Barcode, x.BorrowedAt, x.DueAt, x.ReturnedAt, status = x.Status.ToString(), x.Renewals }).ToListAsync(ct)));
staff.MapGet("/members", async (string? q, LibraryDbContext db, CancellationToken ct) => Results.Ok(await db.Users.Where(x => x.Role == UserRole.Member && (string.IsNullOrWhiteSpace(q) || x.FullName.ToLower().Contains(q.ToLower()) || x.Email.ToLower().Contains(q.ToLower()))).OrderBy(x => x.FullName).Select(x => new { x.Id, x.FullName, x.Email, x.IsActive, activeLoans = db.Loans.Count(l => l.UserId == x.Id && l.Status == LoanStatus.Active) }).Take(100).ToListAsync(ct)));
staff.MapGet("/dashboard", async (LibraryDbContext db, CancellationToken ct) => Results.Ok(new { titles = await db.Books.CountAsync(ct), copies = await db.Copies.CountAsync(ct), available = await db.Copies.CountAsync(x => x.Status == CopyStatus.Available, ct), activeLoans = await db.Loans.CountAsync(x => x.Status == LoanStatus.Active, ct), overdue = await db.Loans.CountAsync(x => x.Status == LoanStatus.Active && x.DueAt < DateTime.UtcNow, ct), members = await db.Users.CountAsync(x => x.Role == UserRole.Member && x.IsActive, ct), reservations = await db.Reservations.CountAsync(x => x.Status == ReservationStatus.Waiting, ct), outstandingFines = await db.Fines.SumAsync(x => (decimal?)(x.Amount-x.PaidAmount), ct) ?? 0 }));
staff.MapGet("/reports", async (DateTime? from, DateTime? to, LibraryDbContext db, CancellationToken ct) =>
{
    var start = DateTime.SpecifyKind(from?.Date ?? DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);
    var end = DateTime.SpecifyKind((to?.Date ?? DateTime.UtcNow.Date).AddDays(1), DateTimeKind.Utc);
    if (end <= start || (end - start).TotalDays > 366) return Results.BadRequest(new { error = "Rapor aralığı 1 ile 366 gün arasında olmalıdır." });
    var loans = db.Loans.AsNoTracking().Where(x => x.BorrowedAt >= start && x.BorrowedAt < end);
    var popular = await loans.GroupBy(x => x.Copy!.Book!.Title).Select(g => new { title = g.Key, count = g.Count() }).OrderByDescending(x => x.count).ThenBy(x => x.title).Take(10).ToListAsync(ct);
    var byDay = await loans.GroupBy(x => x.BorrowedAt.Date).Select(g => new { date = g.Key, count = g.Count() }).OrderBy(x => x.date).ToListAsync(ct);
    var overdue = await db.Loans.AsNoTracking().Where(x => x.Status == LoanStatus.Active && x.DueAt < DateTime.UtcNow).Include(x => x.User).Include(x => x.Copy).ThenInclude(x => x!.Book).OrderBy(x => x.DueAt).Take(100).Select(x => new { x.Id, member = x.User!.FullName, email = x.User.Email, book = x.Copy!.Book!.Title, x.DueAt, overdueDays = (int)Math.Ceiling((DateTime.UtcNow - x.DueAt).TotalDays) }).ToListAsync(ct);
    return Results.Ok(new { from = start, to = end.AddDays(-1), loans = await loans.CountAsync(ct), returns = await db.Loans.CountAsync(x => x.ReturnedAt >= start && x.ReturnedAt < end, ct), newMembers = await db.Users.CountAsync(x => x.Role == UserRole.Member && x.CreatedAt >= start && x.CreatedAt < end, ct), collectedFines = await db.Fines.Where(x => x.CreatedAt >= start && x.CreatedAt < end).SumAsync(x => (decimal?)x.PaidAmount, ct) ?? 0m, outstandingFines = await db.Fines.SumAsync(x => (decimal?)(x.Amount - x.PaidAmount), ct) ?? 0m, popularBooks = popular, loansByDay = byDay, overdueLoans = overdue });
}).RequireAuthorization("Staff");
staff.MapGet("/settings", async (LibraryDbContext db, CancellationToken ct) => Results.Ok(await db.Settings.AsNoTracking().OrderBy(x => x.Key).ToListAsync(ct))).RequireAuthorization("Admin");
staff.MapPut("/settings", async (SettingsInput input, ClaimsPrincipal principal, LibraryDbContext db, CancellationToken ct) =>
{
    var limits = new Dictionary<string, (int Min, int Max)> { ["LoanPeriodDays"] = (1, 90), ["MaxActiveLoans"] = (1, 30), ["FinePerDay"] = (0, 1000), ["ReservationPickupDays"] = (1, 14) };
    if (input.Values is null || input.Values.Count != limits.Count || input.Values.Keys.Any(k => !limits.ContainsKey(k))) return Results.BadRequest(new { error = "Tüm izin verilen sistem ayarlarını gönderin." });
    foreach (var (name, value) in input.Values) if (value < limits[name].Min || value > limits[name].Max) return Results.BadRequest(new { error = $"{name} değeri {limits[name].Min} ile {limits[name].Max} arasında olmalıdır." });
    foreach (var (name, value) in input.Values) { var row = await db.Settings.FindAsync([name], ct); if (row is null) db.Settings.Add(new SystemSetting { Key = name, Value = value.ToString(), UpdatedAt = DateTime.UtcNow }); else { row.Value = value.ToString(); row.UpdatedAt = DateTime.UtcNow; } }
    db.AuditLogs.Add(new AuditLog { UserId = Id(principal), Action = "SystemSettingsUpdated", Entity = "SystemSetting", EntityId = "library-rules" });
    await db.SaveChangesAsync(ct); return Results.NoContent();
}).RequireAuthorization("Admin");
staff.MapGet("/catalog/{kind}", async (string kind, LibraryDbContext db, CancellationToken ct) =>
{
    if (!catalogKinds.Contains(kind)) return Results.NotFound();
    return Results.Ok(await db.CatalogEntries.AsNoTracking().Where(x => x.Kind == kind).OrderBy(x => x.Name).ToListAsync(ct));
});
staff.MapPost("/catalog/{kind}", async (string kind, CatalogEntryInput input, ClaimsPrincipal principal, LibraryDbContext db, CancellationToken ct) =>
{
    if (!catalogKinds.Contains(kind)) return Results.NotFound();
    var name = input.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 200) return Results.BadRequest(new { error = "Ad boş olamaz ve 200 karakteri aşamaz." });
    if (await db.CatalogEntries.AnyAsync(x => x.Kind == kind && x.Name.ToLower() == name.ToLower(), ct)) return Results.Conflict(new { error = "Bu değer zaten kayıtlı." });
    var row = new CatalogEntry { Kind = kind, Name = name };
    db.CatalogEntries.Add(row); db.AuditLogs.Add(new AuditLog { UserId = Id(principal), Action = "CatalogEntryCreated", Entity = kind, EntityId = row.Id.ToString() });
    await db.SaveChangesAsync(ct); return Results.Created($"/api/staff/catalog/{kind}/{row.Id}", row);
});
staff.MapDelete("/catalog/{kind}/{id:guid}", async (string kind, Guid id, ClaimsPrincipal principal, LibraryDbContext db, CancellationToken ct) =>
{
    if (!catalogKinds.Contains(kind)) return Results.NotFound();
    var row = await db.CatalogEntries.SingleOrDefaultAsync(x => x.Id == id && x.Kind == kind, ct); if (row is null) return Results.NotFound();
    var used = kind switch { "authors" => await db.Books.AnyAsync(x => x.Authors.ToLower().Contains(row.Name.ToLower()), ct), "categories" => await db.Books.AnyAsync(x => x.Category == row.Name, ct), "publishers" => await db.Books.AnyAsync(x => x.Publisher == row.Name, ct), "shelves" => await db.Copies.AnyAsync(x => x.Shelf == row.Name, ct), _ => false };
    if (used) return Results.Conflict(new { error = "Bu değer katalog kayıtlarında kullanılıyor; önce kitap/kopya kayıtlarını güncelleyin." });
    db.CatalogEntries.Remove(row); db.AuditLogs.Add(new AuditLog { UserId = Id(principal), Action = "CatalogEntryDeleted", Entity = kind, EntityId = id.ToString() }); await db.SaveChangesAsync(ct); return Results.NoContent();
});
staff.MapGet("/fines", async (LibraryDbContext db, CancellationToken ct) => Results.Ok(await db.Fines.Join(db.Users, f => f.UserId, u => u.Id, (f,u) => new { f.Id, member = u.FullName, email = u.Email, f.Amount, f.PaidAmount, balance = f.Amount-f.PaidAmount, f.Reason, f.CreatedAt }).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)));
staff.MapPost("/fines/{id:guid}/payments", async (Guid id, PaymentInput input, LibraryDbContext db, CancellationToken ct) =>
{
    var fine = await db.Fines.FindAsync([id], ct); if (fine is null) return Results.NotFound();
    if (input.Amount <= 0 || input.Amount > fine.Amount-fine.PaidAmount) return Results.BadRequest(new { error = "Tutar kalan borçtan büyük veya sıfır olamaz." });
    fine.PaidAmount += input.Amount; await db.SaveChangesAsync(ct); return Results.Ok(new { fine.Amount, fine.PaidAmount, balance = fine.Amount-fine.PaidAmount });
});
staff.MapGet("/audit", async (LibraryDbContext db, CancellationToken ct) => Results.Ok(await db.AuditLogs.OrderByDescending(x => x.CreatedAt).Take(250).ToListAsync(ct))).RequireAuthorization("Admin");
staff.MapPut("/members/{id:guid}/active", async (Guid id, ActiveInput input, LibraryDbContext db, CancellationToken ct) =>
{
    var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.Role == UserRole.Member, ct); if (user is null) return Results.NotFound(); user.IsActive = input.IsActive; await db.SaveChangesAsync(ct); return Results.Ok(UserDto.From(user));
}).RequireAuthorization("Admin");
app.MapPost("/api/admin/staff", async (StaffAccountInput input, LibraryDbContext db, IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.FullName) || !input.Email.Contains('@') || input.Password.Length < 12 || input.Role is not (UserRole.Admin or UserRole.Librarian)) return Results.BadRequest(new { error = "Personel adı, e-posta, en az 12 karakter parola ve geçerli personel rolü gereklidir." });
    var email = input.Email.Trim().ToLowerInvariant(); if (await db.Users.AnyAsync(x => x.Email == email, ct)) return Results.Conflict(new { error = "Bu e-posta zaten kayıtlı." });
    var user = new AppUser { FullName = input.FullName.Trim(), Email = email, Role = input.Role }; user.PasswordHash = hasher.HashPassword(user, input.Password); db.Users.Add(user); db.AuditLogs.Add(new AuditLog { Action = "StaffCreated", Entity = "User", EntityId = user.Id.ToString() }); await db.SaveChangesAsync(ct); return Results.Created($"/api/staff/members/{user.Id}", UserDto.From(user));
}).RequireAuthorization("Admin");

app.Run();

static Guid Id(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
static async Task<CatalogEntry?> EnsureCatalogEntryAsync(LibraryDbContext db, string kind, string? name, CancellationToken ct)
{
    var normalized = name?.Trim();
    if (string.IsNullOrEmpty(normalized)) return null;
    var local = db.CatalogEntries.Local.FirstOrDefault(x => x.Kind == kind && string.Equals(x.Name, normalized, StringComparison.OrdinalIgnoreCase));
    if (local is not null) return local;
    // Catalog values are a small lookup set. Compare names in .NET so behavior is
    // consistent for Turkish casing regardless of PostgreSQL's locale rules.
    var entries = await db.CatalogEntries.Where(x => x.Kind == kind).ToListAsync(ct);
    var existing = entries.FirstOrDefault(x => string.Equals(x.Name, normalized, StringComparison.OrdinalIgnoreCase));
    if (existing is not null) return existing;
    var created = new CatalogEntry { Kind = kind, Name = normalized }; db.CatalogEntries.Add(created); return created;
}
static async Task SyncBookCatalogAsync(LibraryDbContext db, Book book, CancellationToken ct)
{
    book.CategoryEntryId = (await EnsureCatalogEntryAsync(db, "categories", book.Category, ct))?.Id;
    book.PublisherEntryId = (await EnsureCatalogEntryAsync(db, "publishers", book.Publisher, ct))?.Id;
    foreach (var name in book.Authors.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var author = await EnsureCatalogEntryAsync(db, "authors", name, ct);
        if (author is not null && !book.AuthorEntries.Any(x => x.AuthorEntryId == author.Id)) book.AuthorEntries.Add(new BookAuthor { BookId = book.Id, AuthorEntryId = author.Id, Author = author });
    }
    var activeIds = book.Authors.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(x => x.ToLower()).ToHashSet();
    foreach (var old in book.AuthorEntries.Where(x => x.Author is not null && !activeIds.Contains(x.Author.Name.ToLower())).ToList()) book.AuthorEntries.Remove(old);
}
static string Token(AppUser user, SymmetricSecurityKey key)
{
    var jwt = new JwtSecurityToken("SmartLibrary", "SmartLibrary.Web", [new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Name,user.FullName),new Claim(ClaimTypes.Email,user.Email),new Claim(ClaimTypes.Role,user.Role.ToString())], expires: DateTime.UtcNow.AddHours(2), signingCredentials: new SigningCredentials(key,SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(jwt);
}
static async Task InitializeAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    if (db.Database.IsRelational()) await db.Database.MigrateAsync(); else await db.Database.EnsureCreatedAsync();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>(); var password = app.Configuration["Seed:AdminPassword"];
    var defaults = new Dictionary<string, string> { ["LoanPeriodDays"] = "14", ["MaxActiveLoans"] = "5", ["FinePerDay"] = "5", ["ReservationPickupDays"] = "2" };
    var knownPages = new Dictionary<string, int> { ["9780451524935"] = 328, ["9780743273565"] = 180, ["9780141439518"] = 279, ["9780547928227"] = 300, ["9780061120084"] = 336, ["9780316769488"] = 277, ["9780451526342"] = 140, ["9780061122415"] = 208, ["9780375842207"] = 584, ["9780140449136"] = 560 };
    foreach (var setting in defaults) if (!await db.Settings.AnyAsync(x => x.Key == setting.Key)) db.Settings.Add(new SystemSetting { Key = setting.Key, Value = setting.Value });
    if (!string.IsNullOrWhiteSpace(password) && !await db.Users.AnyAsync(x => x.Role == UserRole.Admin)) { var admin = new AppUser { FullName="Sistem Yöneticisi",Email=app.Configuration["Seed:AdminEmail"] ?? "admin@smartlibrary.local",Role=UserRole.Admin }; admin.PasswordHash=hasher.HashPassword(admin,password); db.Users.Add(admin); }
    if (!await db.Books.AnyAsync())
    {
        var sample = new[] { new Book {Isbn="9789753638029",Title="Saatleri Ayarlama Enstitüsü",Authors="Ahmet Hamdi Tanpınar",Category="Edebiyat",PublishedYear=1961,Publisher="Dergâh Yayınları",Description="Zaman, modernleşme ve toplum üzerine zamansız bir roman."}, new Book {Isbn="9789750726439",Title="Kürk Mantolu Madonna",Authors="Sabahattin Ali",Category="Edebiyat",PublishedYear=1943,Publisher="Yapı Kredi Yayınları",Description="Raif Efendi'nin iç dünyasına açılan dokunaklı bir hikâye."}, new Book {Isbn="9789750718533",Title="Tutunamayanlar",Authors="Oğuz Atay",Category="Edebiyat",PublishedYear=1972,Publisher="İletişim Yayınları",Description="Modern Türk edebiyatının dönüm noktalarından bir roman."}, new Book {Isbn="9786053325619",Title="Sapiens",Authors="Yuval Noah Harari",Category="Tarih",PublishedYear=2011,Publisher="Kolektif Kitap",Description="İnsanlık tarihine geniş bir bakış."}, new Book {Isbn="9789754589024",Title="Küçük Prens",Authors="Antoine de Saint-Exupéry",Category="Çocuk",PublishedYear=1943,Publisher="Can Çocuk",Description="Dostluk, merak ve büyümek üzerine klasik bir anlatı."}, new Book {Isbn="9786053754794",Title="Dönüşüm",Authors="Franz Kafka",Category="Klasikler",PublishedYear=1915,Publisher="İş Bankası Kültür Yayınları",Description="Yabancılaşma ve insan olma üzerine kısa roman."} };
        foreach(var b in sample) { b.Copies.Add(new BookCopy { Book=b,Barcode=$"SL-{b.Isbn[^5..]}-01",Shelf="A-01" }); b.Copies.Add(new BookCopy { Book=b,Barcode=$"SL-{b.Isbn[^5..]}-02",Shelf="A-01" }); } db.Books.AddRange(sample);
    }
    var additionalBooks = new[] {
        new Book { Isbn="9780451524935", Title="1984", Authors="George Orwell", Category="Bilim Kurgu", Publisher="Signet Classics", PublishedYear=1949, Description="Gözetim, propaganda ve özgürlük üzerine distopik bir klasik.", CoverUrl="https://covers.openlibrary.org/b/isbn/9780451524935-L.jpg" },
        new Book { Isbn="9780743273565", Title="Muhteşem Gatsby", Authors="F. Scott Fitzgerald", Category="Klasikler", Publisher="Scribner", PublishedYear=1925, Description="Caz Çağı'nda zenginlik, özlem ve Amerikan rüyası.", CoverUrl="https://covers.openlibrary.org/b/isbn/9780743273565-L.jpg" },
        new Book { Isbn="9780141439518", Title="Gurur ve Önyargı", Authors="Jane Austen", Category="Romantik", Publisher="Penguin Classics", PublishedYear=1813, Description="Elizabeth Bennet ve Bay Darcy'nin toplumsal beklentiler arasında gelişen hikâyesi.", CoverUrl="https://covers.openlibrary.org/b/isbn/9780141439518-L.jpg" },
        new Book { Isbn="9780547928227", Title="Hobbit", Authors="J. R. R. Tolkien", Category="Fantastik", Publisher="Mariner Books", PublishedYear=1937, Description="Bilbo Baggins'in beklenmedik macerası.", CoverUrl="https://covers.openlibrary.org/b/isbn/9780547928227-L.jpg" },
        new Book { Isbn="9780061120084", Title="Bülbülü Öldürmek", Authors="Harper Lee", Category="Roman", Publisher="Harper Perennial", PublishedYear=1960, Description="Adalet, vicdan ve çocukluk üzerine güçlü bir anlatı.", CoverUrl="https://covers.openlibrary.org/b/isbn/9780061120084-L.jpg" },
        new Book { Isbn="9780316769488", Title="Çavdar Tarlasında Çocuklar", Authors="J. D. Salinger", Category="Roman", Publisher="Little, Brown and Company", PublishedYear=1951, Description="Holden Caulfield'ın gençlik ve aidiyet arayışı.", CoverUrl="https://covers.openlibrary.org/b/isbn/9780316769488-L.jpg" },
        new Book { Isbn="9780451526342", Title="Hayvan Çiftliği", Authors="George Orwell", Category="Klasikler", Publisher="Signet Classics", PublishedYear=1945, Description="İktidar ve eşitlik üzerine alegorik bir novella.", CoverUrl="https://covers.openlibrary.org/b/isbn/9780451526342-L.jpg" },
        new Book { Isbn="9780061122415", Title="Simyacı", Authors="Paulo Coelho", Category="Macera", Publisher="HarperOne", PublishedYear=1988, Description="Kişisel menkıbesini arayan Santiago'nun yolculuğu.", CoverUrl="https://covers.openlibrary.org/b/isbn/9780061122415-L.jpg" },
        new Book { Isbn="9780375842207", Title="Kitap Hırsızı", Authors="Markus Zusak", Category="Tarihî Roman", Publisher="Knopf Books for Young Readers", PublishedYear=2005, Description="Savaş yıllarında kitaplarla ve kelimelerle kurulan bağ.", CoverUrl="https://covers.openlibrary.org/b/isbn/9780375842207-L.jpg" },
        new Book { Isbn="9780140449136", Title="Odysseia", Authors="Homeros", Category="Klasikler", Publisher="Penguin Classics", PublishedYear=-700, Description="Odysseus'un eve dönüş yolundaki destansı serüveni.", CoverUrl="https://covers.openlibrary.org/b/isbn/9780140449136-L.jpg" }
    };
    foreach (var book in additionalBooks)
    {
        if (await db.Books.AnyAsync(x => x.Isbn == book.Isbn)) continue;
        book.PageCount = knownPages.GetValueOrDefault(book.Isbn);
        var suffix = book.Isbn[^5..];
        book.Copies.Add(new BookCopy { Barcode = $"SL-{suffix}-01", Shelf = "A-02" });
        book.Copies.Add(new BookCopy { Barcode = $"SL-{suffix}-02", Shelf = "A-02" });
        db.Books.Add(book);
    }
    var booksWithoutPages = await db.Books.Where(x => x.PageCount == null).ToListAsync();
    foreach (var book in booksWithoutPages) if (knownPages.TryGetValue(book.Isbn, out var pages)) book.PageCount = pages;
    await db.SaveChangesAsync(CancellationToken.None);
    var catalogValues = new List<ValuePair>();
    catalogValues.AddRange(await db.Books.Select(x => new ValuePair("authors", x.Authors)).ToListAsync());
    catalogValues.AddRange(await db.Books.Select(x => new ValuePair("categories", x.Category)).ToListAsync());
    catalogValues.AddRange(await db.Books.Select(x => new ValuePair("publishers", x.Publisher)).ToListAsync());
    catalogValues.AddRange(await db.Copies.Select(x => new ValuePair("shelves", x.Shelf)).ToListAsync());
    foreach (var value in catalogValues.Where(x => !string.IsNullOrWhiteSpace(x.Name)).DistinctBy(x => new { x.Kind, Name = x.Name.Trim().ToLowerInvariant() }))
        await EnsureCatalogEntryAsync(db, value.Kind, value.Name, CancellationToken.None);
    await NormalizeCatalogReferencesAsync(db, CancellationToken.None);
    await db.SaveChangesAsync();
}
static async Task NormalizeCatalogReferencesAsync(LibraryDbContext db, CancellationToken ct)
{
    var books = await db.Books.Include(x => x.AuthorEntries).ToListAsync(ct);
    foreach (var book in books) await SyncBookCatalogAsync(db, book, ct);
    var copies = await db.Copies.ToListAsync(ct);
    foreach (var copy in copies) copy.ShelfEntryId = (await EnsureCatalogEntryAsync(db, "shelves", copy.Shelf, ct))?.Id;
}

record RegisterRequest(string FullName,string Email,string Password);
record LoginRequest(string Email,string Password);
record LoanInput(Guid MemberId,Guid CopyId);
record CopyInput(string Barcode,string Shelf);
record CopyStatusInput(string Status);
record PaymentInput(decimal Amount);
record ActiveInput(bool IsActive);
record SettingsInput(Dictionary<string, int> Values);
record CatalogEntryInput(string? Name);
record ValuePair(string Kind, string Name);
record AssistantChatInput(string? Message, string? Language, List<AssistantChatTurn>? History);
record AssistantChatTurn(string Role, string Text);
record StaffAccountInput(string FullName,string Email,string Password,UserRole Role);
record AuthResponse(string Token,UserDto User);
record UserDto(Guid Id,string FullName,string Email,string Role,bool IsActive,DateTime CreatedAt) { public static UserDto From(AppUser u)=>new(u.Id,u.FullName,u.Email,u.Role.ToString(),u.IsActive,u.CreatedAt); }
record BookInput(string Title,string? Isbn,string? Authors,string? Category,string? Publisher,int PublishedYear,int? PageCount,string? Description,string? CoverUrl)
{
    public Book ToEntity() { var b=new Book(); Apply(b); return b; }
    public void Apply(Book b) { b.Title=Title.Trim();b.Isbn=Isbn?.Trim()??"";b.Authors=Authors?.Trim()??"";b.Category=Category?.Trim()??"Genel";b.Publisher=Publisher?.Trim()??"";b.PublishedYear=PublishedYear;b.PageCount=PageCount;b.Description=Description?.Trim()??"";b.CoverUrl=CoverUrl?.Trim()??""; }
}
record BookDto(Guid Id,string Isbn,string Title,string Description,string CoverUrl,string Category,string Publisher,int PublishedYear,int? PageCount,string Authors,int AvailableCopies,int TotalCopies)
{
    public static BookDto From(Book b)=>new(b.Id,b.Isbn,b.Title,b.Description,b.CoverUrl,b.Category,b.Publisher,b.PublishedYear,b.PageCount,b.Authors,b.Copies.Count(x=>x.Status==CopyStatus.Available),b.Copies.Count);
}

public partial class Program { }
