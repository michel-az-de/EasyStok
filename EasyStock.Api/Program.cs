using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using EasyStock.Api.Services;
using EasyStock.Application.Validators;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.GerenciarCategoria;
using EasyStock.Application.UseCases.GerenciarFornecedor;
using EasyStock.Application.UseCases.GerenciarLoja;
using EasyStock.Application.UseCases.GerenciarUsuario;
using EasyStock.Application.UseCases.ListarPlanos;
using EasyStock.Application.UseCases.RegistrarEmpresa;
using EasyStock.Infra.MongoDb.DependencyInjection;
using EasyStock.Infra.MongoDb.HealthChecks;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProcessId()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddEndpointsApiExplorer();

// Swagger com suporte a JWT Bearer
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "EasyStock API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Formato: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Database
var databaseProvider = builder.Configuration["Database:Provider"] ?? "PostgreSql";
var postgresConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var mongoConnectionString = builder.Configuration.GetConnectionString("MongoConnection");
var mongoDatabaseName = builder.Configuration["Database:MongoDatabase"] ?? "EasyStockDbMongo";

switch (databaseProvider.Trim().ToLowerInvariant())
{
    case "mongodb":
    case "mongo":
        if (string.IsNullOrWhiteSpace(mongoConnectionString))
            throw new InvalidOperationException("Connection string 'MongoConnection' nao configurada.");

        builder.Services.AddEasyStockMongoInfrastructure(mongoConnectionString, mongoDatabaseName, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddCheck<MongoDatabaseHealthCheck>("MongoDB");
        break;

    case "postgres":
    case "postgresql":
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
            throw new InvalidOperationException("Connection string 'DefaultConnection' nao configurada.");

        builder.Services.AddEasyStockPostgreInfrastructure(postgresConnectionString, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddNpgSql(postgresConnectionString, name: "PostgreSQL");
        break;

    default:
        throw new InvalidOperationException($"Database:Provider '{databaseProvider}' nao suportado.");
}

// Use Cases
builder.Services.AddScoped<AutenticarUsuarioUseCase>();
builder.Services.AddScoped<RegistrarEmpresaUseCase>();
builder.Services.AddScoped<GerenciarUsuarioUseCase>();
builder.Services.AddScoped<GerenciarLojaUseCase>();
builder.Services.AddScoped<GerenciarFornecedorUseCase>();
builder.Services.AddScoped<CadastrarProdutoUseCase>();
builder.Services.AddScoped<EasyStock.Application.UseCases.RegistrarEntradaEstoque.RegistrarEntradaEstoqueUseCase>();
builder.Services.AddScoped<EasyStock.Application.UseCases.RegistrarSaidaEstoque.RegistrarSaidaEstoqueUseCase>();
builder.Services.AddScoped<EasyStock.Application.UseCases.ReporEstoque.ReporEstoqueUseCase>();
builder.Services.AddScoped<EasyStock.Application.UseCases.BuscarEstoqueInteligente.BuscarEstoqueInteligenteUseCase>();
builder.Services.AddScoped<EasyStock.Application.UseCases.GerarSugestaoDescricaoAnuncio.GerarSugestaoDescricaoAnuncioUseCase>();
builder.Services.AddScoped<GerenciarCategoriaUseCase>();
builder.Services.AddScoped<ListarPlanosUseCase>();

// Configuration
builder.Services.Configure<EasyStockConfiguracoes>(
    builder.Configuration.GetSection("EasyStock"));

// Observability
builder.Services.AddSingleton<EasyStock.Api.Observability.MetricsService>();
builder.Services.AddProblemDetails();

// Authentication / Authorization
var jwtKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey nao configurado.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("Admin",    p => p.RequireClaim("nivel", "SuperAdmin", "Admin"));
    opts.AddPolicy("Gerente",  p => p.RequireClaim("nivel", "SuperAdmin", "Admin", "Gerente"));
    opts.AddPolicy("Operador", p => p.RequireClaim("nivel", "SuperAdmin", "Admin", "Gerente", "Operador"));
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (allowedOrigins is { Length: > 0 })
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        else
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("ai", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 2;
    });
    options.AddFixedWindowLimiter("geral", limiter =>
    {
        limiter.PermitLimit = 200;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 20;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Background Services
builder.Services.AddHostedService<AnalisadorEstoqueBackgroundService>();

// Exception Handler
builder.Services.AddExceptionHandler<EasyStock.Api.Observability.GlobalExceptionHandler>();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("EasyStock.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        })
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        })
        .AddConsoleExporter());

builder.Services.AddMemoryCache();

builder.Services.AddValidatorsFromAssemblyContaining<CadastrarProdutoCommandValidator>();

var app = builder.Build();

// Middleware for Correlation ID
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(correlationId))
    {
        correlationId = Guid.NewGuid().ToString();
    }
    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;
    await next();
});

// Serilog Request Logging
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.Items["CorrelationId"]);
        diagnosticContext.Set("Endpoint", httpContext.Request.Path);
        diagnosticContext.Set("MetodoHttp", httpContext.Request.Method);
    };
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseExceptionHandler();
app.MapControllers();

// Health Check endpoint
app.MapHealthChecks("/health");

app.Run();

public partial class Program
{
}
