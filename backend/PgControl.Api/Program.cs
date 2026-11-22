using Octokit.Webhooks;
using Octokit.Webhooks.AspNetCore;
using PgControl.Api.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ─── Loglama ─────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ─── Xidmətlər ───────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<PatroniService>();
builder.Services.AddScoped<PostgresMetricsService>();
builder.Services.AddScoped<PgBackRestService>();
builder.Services.AddScoped<FlywayService>();
builder.Services.AddScoped<RestoreJobService>();
builder.Services.AddHealthChecks();

// CORS — Angular dev server üçün
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("DevCors", p => p
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// JWT — Phase 0 üçün sadə, Phase 1-də tam
var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev-only-key-change-in-prod-min-32-chars!!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "pg-control";
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens
                .SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<MigrationScriptService>();


// ─── Pipeline ────────────────────────────────────────────
var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("DevCors");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.MapGet("/", () => new { name = "PG Control Plane API", version = "0.1.0" });

app.Run();