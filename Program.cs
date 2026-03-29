using Google.Cloud.Storage.V1;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using vision_backend.Application.Options;
using vision_backend.Application.Interfaces;
using vision_backend.Application.Services;
using vision_backend.Infrastructure.Data;
using vision_backend.Extensions;
using vision_backend.Infrastructure.Repositories;
using vision_backend.Infrastructure.Realtime;
using vision_backend.Infrastructure.Services;

// Configure QuestPDF community license
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Add user secrets support
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// Add services to the container.

// Force IST (UTC+5:30) — this is an India-only app.
// On Linux servers that default to UTC, this ensures DateTime.Now returns IST.
if (!OperatingSystem.IsWindows())
{
    Environment.SetEnvironmentVariable("TZ", "Asia/Kolkata");
}

// Use IST (local) timestamps throughout — this is an India-only app.
// Npgsql 6+ legacy mode allows DateTime.Now with timestamptz columns.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Configure Entity Framework Core with PostgreSQL
var connectionString = ResolveDatabaseConnectionString(builder.Configuration);
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("DefaultConnection is not configured.");
}
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

// Configure CORS from appsettings (overridable by env vars like Cors__AllowedOrigins__0)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddSignalR();

// Allow larger multipart payloads for voucher receipt uploads.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100L * 1024 * 1024; // 100 MB
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100L * 1024 * 1024; // 100 MB
});

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IVoucherRepository, VoucherRepository>();
builder.Services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();
builder.Services.AddScoped<ISiteRepository, SiteRepository>();
builder.Services.AddScoped<IVoucherCategoryRepository, VoucherCategoryRepository>();
builder.Services.AddScoped<IDesignationRepository, DesignationRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IVoucherService, VoucherService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ILeaveService, LeaveService>();
builder.Services.AddScoped<ISiteService, SiteService>();
builder.Services.AddScoped<IVoucherCategoryService, VoucherCategoryService>();
builder.Services.AddScoped<IDesignationService, DesignationService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<GcpStorageOptions>(builder.Configuration.GetSection("GcpStorage"));

// Storage mode selection:
// - If bucket config exists -> GCS storage
// - Else -> local storage fallback (keeps app usable in dev)
var storageOptions = builder.Configuration.GetSection("GcpStorage").Get<GcpStorageOptions>() ?? new GcpStorageOptions();
var hasGcsConfig = !string.IsNullOrWhiteSpace(storageOptions.BucketName);

if (hasGcsConfig)
{
    builder.Services.AddSingleton(_ =>
    {
        GoogleCredential credential;

        if (!string.IsNullOrWhiteSpace(storageOptions.ServiceAccountJsonBase64))
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(storageOptions.ServiceAccountJsonBase64));
            credential = GoogleCredential.FromJson(json);
        }
        else if (!string.IsNullOrWhiteSpace(storageOptions.ServiceAccountJson))
        {
            credential = GoogleCredential.FromJson(storageOptions.ServiceAccountJson);
        }
        else if (!string.IsNullOrWhiteSpace(storageOptions.ServiceAccountKeyPath))
        {
            credential = GoogleCredential.FromFile(storageOptions.ServiceAccountKeyPath);
        }
        else
        {
            credential = GoogleCredential.GetApplicationDefault();
        }

        if (credential.IsCreateScopedRequired)
        {
            credential = credential.CreateScoped("https://www.googleapis.com/auth/devstorage.full_control");
        }

        return credential;
    });

    builder.Services.AddSingleton(provider =>
    {
        var credential = provider.GetRequiredService<GoogleCredential>();
        return StorageClient.Create(credential);
    });

    builder.Services.AddSingleton<IStorageService, GcsStorageService>();
    Console.WriteLine($"[Storage] Using GCS bucket '{storageOptions.BucketName}' (local fallback: {storageOptions.EnableLocalStorage})");
}
else if (storageOptions.EnableLocalStorage)
{
    Console.WriteLine("[Storage] No GCS bucket configured. Using local storage under wwwroot/uploads.");
    builder.Services.AddSingleton<IStorageService, LocalStorageService>();
}
else
{
    throw new InvalidOperationException(
        "GcpStorage:BucketName is not configured and EnableLocalStorage is false. " +
        "Either configure a GCS bucket or enable local storage.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notifications"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey))
        };
    });
// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "vision-backend API", Version = "v1" });
    
    // Add JWT Bearer token authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter your JWT token in the format: Bearer <your_token>"
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    // Serve Swagger UI at application root ("/")
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "vision-backend v1");
        c.RoutePrefix = string.Empty; // -> serves UI at "/"
    });
}
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Vision API v1");
    c.RoutePrefix = "swagger"; // Access at /swagger
});

//app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationsHub>("/hubs/notifications");

// Auto-apply pending EF Core migrations on startup (safe - only runs pending ones)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

await app.Services.SeedInitialUsersAsync();

app.Run();

static string ResolveDatabaseConnectionString(IConfiguration configuration)
{
    var configuredConnection = configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(configuredConnection))
    {
        return configuredConnection.Trim();
    }

    var databaseUrl = configuration["DATABASE_URL"];
    if (string.IsNullOrWhiteSpace(databaseUrl))
    {
        return string.Empty;
    }

    databaseUrl = databaseUrl.Trim();

    if (databaseUrl.StartsWith("Host=", StringComparison.OrdinalIgnoreCase) ||
        databaseUrl.Contains(";", StringComparison.Ordinal))
    {
        return databaseUrl;
    }

    if (Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase)))
    {
        var userInfo = uri.UserInfo.Split(':', 2, StringSplitOptions.TrimEntries);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = uri.AbsolutePath.Trim('/');

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("DATABASE_URL is missing the database name in its path segment.");
        }

        return $"Host={uri.Host};Port={uri.Port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }

    throw new InvalidOperationException("DATABASE_URL is set but not in a supported PostgreSQL format.");
}

