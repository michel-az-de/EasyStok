using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using Microsoft.EntityFrameworkCore;
using Serilog;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
builder.Services.AddDbContext<EasyStockDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Repositories
builder.Services.AddScoped<IProdutoRepository, ProdutoRepository>();
builder.Services.AddScoped<ICategoriaRepository, CategoriaRepository>();
builder.Services.AddScoped<IEmpresaRepository, EmpresaRepository>();
builder.Services.AddScoped<IItemEstoqueRepository, ItemEstoqueRepository>();
builder.Services.AddScoped<IVendaRepository, VendaRepository>();
builder.Services.AddScoped<IItemVendaRepository, ItemVendaRepository>();
builder.Services.AddScoped<IMovimentacaoEstoqueRepository, MovimentacaoEstoqueRepository>();
builder.Services.AddScoped<IProdutoVariacaoRepository, ProdutoVariacaoRepository>();
builder.Services.AddScoped<IProdutoCaracteristicaRepository, ProdutoCaracteristicaRepository>();
builder.Services.AddScoped<IProdutoEmbalagemRepository, ProdutoEmbalagemRepository>();

// Unit of Work
builder.Services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<EasyStockDbContext>());

 // Use Cases
builder.Services.AddScoped<CadastrarProdutoUseCase>();
builder.Services.AddScoped<EasyStock.Application.UseCases.RegistrarEntradaEstoque.RegistrarEntradaEstoqueUseCase>();
builder.Services.AddScoped<EasyStock.Application.UseCases.RegistrarSaidaEstoque.RegistrarSaidaEstoqueUseCase>();
builder.Services.AddScoped<EasyStock.Application.UseCases.ReporEstoque.ReporEstoqueUseCase>();

// Observability
builder.Services.AddSingleton<EasyStock.Api.Observability.MetricsService>();

// Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!, name: "PostgreSQL");

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
app.UseAuthorization();
app.UseExceptionHandler();
app.MapControllers();

// Health Check endpoint
app.MapHealthChecks("/health");

app.Run();
