using System.Text;
using System.Text.Json.Serialization;
using DiscordClone.Api.Hubs;
using DiscordClone.Api.Middleware;
using DiscordClone.Application.Storage;
using DiscordClone.Infrastructure;
using DiscordClone.Infrastructure.Auth;
using DiscordClone.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console());

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddInfrastructure(builder.Configuration);

var redisConnectionString = builder.Configuration["REDIS_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("REDIS_CONNECTION_STRING is not configured.");

builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddStackExchangeRedis(redisConnectionString);

var jwtOptions = JwtOptions.FromConfiguration(builder.Configuration);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var corsOriginsConfig = builder.Configuration["CORS_ORIGINS"];
// http://127.0.0.1:47823 is the fixed origin the packaged Electron desktop app serves
// itself from (see frontend/electron/main.cjs) — allowed by default so the desktop
// build can log in against a locally-run backend with no extra configuration.
var corsOrigins = (string.IsNullOrWhiteSpace(corsOriginsConfig)
        ? "http://localhost:5173,http://127.0.0.1:47823"
        : corsOriginsConfig)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var startupScope = app.Services.CreateScope())
{
    var mongo = startupScope.ServiceProvider.GetRequiredService<MongoContext>();
    await MongoIndexInitializer.EnsureIndexesAsync(mongo, CancellationToken.None);

    var storage = startupScope.ServiceProvider.GetRequiredService<IStorageService>();
    await storage.EnsureBucketExistsAsync(CancellationToken.None);
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
