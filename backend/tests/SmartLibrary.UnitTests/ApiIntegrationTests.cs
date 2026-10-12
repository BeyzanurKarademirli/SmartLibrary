using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLibrary.Application;
using SmartLibrary.Infrastructure;

namespace SmartLibrary.UnitTests;

public sealed class ApiIntegrationTests : IClassFixture<SmartLibraryApiFactory>
{
    private readonly HttpClient client;

    public ApiIntegrationTests(SmartLibraryApiFactory factory) => client = factory.CreateClient();

    [Fact]
    public async Task Member_can_register_and_member_routes_reject_staff_only_access()
    {
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Integration Member",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "StrongPassword!2026"
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        using var response = System.Text.Json.JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        var token = response.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/member/loans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/staff/dashboard")).StatusCode);
    }
}

public sealed class SmartLibraryApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "Integration-Test-Signing-Key-At-Least-32-Bytes",
            ["FrontendOrigin"] = "http://localhost:5173"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LibraryDbContext>();
            services.RemoveAll<DbContextOptions<LibraryDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<LibraryDbContext>>();
            services.AddDbContext<LibraryDbContext>(options => options.UseInMemoryDatabase("SmartLibrary-Api-Integration"));
        });
    }
}
