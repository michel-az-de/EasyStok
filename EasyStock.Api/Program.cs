using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
