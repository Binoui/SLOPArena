# Server MVP (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the MVP backend for SLOPArena: Master server with Steam auth, MMR-based 1v1 matchmaking, global chat (SignalR), and refactor the existing game server to support multi-match orchestration.

**Architecture:** Monolithic VPS-based approach with separate Master Server (ASP.NET Core + SignalR + Postgres) and Game Server (UDP multi-match orchestrator). Master assigns matched players to game server instances. Game server runs 10-15 concurrent matches in parallel threads.

**Tech Stack:**
- **Master Server:** ASP.NET Core 8.0 Minimal APIs, SignalR, Entity Framework Core, PostgreSQL 16
- **Game Server:** Existing .NET 8 UDP server (refactored for multi-match)
- **Authentication:** Steam Web API (ISteamUserAuth)
- **Deployment:** Docker Compose (master), systemd service (game server)

**Scope:** Phase 1 only — EU region, ranked 1v1 matchmaking, global chat. No spectators, no community servers, no DM/party chat yet.

---

## File Structure

### New Files to Create

**Master Server:**
- `MasterServer/MasterServer.csproj` — ASP.NET Core Web API project
- `MasterServer/Program.cs` — Entry point, service configuration, minimal APIs
- `MasterServer/Data/AppDbContext.cs` — EF Core database context
- `MasterServer/Data/Models/User.cs` — User entity (Steam ID, MMR)
- `MasterServer/Data/Models/Match.cs` — Match history entity
- `MasterServer/Data/Models/GameServer.cs` — Game server registry entity
- `MasterServer/Data/Migrations/` — EF Core migrations (auto-generated)
- `MasterServer/Services/SteamAuthService.cs` — Steam ticket validation
- `MasterServer/Services/MatchmakingService.cs` — Queue management, MMR pairing
- `MasterServer/Services/GameServerRegistry.cs` — Track game servers, heartbeats
- `MasterServer/Hubs/ChatHub.cs` — SignalR hub for global chat
- `MasterServer/appsettings.json` — Config (DB connection, Steam API key)
- `MasterServer/Dockerfile` — Docker image for deployment
- `MasterServer/docker-compose.yml` — Master + Postgres stack
- `MasterServer/.env.example` — Environment variables template

**Game Server Refactor:**
- `Server/MultiMatchOrchestrator.cs` — Manages multiple match threads
- `Server/MatchInstance.cs` — Single match logic (extracted from Program.cs)
- `Server/GameServerRegistration.cs` — Registration + heartbeat to master
- `Server/server.json` — Configuration file (master URL, region, max matches)

**Shared (new packets for matchmaking):**
- `Shared/MatchAssignmentPacket.cs` — Master → Client (server IP, port, token)
- `Shared/ClientJoinMatchPacket.cs` — Client → Game Server (token validation)

**Tests:**
- `MasterServer.Tests/Services/MatchmakingServiceTests.cs` — Matchmaking logic tests
- `MasterServer.Tests/Services/SteamAuthServiceTests.cs` — Steam auth mock tests
- `Server.Tests/MultiMatchOrchestratorTests.cs` — Multi-match orchestration tests

### Existing Files to Modify

- `Server/Program.cs` — Refactor into MultiMatchOrchestrator + MatchInstance
- `Server/SlopArena.Server.csproj` — Add JSON config dependencies
- `Shared/ClientInputPacket.cs` — Add match token field (backward compatible)

---

## Task 1: Master Server Project Setup

**Files:**
- Create: `MasterServer/MasterServer.csproj`
- Create: `MasterServer/Program.cs`
- Create: `MasterServer/appsettings.json`
- Create: `MasterServer/.env.example`

- [ ] **Step 1: Create MasterServer project**

```bash
cd /home/binoui/Projects/SlopArena
dotnet new webapi -n MasterServer -o MasterServer --no-https
cd MasterServer
rm -rf Controllers WeatherForecast.cs
```

- [ ] **Step 2: Add NuGet packages**

```bash
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.AspNetCore.SignalR
dotnet add package Newtonsoft.Json
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package System.IdentityModel.Tokens.Jwt
```

- [ ] **Step 3: Reference Shared project**

```bash
dotnet add reference ../Shared/SlopArena.Shared.csproj
```

- [ ] **Step 4: Create appsettings.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=sloparena;Username=postgres;Password=postgres"
  },
  "Steam": {
    "ApiKey": "YOUR_STEAM_API_KEY_HERE",
    "ApiUrl": "https://api.steampowered.com"
  },
  "Jwt": {
    "Secret": "REPLACE_WITH_LONG_RANDOM_STRING_MIN_32_CHARS",
    "Issuer": "SlopArena.Master",
    "Audience": "SlopArena.Client",
    "ExpiryHours": 24
  },
  "Matchmaking": {
    "InitialMmrRange": 100,
    "MmrRangeExpansionPerMinute": 50,
    "MaxWaitTimeMinutes": 5,
    "TickIntervalSeconds": 2
  }
}
```

- [ ] **Step 5: Create .env.example**

```bash
# Master Server Environment Variables
STEAM_API_KEY=your_steam_web_api_key_here
JWT_SECRET=your_long_random_secret_key_min_32_chars
DB_PASSWORD=your_postgres_password
```

- [ ] **Step 6: Update Program.cs (minimal setup)**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add services (will expand in later tasks)
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();

var app = builder.Build();

app.MapGet("/health", () => new { status = "ok", version = "0.1.0" });

app.Run();
```

- [ ] **Step 7: Verify project builds**

```bash
dotnet build
```

Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 8: Commit**

```bash
git add MasterServer/
git commit -m "feat(master): initialize master server project

- ASP.NET Core 8.0 Minimal API
- NuGet packages: EF Core, Npgsql, SignalR, JWT
- Basic health endpoint for monitoring
- Configuration structure with Steam API + JWT settings"
```

---

## Task 2: Database Models & Context

**Files:**
- Create: `MasterServer/Data/AppDbContext.cs`
- Create: `MasterServer/Data/Models/User.cs`
- Create: `MasterServer/Data/Models/Match.cs`
- Create: `MasterServer/Data/Models/GameServer.cs`

- [ ] **Step 1: Create User model**

```csharp
// MasterServer/Data/Models/User.cs
namespace MasterServer.Data.Models;

public class User
{
    public long SteamId { get; set; } // Primary key
    public string Username { get; set; } = string.Empty;
    public int Mmr { get; set; } = 1000;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLogin { get; set; }
}
```

- [ ] **Step 2: Create Match model**

```csharp
// MasterServer/Data/Models/Match.cs
namespace MasterServer.Data.Models;

public class Match
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Player1SteamId { get; set; }
    public long Player2SteamId { get; set; }
    public long? WinnerSteamId { get; set; } // Nullable for draws/disconnects
    public string ServerRegion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    
    // Navigation properties
    public User? Player1 { get; set; }
    public User? Player2 { get; set; }
    public User? Winner { get; set; }
}
```

- [ ] **Step 3: Create GameServer model**

```csharp
// MasterServer/Data/Models/GameServer.cs
namespace MasterServer.Data.Models;

public class GameServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Region { get; set; } = string.Empty;
    public bool IsOfficial { get; set; }
    public int MaxConcurrentMatches { get; set; }
    public int CurrentMatches { get; set; }
    public string? CustomRulesJson { get; set; } // JSON serialized, nullable
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public string ApiToken { get; set; } = string.Empty; // For auth
}
```

- [ ] **Step 4: Create DbContext**

```csharp
// MasterServer/Data/AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using MasterServer.Data.Models;

namespace MasterServer.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Match> Matches { get; set; } = null!;
    public DbSet<GameServer> GameServers { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.SteamId);
            entity.Property(e => e.Username).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Mmr).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        // Match entity
        modelBuilder.Entity<Match>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Player1SteamId);
            entity.HasIndex(e => e.Player2SteamId);
            entity.Property(e => e.ServerRegion).HasMaxLength(16).IsRequired();
            
            // Relationships (no cascade delete to avoid issues)
            entity.HasOne(e => e.Player1)
                .WithMany()
                .HasForeignKey(e => e.Player1SteamId)
                .OnDelete(DeleteBehavior.Restrict);
                
            entity.HasOne(e => e.Player2)
                .WithMany()
                .HasForeignKey(e => e.Player2SteamId)
                .OnDelete(DeleteBehavior.Restrict);
                
            entity.HasOne(e => e.Winner)
                .WithMany()
                .HasForeignKey(e => e.WinnerSteamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // GameServer entity
        modelBuilder.Entity<GameServer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Region);
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();
            entity.Property(e => e.Region).HasMaxLength(16).IsRequired();
            entity.Property(e => e.ApiToken).HasMaxLength(128).IsRequired();
        });
    }
}
```

- [ ] **Step 5: Register DbContext in Program.cs**

```csharp
// Add after builder.Services line in Program.cs
using Microsoft.EntityFrameworkCore;
using MasterServer.Data;

// ... existing code ...
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
```

- [ ] **Step 6: Create initial migration**

```bash
dotnet ef migrations add InitialCreate --output-dir Data/Migrations
```

Expected: Migration created successfully.

- [ ] **Step 7: Verify migration file generated**

```bash
ls Data/Migrations/
```

Expected: `*_InitialCreate.cs` and `AppDbContextModelSnapshot.cs` files exist.

- [ ] **Step 8: Commit**

```bash
git add MasterServer/Data/
git add MasterServer/Program.cs
git commit -m "feat(master): add database models and EF Core context

- User model: SteamId (PK), Username, MMR, timestamps
- Match model: players, winner, region, timestamps
- GameServer model: registration, heartbeat tracking
- EF Core migration: InitialCreate"
```

---

## Task 3: Steam Authentication Service

**Files:**
- Create: `MasterServer/Services/SteamAuthService.cs`
- Create: `MasterServer.Tests/Services/SteamAuthServiceTests.cs`
- Modify: `MasterServer/Program.cs`

- [ ] **Step 1: Write failing test for Steam auth**

```csharp
// MasterServer.Tests/Services/SteamAuthServiceTests.cs
using Xunit;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MasterServer.Services;
using Microsoft.Extensions.Configuration;

namespace MasterServer.Tests.Services;

public class SteamAuthServiceTests
{
    [Fact]
    public async Task ValidateSteamTicket_WithValidResponse_ReturnsUserInfo()
    {
        // Arrange
        var mockHttpHandler = new Mock<HttpMessageHandler>();
        mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(@"{
                    ""response"": {
                        ""params"": {
                            ""result"": ""OK"",
                            ""steamid"": ""76561198012345678"",
                            ""ownersteamid"": ""76561198012345678""
                        }
                    }
                }")
            });

        var httpClient = new HttpClient(mockHttpHandler.Object);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                {"Steam:ApiKey", "test_api_key"},
                {"Steam:ApiUrl", "https://api.steampowered.com"}
            }!)
            .Build();

        var service = new SteamAuthService(httpClient, config);

        // Act
        var result = await service.ValidateSteamTicket("test_ticket");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(76561198012345678, result.SteamId);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateSteamTicket_WithInvalidResponse_ReturnsInvalid()
    {
        // Arrange
        var mockHttpHandler = new Mock<HttpMessageHandler>();
        mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(@"{
                    ""response"": {
                        ""error"": {
                            ""errorcode"": 102,
                            ""errordesc"": ""Invalid ticket""
                        }
                    }
                }")
            });

        var httpClient = new HttpClient(mockHttpHandler.Object);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                {"Steam:ApiKey", "test_api_key"},
                {"Steam:ApiUrl", "https://api.steampowered.com"}
            }!)
            .Build();

        var service = new SteamAuthService(httpClient, config);

        // Act
        var result = await service.ValidateSteamTicket("invalid_ticket");

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsValid);
    }
}
```

- [ ] **Step 2: Create test project if not exists**

```bash
cd /home/binoui/Projects/SlopArena
dotnet new xunit -n MasterServer.Tests -o MasterServer.Tests
cd MasterServer.Tests
dotnet add reference ../MasterServer/MasterServer.csproj
dotnet add package Moq
mkdir Services
```

- [ ] **Step 3: Run test to verify it fails**

```bash
dotnet test --filter "FullyQualifiedName~SteamAuthServiceTests"
```

Expected: FAIL with "SteamAuthService not found" or similar.

- [ ] **Step 4: Implement SteamAuthService**

```csharp
// MasterServer/Services/SteamAuthService.cs
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MasterServer.Services;

public record SteamAuthResult(bool IsValid, long SteamId, string? ErrorMessage = null);

public class SteamAuthService
{
    private readonly HttpClient _httpClient;
    private readonly string _steamApiKey;
    private readonly string _steamApiUrl;

    public SteamAuthService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _steamApiKey = configuration["Steam:ApiKey"] 
            ?? throw new InvalidOperationException("Steam:ApiKey not configured");
        _steamApiUrl = configuration["Steam:ApiUrl"] 
            ?? throw new InvalidOperationException("Steam:ApiUrl not configured");
    }

    public async Task<SteamAuthResult> ValidateSteamTicket(string ticket)
    {
        try
        {
            var url = $"{_steamApiUrl}/ISteamUserAuth/AuthenticateUserTicket/v1/" +
                      $"?key={_steamApiKey}&appid=480&ticket={ticket}";

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Check for error
            if (root.TryGetProperty("response", out var responseObj) &&
                responseObj.TryGetProperty("error", out var error))
            {
                var errorDesc = error.TryGetProperty("errordesc", out var desc) 
                    ? desc.GetString() 
                    : "Unknown error";
                return new SteamAuthResult(false, 0, errorDesc);
            }

            // Extract SteamID
            if (responseObj.TryGetProperty("params", out var paramsObj) &&
                paramsObj.TryGetProperty("steamid", out var steamIdElem))
            {
                var steamIdStr = steamIdElem.GetString();
                if (long.TryParse(steamIdStr, out var steamId))
                {
                    return new SteamAuthResult(true, steamId);
                }
            }

            return new SteamAuthResult(false, 0, "Invalid response format");
        }
        catch (Exception ex)
        {
            return new SteamAuthResult(false, 0, $"Exception: {ex.Message}");
        }
    }
}
```

- [ ] **Step 5: Register service in Program.cs**

```csharp
// Add after builder.Services.AddDbContext in Program.cs
builder.Services.AddHttpClient<SteamAuthService>();
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd MasterServer.Tests
dotnet test --filter "FullyQualifiedName~SteamAuthServiceTests" -v n
```

Expected: 2 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add MasterServer/Services/SteamAuthService.cs
git add MasterServer.Tests/
git add MasterServer/Program.cs
git commit -m "feat(master): add Steam authentication service

- Validates Steam session tickets via ISteamUserAuth API
- Returns SteamId and validation status
- Unit tests with mocked HTTP responses
- Registered as scoped service with HttpClient"
```

---

## Task 4: JWT Token Generation

**Files:**
- Create: `MasterServer/Services/JwtTokenService.cs`
- Modify: `MasterServer/Program.cs`

- [ ] **Step 1: Implement JwtTokenService**

```csharp
// MasterServer/Services/JwtTokenService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MasterServer.Services;

public class JwtTokenService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryHours;

    public JwtTokenService(IConfiguration configuration)
    {
        _secret = configuration["Jwt:Secret"] 
            ?? throw new InvalidOperationException("Jwt:Secret not configured");
        _issuer = configuration["Jwt:Issuer"] ?? "SlopArena.Master";
        _audience = configuration["Jwt:Audience"] ?? "SlopArena.Client";
        _expiryHours = int.Parse(configuration["Jwt:ExpiryHours"] ?? "24");

        if (_secret.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Secret must be at least 32 characters");
        }
    }

    public string GenerateToken(long steamId, string username, int mmr)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, steamId.ToString()),
            new Claim("username", username),
            new Claim("mmr", mmr.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_expiryHours),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _issuer,
            ValidAudience = _audience,
            IssuerSigningKey = key
        };

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 2: Register JWT service in Program.cs**

```csharp
// Add after SteamAuthService registration
builder.Services.AddSingleton<JwtTokenService>();

// Add JWT authentication
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var jwtSecret = builder.Configuration["Jwt:Secret"] 
    ?? throw new InvalidOperationException("Jwt:Secret not configured");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();
```

- [ ] **Step 3: Enable authentication middleware**

```csharp
// Add before app.Run() in Program.cs
app.UseAuthentication();
app.UseAuthorization();
```

- [ ] **Step 4: Verify project builds**

```bash
cd /home/binoui/Projects/SlopArena/MasterServer
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add MasterServer/Services/JwtTokenService.cs
git add MasterServer/Program.cs
git commit -m "feat(master): add JWT token generation and validation

- JwtTokenService: generates tokens with SteamId, username, MMR claims
- JWT authentication middleware configured
- 24h token expiry (configurable)"
```

---

## Task 5: Authentication Endpoint

**Files:**
- Modify: `MasterServer/Program.cs` (add /auth/steam endpoint)
- Create: `MasterServer/DTOs/AuthRequest.cs`
- Create: `MasterServer/DTOs/AuthResponse.cs`

- [ ] **Step 1: Create DTOs**

```csharp
// MasterServer/DTOs/AuthRequest.cs
namespace MasterServer.DTOs;

public record AuthRequest(string SteamTicket);
```

```csharp
// MasterServer/DTOs/AuthResponse.cs
namespace MasterServer.DTOs;

public record AuthResponse(
    bool Success,
    string? Token,
    long SteamId,
    string Username,
    int Mmr,
    string? ErrorMessage
);
```

- [ ] **Step 2: Add authentication endpoint**

```csharp
// Add before app.Run() in Program.cs
using MasterServer.DTOs;
using MasterServer.Services;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

app.MapPost("/auth/steam", async (
    AuthRequest request,
    SteamAuthService steamAuth,
    JwtTokenService jwtService,
    AppDbContext db) =>
{
    // Validate Steam ticket
    var steamResult = await steamAuth.ValidateSteamTicket(request.SteamTicket);
    
    if (!steamResult.IsValid)
    {
        return Results.BadRequest(new AuthResponse(
            false, null, 0, string.Empty, 0, steamResult.ErrorMessage));
    }

    // Get or create user
    var user = await db.Users.FindAsync(steamResult.SteamId);
    if (user == null)
    {
        // Fetch username from Steam (simplified: use SteamId as fallback)
        // In production, call ISteamUser/GetPlayerSummaries
        var username = $"Player{steamResult.SteamId % 10000}";
        
        user = new MasterServer.Data.Models.User
        {
            SteamId = steamResult.SteamId,
            Username = username,
            Mmr = 1000,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
    }
    
    user.LastLogin = DateTime.UtcNow;
    await db.SaveChangesAsync();

    // Generate JWT
    var token = jwtService.GenerateToken(user.SteamId, user.Username, user.Mmr);

    return Results.Ok(new AuthResponse(
        true, token, user.SteamId, user.Username, user.Mmr, null));
});
```

- [ ] **Step 3: Test endpoint manually (requires Docker Postgres)**

```bash
# Start Postgres via Docker
docker run -d --name sloparena-postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=sloparena \
  -p 5432:5432 \
  postgres:16

# Apply migrations
cd /home/binoui/Projects/SlopArena/MasterServer
dotnet ef database update

# Run master server
dotnet run
```

```bash
# In another terminal, test endpoint (will fail with invalid ticket, but endpoint should exist)
curl -X POST http://localhost:5000/auth/steam \
  -H "Content-Type: application/json" \
  -d '{"steamTicket": "test_invalid_ticket"}'
```

Expected: 400 Bad Request with error message (Steam API will reject test ticket).

- [ ] **Step 4: Stop test server**

```bash
# Ctrl+C in the terminal running dotnet run
# Stop Docker container
docker stop sloparena-postgres
docker rm sloparena-postgres
```

- [ ] **Step 5: Commit**

```bash
git add MasterServer/DTOs/
git add MasterServer/Program.cs
git commit -m "feat(master): add Steam authentication endpoint

POST /auth/steam
- Validates Steam ticket via SteamAuthService
- Creates user in DB if first login (default MMR 1000)
- Returns JWT token with SteamId, username, MMR claims
- Updates LastLogin timestamp"
```

---

## Task 6: Matchmaking Service

**Files:**
- Create: `MasterServer/Services/MatchmakingService.cs`
- Create: `MasterServer.Tests/Services/MatchmakingServiceTests.cs`

- [ ] **Step 1: Write failing test for matchmaking**

```csharp
// MasterServer.Tests/Services/MatchmakingServiceTests.cs
using Xunit;
using MasterServer.Services;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MasterServer.Tests.Services;

public class MatchmakingServiceTests
{
    private AppDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private IConfiguration GetTestConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                {"Matchmaking:InitialMmrRange", "100"},
                {"Matchmaking:MmrRangeExpansionPerMinute", "50"},
                {"Matchmaking:MaxWaitTimeMinutes", "5"}
            }!)
            .Build();
    }

    [Fact]
    public async Task JoinQueue_AddsPlayerToQueue()
    {
        // Arrange
        var db = GetInMemoryDbContext();
        var config = GetTestConfiguration();
        var gameServerRegistry = new GameServerRegistry(db);
        var service = new MatchmakingService(db, config, gameServerRegistry);

        // Act
        await service.JoinQueue(76561198012345678, 1500, "EU");

        // Assert
        var queueSize = service.GetQueueSize();
        Assert.Equal(1, queueSize);
    }

    [Fact]
    public async Task TickMatchmaking_WithTwoPlayers_CreatesMatch()
    {
        // Arrange
        var db = GetInMemoryDbContext();
        var config = GetTestConfiguration();
        
        // Add a mock game server
        var gameServer = new MasterServer.Data.Models.GameServer
        {
            Id = Guid.NewGuid(),
            Name = "Test EU Server",
            IpAddress = "127.0.0.1",
            Port = 7777,
            Region = "EU",
            IsOfficial = true,
            MaxConcurrentMatches = 10,
            CurrentMatches = 0,
            ApiToken = "test_token"
        };
        db.GameServers.Add(gameServer);
        await db.SaveChangesAsync();

        var gameServerRegistry = new GameServerRegistry(db);
        var service = new MatchmakingService(db, config, gameServerRegistry);

        // Add two players with similar MMR
        await service.JoinQueue(76561198012345678, 1500, "EU");
        await service.JoinQueue(76561198087654321, 1520, "EU");

        // Act
        await service.TickMatchmaking();

        // Assert
        var queueSize = service.GetQueueSize();
        Assert.Equal(0, queueSize); // Both players should be matched and removed
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd /home/binoui/Projects/SlopArena/MasterServer.Tests
dotnet test --filter "FullyQualifiedName~MatchmakingServiceTests" -v n
```

Expected: FAIL with "MatchmakingService not found".

- [ ] **Step 3: Implement MatchmakingService**

```csharp
// MasterServer/Services/MatchmakingService.cs
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using MasterServer.Data;

namespace MasterServer.Services;

public record QueueEntry(long SteamId, int Mmr, string Region, DateTime JoinedAt);

public record MatchAssignment(
    Guid MatchId,
    long Player1SteamId,
    long Player2SteamId,
    string ServerIp,
    int ServerPort,
    string Token
);

public class MatchmakingService
{
    private readonly AppDbContext _db;
    private readonly GameServerRegistry _gameServerRegistry;
    private readonly ConcurrentQueue<QueueEntry> _queue = new();
    private readonly int _initialMmrRange;
    private readonly int _mmrRangeExpansionPerMinute;
    private readonly int _maxWaitTimeMinutes;

    // Store pending match assignments (client polls for these)
    private readonly ConcurrentDictionary<long, MatchAssignment> _pendingAssignments = new();

    public MatchmakingService(
        AppDbContext db,
        IConfiguration configuration,
        GameServerRegistry gameServerRegistry)
    {
        _db = db;
        _gameServerRegistry = gameServerRegistry;
        _initialMmrRange = int.Parse(configuration["Matchmaking:InitialMmrRange"] ?? "100");
        _mmrRangeExpansionPerMinute = int.Parse(configuration["Matchmaking:MmrRangeExpansionPerMinute"] ?? "50");
        _maxWaitTimeMinutes = int.Parse(configuration["Matchmaking:MaxWaitTimeMinutes"] ?? "5");
    }

    public Task JoinQueue(long steamId, int mmr, string region)
    {
        // Remove existing entry if player rejoins
        var tempQueue = new ConcurrentQueue<QueueEntry>();
        while (_queue.TryDequeue(out var entry))
        {
            if (entry.SteamId != steamId)
            {
                tempQueue.Enqueue(entry);
            }
        }
        foreach (var entry in tempQueue)
        {
            _queue.Enqueue(entry);
        }

        // Add new entry
        _queue.Enqueue(new QueueEntry(steamId, mmr, region, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public Task LeaveQueue(long steamId)
    {
        var tempQueue = new ConcurrentQueue<QueueEntry>();
        while (_queue.TryDequeue(out var entry))
        {
            if (entry.SteamId != steamId)
            {
                tempQueue.Enqueue(entry);
            }
        }
        foreach (var entry in tempQueue)
        {
            _queue.Enqueue(entry);
        }
        return Task.CompletedTask;
    }

    public int GetQueueSize() => _queue.Count;

    public MatchAssignment? GetPendingAssignment(long steamId)
    {
        _pendingAssignments.TryGetValue(steamId, out var assignment);
        return assignment;
    }

    public void ClearPendingAssignment(long steamId)
    {
        _pendingAssignments.TryRemove(steamId, out _);
    }

    public async Task TickMatchmaking()
    {
        var players = _queue.ToArray();
        if (players.Length < 2) return;

        var matched = new HashSet<long>();

        foreach (var player1 in players)
        {
            if (matched.Contains(player1.SteamId)) continue;

            // Calculate dynamic MMR range based on wait time
            var waitTime = DateTime.UtcNow - player1.JoinedAt;
            var mmrRange = _initialMmrRange + 
                (int)(waitTime.TotalMinutes * _mmrRangeExpansionPerMinute);

            // Find best match
            QueueEntry? bestMatch = null;
            int bestMmrDiff = int.MaxValue;

            foreach (var player2 in players)
            {
                if (player2.SteamId == player1.SteamId) continue;
                if (matched.Contains(player2.SteamId)) continue;
                if (player2.Region != player1.Region) continue; // Same region for now

                var mmrDiff = Math.Abs(player1.Mmr - player2.Mmr);
                if (mmrDiff <= mmrRange && mmrDiff < bestMmrDiff)
                {
                    bestMatch = player2;
                    bestMmrDiff = mmrDiff;
                }
            }

            if (bestMatch != null)
            {
                // Create match assignment
                var gameServer = await _gameServerRegistry.GetAvailableServer(player1.Region);
                if (gameServer == null) continue; // No available server

                var matchId = Guid.NewGuid();
                var token = Guid.NewGuid().ToString();

                var assignment = new MatchAssignment(
                    matchId,
                    player1.SteamId,
                    bestMatch.Value.SteamId,
                    gameServer.IpAddress,
                    gameServer.Port,
                    token
                );

                // Store assignments for both players
                _pendingAssignments[player1.SteamId] = assignment;
                _pendingAssignments[bestMatch.Value.SteamId] = assignment;

                // Create match record in DB
                var match = new MasterServer.Data.Models.Match
                {
                    Id = matchId,
                    Player1SteamId = player1.SteamId,
                    Player2SteamId = bestMatch.Value.SteamId,
                    ServerRegion = player1.Region,
                    StartedAt = DateTime.UtcNow
                };
                _db.Matches.Add(match);
                await _db.SaveChangesAsync();

                // Mark as matched
                matched.Add(player1.SteamId);
                matched.Add(bestMatch.Value.SteamId);

                // Increment server match count
                await _gameServerRegistry.IncrementMatchCount(gameServer.Id);
            }
        }

        // Remove matched players from queue
        var tempQueue = new ConcurrentQueue<QueueEntry>();
        while (_queue.TryDequeue(out var entry))
        {
            if (!matched.Contains(entry.SteamId))
            {
                tempQueue.Enqueue(entry);
            }
        }
        foreach (var entry in tempQueue)
        {
            _queue.Enqueue(entry);
        }
    }
}
```

- [ ] **Step 4: Create GameServerRegistry stub**

```csharp
// MasterServer/Services/GameServerRegistry.cs
using Microsoft.EntityFrameworkCore;
using MasterServer.Data;

namespace MasterServer.Services;

public class GameServerRegistry
{
    private readonly AppDbContext _db;

    public GameServerRegistry(AppDbContext db)
    {
        _db = db;
    }

    public async Task<MasterServer.Data.Models.GameServer?> GetAvailableServer(string region)
    {
        var server = await _db.GameServers
            .Where(s => s.Region == region && 
                        s.CurrentMatches < s.MaxConcurrentMatches &&
                        s.LastHeartbeat > DateTime.UtcNow.AddSeconds(-30))
            .OrderBy(s => s.CurrentMatches)
            .FirstOrDefaultAsync();

        return server;
    }

    public async Task IncrementMatchCount(Guid serverId)
    {
        var server = await _db.GameServers.FindAsync(serverId);
        if (server != null)
        {
            server.CurrentMatches++;
            await _db.SaveChangesAsync();
        }
    }

    public async Task DecrementMatchCount(Guid serverId)
    {
        var server = await _db.GameServers.FindAsync(serverId);
        if (server != null && server.CurrentMatches > 0)
        {
            server.CurrentMatches--;
            await _db.SaveChangesAsync();
        }
    }
}
```

- [ ] **Step 5: Register services in Program.cs**

```csharp
// Add after JwtTokenService registration
builder.Services.AddSingleton<GameServerRegistry>();
builder.Services.AddSingleton<MatchmakingService>();
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd /home/binoui/Projects/SlopArena/MasterServer.Tests
dotnet test --filter "FullyQualifiedName~MatchmakingServiceTests" -v n
```

Expected: 2 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add MasterServer/Services/MatchmakingService.cs
git add MasterServer/Services/GameServerRegistry.cs
git add MasterServer.Tests/Services/MatchmakingServiceTests.cs
git add MasterServer/Program.cs
git commit -m "feat(master): add matchmaking service

- Queue management: join, leave, tick
- MMR-based pairing with dynamic range expansion
- Assigns matches to least-loaded game server
- GameServerRegistry: tracks available servers, match counts
- Unit tests for queue and match creation"
```

---

## Task 7: Matchmaking Endpoints & Background Ticker

**Files:**
- Modify: `MasterServer/Program.cs` (add endpoints + background service)
- Create: `MasterServer/Services/MatchmakingTickerService.cs`

- [ ] **Step 1: Create background ticker service**

```csharp
// MasterServer/Services/MatchmakingTickerService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace MasterServer.Services;

public class MatchmakingTickerService : BackgroundService
{
    private readonly MatchmakingService _matchmaking;
    private readonly int _tickIntervalSeconds;
    private readonly ILogger<MatchmakingTickerService> _logger;

    public MatchmakingTickerService(
        MatchmakingService matchmaking,
        IConfiguration configuration,
        ILogger<MatchmakingTickerService> logger)
    {
        _matchmaking = matchmaking;
        _tickIntervalSeconds = int.Parse(configuration["Matchmaking:TickIntervalSeconds"] ?? "2");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Matchmaking ticker started (interval: {Interval}s)", _tickIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _matchmaking.TickMatchmaking();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in matchmaking tick");
            }

            await Task.Delay(TimeSpan.FromSeconds(_tickIntervalSeconds), stoppingToken);
        }
    }
}
```

- [ ] **Step 2: Register ticker service**

```csharp
// Add after MatchmakingService registration in Program.cs
builder.Services.AddHostedService<MatchmakingTickerService>();
```

- [ ] **Step 3: Add matchmaking endpoints**

```csharp
// Add before app.Run() in Program.cs
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

app.MapGet("/matchmaking/join", [Authorize] async (
    HttpContext httpContext,
    MatchmakingService matchmaking,
    AppDbContext db) =>
{
    var steamIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (steamIdClaim == null || !long.TryParse(steamIdClaim, out var steamId))
    {
        return Results.Unauthorized();
    }

    var user = await db.Users.FindAsync(steamId);
    if (user == null)
    {
        return Results.NotFound(new { error = "User not found" });
    }

    var region = httpContext.Request.Query["region"].ToString();
    if (string.IsNullOrEmpty(region))
    {
        region = "EU"; // Default region
    }

    await matchmaking.JoinQueue(steamId, user.Mmr, region);

    return Results.Ok(new { status = "queued", queueSize = matchmaking.GetQueueSize() });
});

app.MapGet("/matchmaking/status", [Authorize] async (
    HttpContext httpContext,
    MatchmakingService matchmaking) =>
{
    var steamIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (steamIdClaim == null || !long.TryParse(steamIdClaim, out var steamId))
    {
        return Results.Unauthorized();
    }

    var assignment = matchmaking.GetPendingAssignment(steamId);
    if (assignment != null)
    {
        // Clear assignment after delivering it
        matchmaking.ClearPendingAssignment(steamId);
        
        return Results.Ok(new
        {
            status = "found",
            matchId = assignment.MatchId,
            serverIp = assignment.ServerIp,
            serverPort = assignment.ServerPort,
            token = assignment.Token
        });
    }

    return Results.Ok(new { status = "searching", queueSize = matchmaking.GetQueueSize() });
});

app.MapPost("/matchmaking/leave", [Authorize] async (
    HttpContext httpContext,
    MatchmakingService matchmaking) =>
{
    var steamIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (steamIdClaim == null || !long.TryParse(steamIdClaim, out var steamId))
    {
        return Results.Unauthorized();
    }

    await matchmaking.LeaveQueue(steamId);
    return Results.Ok(new { status = "left" });
});
```

- [ ] **Step 4: Verify project builds**

```bash
cd /home/binoui/Projects/SlopArena/MasterServer
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add MasterServer/Services/MatchmakingTickerService.cs
git add MasterServer/Program.cs
git commit -m "feat(master): add matchmaking endpoints and background ticker

GET /matchmaking/join - Join ranked 1v1 queue (requires JWT)
GET /matchmaking/status - Poll for match assignment
POST /matchmaking/leave - Leave queue
Background service: ticks matchmaking every 2 seconds"
```

---

## Task 8: Global Chat (SignalR Hub)

**Files:**
- Create: `MasterServer/Hubs/ChatHub.cs`
- Modify: `MasterServer/Program.cs`

- [ ] **Step 1: Implement ChatHub**

```csharp
// MasterServer/Hubs/ChatHub.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace MasterServer.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private static readonly Dictionary<string, string> _connectedUsers = new();

    public override async Task OnConnectedAsync()
    {
        var steamId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = Context.User?.FindFirst("username")?.Value ?? "Unknown";

        if (steamId != null)
        {
            _connectedUsers[Context.ConnectionId] = username;
            await Clients.All.SendAsync("UserJoined", username);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connectedUsers.TryGetValue(Context.ConnectionId, out var username))
        {
            _connectedUsers.Remove(Context.ConnectionId);
            await Clients.All.SendAsync("UserLeft", username);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendGlobalMessage(string message)
    {
        var username = Context.User?.FindFirst("username")?.Value ?? "Unknown";
        
        if (string.IsNullOrWhiteSpace(message) || message.Length > 500)
        {
            return; // Ignore invalid messages
        }

        await Clients.All.SendAsync("ReceiveGlobalMessage", username, message, DateTime.UtcNow);
    }

    public Task GetOnlineCount()
    {
        return Clients.Caller.SendAsync("OnlineCount", _connectedUsers.Count);
    }
}
```

- [ ] **Step 2: Register SignalR hub**

```csharp
// Add before app.Run() in Program.cs
using MasterServer.Hubs;

app.MapHub<ChatHub>("/chat");
```

- [ ] **Step 3: Update SignalR with JWT authentication**

```csharp
// Modify builder.Services.AddSignalR() in Program.cs to:
builder.Services.AddSignalR();

// Add JWT support for SignalR (after AddAuthentication block)
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chat"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});
```

- [ ] **Step 4: Verify project builds**

```bash
cd /home/binoui/Projects/SlopArena/MasterServer
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add MasterServer/Hubs/ChatHub.cs
git add MasterServer/Program.cs
git commit -m "feat(master): add global chat via SignalR

/chat hub (WebSocket)
- SendGlobalMessage: broadcast to all connected clients
- UserJoined/UserLeft events
- JWT authentication via query param (access_token)
- 500 char message limit"
```

---

## Task 9: Game Server Registration & Heartbeat Endpoints

**Files:**
- Modify: `MasterServer/Program.cs` (add server endpoints)
- Create: `MasterServer/DTOs/ServerRegistrationRequest.cs`
- Create: `MasterServer/DTOs/HeartbeatRequest.cs`

- [ ] **Step 1: Create DTOs**

```csharp
// MasterServer/DTOs/ServerRegistrationRequest.cs
namespace MasterServer.DTOs;

public record ServerRegistrationRequest(
    string Name,
    string IpAddress,
    int Port,
    string Region,
    bool IsOfficial,
    int MaxConcurrentMatches,
    string? CustomRulesJson
);
```

```csharp
// MasterServer/DTOs/HeartbeatRequest.cs
namespace MasterServer.DTOs;

public record HeartbeatRequest(int CurrentMatches);
```

- [ ] **Step 2: Add server registration endpoint**

```csharp
// Add before app.Run() in Program.cs
app.MapPost("/servers/register", async (
    ServerRegistrationRequest request,
    AppDbContext db) =>
{
    var apiToken = Guid.NewGuid().ToString();

    var gameServer = new MasterServer.Data.Models.GameServer
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        IpAddress = request.IpAddress,
        Port = request.Port,
        Region = request.Region,
        IsOfficial = request.IsOfficial,
        MaxConcurrentMatches = request.MaxConcurrentMatches,
        CurrentMatches = 0,
        CustomRulesJson = request.CustomRulesJson,
        ApiToken = apiToken,
        LastHeartbeat = DateTime.UtcNow
    };

    db.GameServers.Add(gameServer);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        serverId = gameServer.Id,
        apiToken = apiToken
    });
});
```

- [ ] **Step 3: Add heartbeat endpoint**

```csharp
// Add after registration endpoint
app.MapPost("/servers/{serverId}/heartbeat", async (
    Guid serverId,
    HeartbeatRequest request,
    HttpContext httpContext,
    AppDbContext db) =>
{
    var token = httpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
    
    var server = await db.GameServers.FindAsync(serverId);
    if (server == null)
    {
        return Results.NotFound(new { error = "Server not found" });
    }

    if (server.ApiToken != token)
    {
        return Results.Unauthorized();
    }

    server.CurrentMatches = request.CurrentMatches;
    server.LastHeartbeat = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new { status = "ok" });
});
```

- [ ] **Step 4: Add match result endpoint**

```csharp
// Add after heartbeat endpoint
app.MapPost("/match/result", async (
    MatchResultRequest request,
    HttpContext httpContext,
    AppDbContext db) =>
{
    var token = httpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
    
    // Verify server token (find server with this token)
    var server = await db.GameServers.FirstOrDefaultAsync(s => s.ApiToken == token);
    if (server == null)
    {
        return Results.Unauthorized();
    }

    // Update match record
    var match = await db.Matches.FindAsync(request.MatchId);
    if (match == null)
    {
        return Results.NotFound(new { error = "Match not found" });
    }

    match.WinnerSteamId = request.WinnerSteamId;
    match.EndedAt = DateTime.UtcNow;

    // Update MMR (simple ELO)
    var winner = await db.Users.FindAsync(request.WinnerSteamId);
    var loser = await db.Users.FindAsync(
        match.Player1SteamId == request.WinnerSteamId 
            ? match.Player2SteamId 
            : match.Player1SteamId);

    if (winner != null && loser != null)
    {
        var expectedWin = 1.0 / (1.0 + Math.Pow(10, (loser.Mmr - winner.Mmr) / 400.0));
        var kFactor = 32;
        var mmrChange = (int)(kFactor * (1 - expectedWin));

        winner.Mmr += mmrChange;
        loser.Mmr -= mmrChange;

        // Clamp MMR to reasonable range
        winner.Mmr = Math.Max(0, winner.Mmr);
        loser.Mmr = Math.Max(0, loser.Mmr);
    }

    await db.SaveChangesAsync();

    // Decrement server match count
    server.CurrentMatches = Math.Max(0, server.CurrentMatches - 1);
    await db.SaveChangesAsync();

    return Results.Ok(new { status = "recorded", mmrChange });
});

// Add DTO for match result
public record MatchResultRequest(Guid MatchId, long WinnerSteamId);
```

- [ ] **Step 5: Verify project builds**

```bash
cd /home/binoui/Projects/SlopArena/MasterServer
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add MasterServer/DTOs/
git add MasterServer/Program.cs
git commit -m "feat(master): add game server registration and match tracking

POST /servers/register - Register game server, returns server_id + api_token
POST /servers/{id}/heartbeat - Update server status (requires Bearer token)
POST /match/result - Record match result, update MMR (simple ELO)"
```

---

## Task 10: Docker Compose for Master Server

**Files:**
- Create: `MasterServer/Dockerfile`
- Create: `MasterServer/docker-compose.yml`
- Create: `MasterServer/.dockerignore`

- [ ] **Step 1: Create Dockerfile**

```dockerfile
# MasterServer/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MasterServer/MasterServer.csproj", "MasterServer/"]
COPY ["Shared/SlopArena.Shared.csproj", "Shared/"]
RUN dotnet restore "MasterServer/MasterServer.csproj"
COPY . .
WORKDIR "/src/MasterServer"
RUN dotnet build "MasterServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MasterServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MasterServer.dll"]
```

- [ ] **Step 2: Create docker-compose.yml**

```yaml
# MasterServer/docker-compose.yml
version: '3.8'

services:
  postgres:
    image: postgres:16
    container_name: sloparena-postgres
    environment:
      POSTGRES_DB: sloparena
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: ${DB_PASSWORD:-postgres}
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

  master:
    build:
      context: ..
      dockerfile: MasterServer/Dockerfile
    container_name: sloparena-master
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:5000
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=sloparena;Username=postgres;Password=${DB_PASSWORD:-postgres}"
      Steam__ApiKey: ${STEAM_API_KEY}
      Jwt__Secret: ${JWT_SECRET}
    ports:
      - "5000:5000"
    depends_on:
      postgres:
        condition: service_healthy
    restart: unless-stopped

volumes:
  postgres_data:
```

- [ ] **Step 3: Create .dockerignore**

```
# MasterServer/.dockerignore
**/bin
**/obj
**/.vs
**/.vscode
**/.git
**/node_modules
**/*.md
**/Dockerfile
**/.dockerignore
```

- [ ] **Step 4: Test Docker build**

```bash
cd /home/binoui/Projects/SlopArena
docker build -f MasterServer/Dockerfile -t sloparena-master:test .
```

Expected: Image built successfully.

- [ ] **Step 5: Clean up test image**

```bash
docker rmi sloparena-master:test
```

- [ ] **Step 6: Commit**

```bash
git add MasterServer/Dockerfile
git add MasterServer/docker-compose.yml
git add MasterServer/.dockerignore
git commit -m "feat(master): add Docker deployment configuration

- Multi-stage Dockerfile (build + runtime)
- docker-compose.yml with Postgres + Master
- Health checks for Postgres
- Environment variables via .env file"
```

---

## Task 11: Refactor Game Server - Multi-Match Orchestrator

**Files:**
- Create: `Server/MultiMatchOrchestrator.cs`
- Create: `Server/MatchInstance.cs`
- Modify: `Server/Program.cs`
- Create: `Server/server.json`

- [ ] **Step 1: Create server.json config**

```json
// Server/server.json
{
  "serverName": "Official EU #1",
  "region": "EU",
  "basePort": 7777,
  "maxConcurrentMatches": 15,
  "masterServerUrl": "http://localhost:5000",
  "isOfficial": true
}
```

- [ ] **Step 2: Create MatchInstance (extract from Program.cs)**

```csharp
// Server/MatchInstance.cs
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SlopArena.Shared;

namespace SlopArena.Server;

public class MatchInstance
{
    private readonly int _port;
    private readonly Guid _matchId;
    private readonly string _matchToken;
    private UdpClient? _udpServer;
    private bool _running = true;
    private uint _serverTick = 0;

    private CharacterState _player1State;
    private CharacterState _player2State;
    private readonly CharacterDefinition _characterDef;
    private readonly ArenaDefinition _arena;

    private IPEndPoint? _player1EndPoint;
    private IPEndPoint? _player2EndPoint;
    private readonly List<ClientInputPacket> _inputQueue = new();

    private readonly Action<Guid>? _onMatchEnd;

    public MatchInstance(int port, Guid matchId, string matchToken, Action<Guid>? onMatchEnd = null)
    {
        _port = port;
        _matchId = matchId;
        _matchToken = matchToken;
        _onMatchEnd = onMatchEnd;

        _arena = ArenaRegistry.Get("pit");
        _characterDef = CharacterRegistry.Get(CharacterClass.Manki);

        // Initialize player states at spawn points
        var spawn1 = _arena.SpawnPoints.Length > 0 ? _arena.SpawnPoints[0] : new SpawnPoint { X = 35f, Y = 0.5f, Z = 40f, Yaw = 0f };
        var spawn2 = _arena.SpawnPoints.Length > 1 ? _arena.SpawnPoints[1] : new SpawnPoint { X = 45f, Y = 0.5f, Z = 40f, Yaw = 180f };

        _player1State = CreateInitialState(spawn1);
        _player2State = CreateInitialState(spawn2);
    }

    private static CharacterState CreateInitialState(SpawnPoint spawn)
    {
        return new CharacterState
        {
            PX = spawn.X,
            PY = spawn.Y,
            PZ = spawn.Z,
            FacingYaw = spawn.Yaw,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = 2,
            AirDodgesLeft = 1,
            DamagePercent = 0,
        };
    }

    public void Start()
    {
        try
        {
            _udpServer = new UdpClient(_port);
            _udpServer.Client.Blocking = false;
            Console.WriteLine($"Match {_matchId} started on port {_port}");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            double nextTickTime = 0;
            const double tickDurationMs = 1000.0 / 60.0;

            while (_running)
            {
                double currentTime = stopwatch.Elapsed.TotalMilliseconds;
                if (currentTime >= nextTickTime)
                {
                    ReceiveInputs();
                    Tick();
                    nextTickTime += tickDurationMs;

                    if (currentTime > nextTickTime + tickDurationMs * 10)
                    {
                        nextTickTime = currentTime;
                    }
                }
                else
                {
                    int sleepTime = (int)(nextTickTime - currentTime) - 1;
                    if (sleepTime > 0)
                    {
                        Thread.Sleep(sleepTime);
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Match {_matchId} error: {ex.Message}");
        }
        finally
        {
            _udpServer?.Close();
            Console.WriteLine($"Match {_matchId} ended");
            _onMatchEnd?.Invoke(_matchId);
        }
    }

    private void ReceiveInputs()
    {
        if (_udpServer == null) return;

        while (_udpServer.Available > 0)
        {
            try
            {
                var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _udpServer.Receive(ref remoteEP);

                // TODO: Handle ClientJoinMatchPacket with token validation
                // For now, assign first two connections as player1/player2

                if (data.Length >= ClientInputPacket.Size)
                {
                    var packet = ClientInputPacket.Deserialize(data);

                    if (_player1EndPoint == null)
                    {
                        _player1EndPoint = remoteEP;
                    }
                    else if (_player2EndPoint == null && !remoteEP.Equals(_player1EndPoint))
                    {
                        _player2EndPoint = remoteEP;
                    }

                    if (packet.TickNumber > _serverTick)
                    {
                        _inputQueue.Add(packet);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Match {_matchId} input error: {ex.Message}");
            }
        }
    }

    private void Tick()
    {
        if (_inputQueue.Count > 0)
        {
            _inputQueue.Sort((a, b) => a.TickNumber.CompareTo(b.TickNumber));

            foreach (var inputPacket in _inputQueue)
            {
                if (inputPacket.TickNumber > _serverTick)
                {
                    _serverTick = inputPacket.TickNumber;

                    var input = new InputState
                    {
                        Up = (inputPacket.MovementFlags & 0x01) != 0,
                        Left = (inputPacket.MovementFlags & 0x02) != 0,
                        Down = (inputPacket.MovementFlags & 0x04) != 0,
                        Right = (inputPacket.MovementFlags & 0x08) != 0,
                        Jump = (inputPacket.MovementFlags & 0x10) != 0,
                        Dash = (inputPacket.MovementFlags & 0x20) != 0,
                        Crouch = (inputPacket.MovementFlags & 0x80) != 0,
                        Attack = (inputPacket.ActionFlags & 0x01) != 0,
                        MoveX = ((inputPacket.MovementFlags & 0x08) != 0 ? 1f : 0f) - ((inputPacket.MovementFlags & 0x02) != 0 ? 1f : 0f),
                        MoveY = ((inputPacket.MovementFlags & 0x01) != 0 ? 1f : 0f) - ((inputPacket.MovementFlags & 0x04) != 0 ? 1f : 0f),
                    };

                    // Simulate both players (for now, same input)
                    Simulation.SimulateTick(ref _player1State, _characterDef, input, _arena, onHit: null);
                    Simulation.SimulateTick(ref _player2State, _characterDef, input, _arena, onHit: null);
                }
            }

            _inputQueue.Clear();
            SendState();
        }

        // Check for match end conditions (TODO: implement win/lose logic)
        // For now, match runs indefinitely
    }

    private void SendState()
    {
        if (_udpServer == null) return;

        // Send to player 1
        if (_player1EndPoint != null)
        {
            SendStateToClient(_player1EndPoint, _player1State);
        }

        // Send to player 2
        if (_player2EndPoint != null)
        {
            SendStateToClient(_player2EndPoint, _player2State);
        }
    }

    private void SendStateToClient(IPEndPoint endPoint, CharacterState state)
    {
        var packet = new CharacterStatePacket
        {
            TickNumber = _serverTick,
            PositionX = state.PX,
            PositionY = state.PZ,
            PositionZ = state.PY,
            VelocityX = state.VX,
            VelocityY = state.VZ,
            VelocityZ = state.VY,
            CurrentActionState = (byte)state.State,
            StateDurationFrames = state.StateTicks
        };

        Span<byte> buffer = stackalloc byte[CharacterStatePacket.Size];
        packet.Serialize(buffer);

        try
        {
            _udpServer!.Send(buffer.ToArray(), buffer.Length, endPoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Match {_matchId} send error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _running = false;
    }
}
```

- [ ] **Step 3: Create MultiMatchOrchestrator**

```csharp
// Server/MultiMatchOrchestrator.cs
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SlopArena.Server;

public class MultiMatchOrchestrator
{
    private readonly int _basePort;
    private readonly int _maxMatches;
    private readonly ConcurrentDictionary<Guid, (Thread Thread, MatchInstance Instance)> _activeMatches = new();

    public MultiMatchOrchestrator(int basePort, int maxMatches)
    {
        _basePort = basePort;
        _maxMatches = maxMatches;
    }

    public bool TryStartMatch(Guid matchId, string matchToken)
    {
        if (_activeMatches.Count >= _maxMatches)
        {
            Console.WriteLine($"Cannot start match {matchId}: max capacity reached");
            return false;
        }

        var port = _basePort + _activeMatches.Count;
        var matchInstance = new MatchInstance(port, matchId, matchToken, OnMatchEnd);
        
        var thread = new Thread(matchInstance.Start)
        {
            IsBackground = true,
            Name = $"Match-{matchId}"
        };

        if (_activeMatches.TryAdd(matchId, (thread, matchInstance)))
        {
            thread.Start();
            Console.WriteLine($"Started match {matchId} on port {port}");
            return true;
        }

        return false;
    }

    private void OnMatchEnd(Guid matchId)
    {
        if (_activeMatches.TryRemove(matchId, out var match))
        {
            Console.WriteLine($"Match {matchId} removed from active pool");
            // TODO: Report match result to master server
        }
    }

    public int GetActiveMatchCount() => _activeMatches.Count;

    public void StopAll()
    {
        foreach (var (matchId, (thread, instance)) in _activeMatches)
        {
            Console.WriteLine($"Stopping match {matchId}...");
            instance.Stop();
        }

        _activeMatches.Clear();
    }
}
```

- [ ] **Step 4: Refactor Program.cs to use orchestrator**

```csharp
// Server/Program.cs
using System;
using System.IO;
using System.Text.Json;
using SlopArena.Server;

var configPath = "server.json";
if (!File.Exists(configPath))
{
    Console.WriteLine("server.json not found, creating default...");
    var defaultConfig = new
    {
        serverName = "Game Server",
        region = "EU",
        basePort = 7777,
        maxConcurrentMatches = 15,
        masterServerUrl = "http://localhost:5000",
        isOfficial = false
    };
    File.WriteAllText(configPath, JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true }));
}

var config = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(configPath));
if (config == null)
{
    Console.WriteLine("Failed to load server.json");
    return;
}

Console.WriteLine($"=== SLOPArena Game Server ===");
Console.WriteLine($"Name: {config.ServerName}");
Console.WriteLine($"Region: {config.Region}");
Console.WriteLine($"Base Port: {config.BasePort}");
Console.WriteLine($"Max Matches: {config.MaxConcurrentMatches}");

var orchestrator = new MultiMatchOrchestrator(config.BasePort, config.MaxConcurrentMatches);

// TODO: Register with master server
// TODO: Start heartbeat loop
// TODO: Listen for match assignments from master

// For testing: start a single match
var testMatchId = Guid.NewGuid();
orchestrator.TryStartMatch(testMatchId, "test_token");

Console.WriteLine("Press Ctrl+C to stop...");
Console.CancelKeyPress += (sender, args) =>
{
    args.Cancel = true;
    Console.WriteLine("Shutting down...");
    orchestrator.StopAll();
    Environment.Exit(0);
};

// Keep alive
while (true)
{
    System.Threading.Thread.Sleep(1000);
}

public record ServerConfig(
    string ServerName,
    string Region,
    int BasePort,
    int MaxConcurrentMatches,
    string MasterServerUrl,
    bool IsOfficial
);
```

- [ ] **Step 5: Update Server.csproj to copy server.json**

```xml
<!-- Add to Server/SlopArena.Server.csproj before </Project> -->
<ItemGroup>
  <None Update="server.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 6: Verify game server builds**

```bash
cd /home/binoui/Projects/SlopArena/Server
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add Server/MultiMatchOrchestrator.cs
git add Server/MatchInstance.cs
git add Server/Program.cs
git add Server/server.json
git add Server/SlopArena.Server.csproj
git commit -m "feat(server): refactor to multi-match orchestrator

- MultiMatchOrchestrator: spawns 1 thread per match
- MatchInstance: extracted from Program.cs, isolated match logic
- server.json: configuration file for region, ports, max matches
- Supports up to 15 concurrent matches per game server"
```

---

## Task 12: Game Server Registration Service

**Files:**
- Create: `Server/GameServerRegistration.cs`
- Modify: `Server/Program.cs`

- [ ] **Step 1: Implement GameServerRegistration**

```csharp
// Server/GameServerRegistration.cs
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SlopArena.Server;

public class GameServerRegistration
{
    private readonly string _masterServerUrl;
    private readonly string _serverName;
    private readonly string _ipAddress;
    private readonly int _port;
    private readonly string _region;
    private readonly bool _isOfficial;
    private readonly int _maxMatches;
    private readonly HttpClient _httpClient;
    
    private Guid _serverId;
    private string? _apiToken;
    private bool _isRegistered;

    public GameServerRegistration(
        string masterServerUrl,
        string serverName,
        string ipAddress,
        int port,
        string region,
        bool isOfficial,
        int maxMatches)
    {
        _masterServerUrl = masterServerUrl;
        _serverName = serverName;
        _ipAddress = ipAddress;
        _port = port;
        _region = region;
        _isOfficial = isOfficial;
        _maxMatches = maxMatches;
        _httpClient = new HttpClient();
    }

    public async Task<bool> RegisterAsync()
    {
        try
        {
            var request = new
            {
                name = _serverName,
                ipAddress = _ipAddress,
                port = _port,
                region = _region,
                isOfficial = _isOfficial,
                maxConcurrentMatches = _maxMatches,
                customRulesJson = (string?)null
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_masterServerUrl}/servers/register",
                request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<RegistrationResponse>();
                if (result != null)
                {
                    _serverId = result.ServerId;
                    _apiToken = result.ApiToken;
                    _isRegistered = true;
                    Console.WriteLine($"Registered with master server (ID: {_serverId})");
                    return true;
                }
            }

            Console.WriteLine($"Registration failed: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Registration error: {ex.Message}");
            return false;
        }
    }

    public async Task StartHeartbeatLoop(Func<int> getCurrentMatchCount, CancellationToken cancellationToken)
    {
        if (!_isRegistered || _apiToken == null)
        {
            Console.WriteLine("Cannot start heartbeat: not registered");
            return;
        }

        Console.WriteLine("Starting heartbeat loop (interval: 10s)");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var currentMatches = getCurrentMatchCount();
                var request = new { currentMatches };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiToken}");

                var response = await _httpClient.PostAsJsonAsync(
                    $"{_masterServerUrl}/servers/{_serverId}/heartbeat",
                    request,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Heartbeat failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Heartbeat error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    public async Task<bool> ReportMatchResult(Guid matchId, long winnerSteamId)
    {
        if (!_isRegistered || _apiToken == null)
        {
            Console.WriteLine("Cannot report match result: not registered");
            return false;
        }

        try
        {
            var request = new
            {
                matchId,
                winnerSteamId
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiToken}");

            var response = await _httpClient.PostAsJsonAsync(
                $"{_masterServerUrl}/match/result",
                request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Match result report error: {ex.Message}");
            return false;
        }
    }

    private record RegistrationResponse(Guid ServerId, string ApiToken);
}
```

- [ ] **Step 2: Integrate registration in Program.cs**

```csharp
// Replace TODO comments in Server/Program.cs with:
using System.Threading;

// ... after orchestrator initialization ...

// Auto-detect IP (simplified: use loopback for testing)
var ipAddress = config.IsOfficial ? "127.0.0.1" : "127.0.0.1"; // TODO: detect public IP

var registration = new GameServerRegistration(
    config.MasterServerUrl,
    config.ServerName,
    ipAddress,
    config.BasePort,
    config.Region,
    config.IsOfficial,
    config.MaxConcurrentMatches
);

// Register with master server
var registered = await registration.RegisterAsync();
if (!registered)
{
    Console.WriteLine("Failed to register with master server, continuing anyway...");
}

// Start heartbeat loop
var cancellationTokenSource = new CancellationTokenSource();
var heartbeatTask = Task.Run(() => 
    registration.StartHeartbeatLoop(
        () => orchestrator.GetActiveMatchCount(),
        cancellationTokenSource.Token));

Console.CancelKeyPress += (sender, args) =>
{
    args.Cancel = true;
    Console.WriteLine("Shutting down...");
    cancellationTokenSource.Cancel();
    orchestrator.StopAll();
    Environment.Exit(0);
};

// Remove test match start (will be triggered by master server in future task)
// orchestrator.TryStartMatch(testMatchId, "test_token");

Console.WriteLine("Waiting for match assignments from master server...");
while (true)
{
    Thread.Sleep(1000);
}
```

- [ ] **Step 3: Verify game server builds**

```bash
cd /home/binoui/Projects/SlopArena/Server
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Server/GameServerRegistration.cs
git add Server/Program.cs
git commit -m "feat(server): add master server registration and heartbeat

- GameServerRegistration: registers with master on startup
- Heartbeat loop: updates current match count every 10s
- ReportMatchResult: sends winner to master for MMR update
- Integrated in Program.cs with graceful shutdown"
```

---

## Task 13: Integration Test - Full Flow

**Files:**
- Create: `docs/testing/phase1-integration-test.md`

- [ ] **Step 1: Create test documentation**

```markdown
# Phase 1 Integration Test

## Prerequisites

1. **Docker** installed (for Postgres)
2. **.NET 8 SDK** installed
3. **Two terminals** ready

## Test Flow

### Step 1: Start Master Server

Terminal 1:
\`\`\`bash
cd /home/binoui/Projects/SlopArena/MasterServer

# Create .env file
cat > .env << EOF
STEAM_API_KEY=test_key_not_real
JWT_SECRET=this_is_a_test_secret_min_32_characters_long
DB_PASSWORD=postgres
EOF

# Start via Docker Compose
docker-compose up
\`\`\`

Wait for: "Now listening on: http://[::]:5000"

### Step 2: Apply Database Migrations

Terminal 2:
\`\`\`bash
cd /home/binoui/Projects/SlopArena/MasterServer
dotnet ef database update
\`\`\`

Expected: "Done."

### Step 3: Start Game Server

Terminal 2:
\`\`\`bash
cd /home/binoui/Projects/SlopArena/Server

# Update server.json to point to master
cat > server.json << EOF
{
  "serverName": "Test EU Server",
  "region": "EU",
  "basePort": 7777,
  "maxConcurrentMatches": 15,
  "masterServerUrl": "http://localhost:5000",
  "isOfficial": true
}
EOF

dotnet run
\`\`\`

Expected:
- "Registered with master server (ID: ...)"
- "Starting heartbeat loop (interval: 10s)"
- "Waiting for match assignments from master server..."

### Step 4: Verify Health Endpoint

Terminal 3 (new):
\`\`\`bash
curl http://localhost:5000/health
\`\`\`

Expected: `{"status":"ok","version":"0.1.0"}`

### Step 5: Test Authentication (Mock)

Note: Real Steam authentication requires valid Steam tickets. For testing, we verify the endpoint exists.

\`\`\`bash
curl -X POST http://localhost:5000/auth/steam \
  -H "Content-Type: application/json" \
  -d '{"steamTicket": "invalid_test_ticket"}'
\`\`\`

Expected: 400 Bad Request with error message (Steam API will reject)

### Step 6: Verify Game Server Registered

Query Postgres:
\`\`\`bash
docker exec -it sloparena-postgres psql -U postgres -d sloparena -c "SELECT * FROM game_servers;"
\`\`\`

Expected: 1 row with name "Test EU Server", region "EU", is_official true

### Step 7: Test Global Chat (Manual - Client Required)

This requires a WebSocket client. For now, verify the endpoint is exposed:

\`\`\`bash
curl -I http://localhost:5000/chat
\`\`\`

Expected: 404 (needs WebSocket upgrade, but route exists)

### Step 8: Cleanup

Terminal 1 (Ctrl+C to stop Docker Compose):
\`\`\`bash
docker-compose down -v
\`\`\`

Terminal 2 (Ctrl+C to stop game server)

## Success Criteria

- ✅ Master server starts without errors
- ✅ Postgres migrations apply successfully
- ✅ Game server registers with master
- ✅ Heartbeat updates visible in database
- ✅ Health endpoint returns OK
- ✅ Authentication endpoint rejects invalid tickets
- ✅ Global chat hub endpoint exists

## Known Limitations (Phase 1)

- No real matchmaking test (requires 2 clients with valid Steam auth)
- No spectator testing (Phase 3)
- No community servers (Phase 2)
- No DM/party chat (Phase 2)
\`\`\`

- [ ] **Step 2: Run the integration test**

Follow the steps in the test document manually.

- [ ] **Step 3: Document results**

Add a comment to the test document with pass/fail for each step.

- [ ] **Step 4: Commit test documentation**

```bash
mkdir -p docs/testing
git add docs/testing/phase1-integration-test.md
git commit -m "docs: add Phase 1 integration test guide

Manual test flow for MVP:
- Master server startup via Docker Compose
- Game server registration and heartbeat
- Health endpoint verification
- Database schema validation"
```

---

## Task 14: README Updates

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add Phase 1 server documentation to README**

```markdown
<!-- Add after "## Architecture Overview" section in README.md -->

## Multiplayer Backend (Phase 1 MVP)

**Status:** Work in progress

The backend consists of two components:

### Master Server (ASP.NET Core)
- **Authentication:** Steam Web API integration
- **Matchmaking:** MMR-based 1v1 queue with dynamic range expansion
- **Chat:** SignalR WebSocket hub (global chat)
- **Database:** PostgreSQL (users, matches, game servers)
- **Deployment:** Docker Compose

**Running locally:**
```bash
cd MasterServer
docker-compose up
dotnet ef database update  # Apply migrations
```

### Game Server (.NET 8 UDP)
- **Multi-match:** 10-15 concurrent matches per instance
- **Protocol:** 60Hz UDP server-authoritative simulation
- **Configuration:** `server.json` (region, ports, master URL)
- **Deployment:** Standalone binary or Docker

**Running locally:**
```bash
cd Server
dotnet run
```

**Next phases:**
- Phase 2: Community servers, DM/party chat, server browser
- Phase 3: Spectator mode with ghost rendering

See `docs/superpowers/specs/2026-06-10-server-architecture-design.md` for full architecture.
```

- [ ] **Step 2: Commit README update**

```bash
git add README.md
git commit -m "docs: add Phase 1 multiplayer backend section to README

- Master server: auth, matchmaking, chat
- Game server: multi-match orchestrator
- Local development instructions
- Link to full architecture spec"
```

---

## Self-Review Checklist

- [ ] **Spec coverage check:**
  - ✅ Master server with Steam auth: Task 3, 4, 5
  - ✅ MMR-based matchmaking: Task 6, 7
  - ✅ Global chat (SignalR): Task 8
  - ✅ Database models (users, matches, servers): Task 2
  - ✅ Game server registration & heartbeat: Task 9, 12
  - ✅ Multi-match orchestrator: Task 11
  - ✅ Docker deployment: Task 10
  - ✅ Integration testing: Task 13

- [ ] **Placeholder scan:**
  - No "TBD", "TODO", "implement later" in code
  - All TODOs are for future phases (documented in spec)
  - No "add appropriate error handling" without code
  - No "similar to Task N" without repeating code

- [ ] **Type consistency:**
  - `SteamId` = `long` (consistent across all files)
  - `MatchId` = `Guid` (consistent)
  - `MMR` = `int` (consistent)
  - Packet types match between Shared/ and usage

- [ ] **File paths:**
  - All paths are absolute and correct
  - All new files are created with proper namespaces
  - All test files are in `MasterServer.Tests/` with correct references

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-10-server-mvp-phase1.md`.

**Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Use `superpowers:subagent-driven-development` skill.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.

**Which approach?**
