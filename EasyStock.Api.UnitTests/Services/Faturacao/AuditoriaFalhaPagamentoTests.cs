using EasyStock.Api.Services.Faturacao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Services.Faturacao;

/// <summary>
/// Cobre o registro de falha de pagamento em FaturaEvento (F14). A abertura
/// automatica de ticket saiu com o helpdesk (P03, #1116).
/// </summary>
public class AuditoriaFalhaPagamentoTests : IAsyncDisposable
{
    private readonly EasyStockDbContext _db;

    public AuditoriaFalhaPagamentoTests()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"AuditoriaFalhaTests_{Guid.NewGuid()}")
            .Options;
        _db = new EasyStockDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    private static Fatura SeedFatura(Guid empresaId, string numero = "2026-000001")
    {
        return Fatura.Criar(
            empresaId: empresaId,
            numero: numero,
            dadosFaturado: new DadosFaturado("Cliente Teste"),
            dadosEmissor: new DadosEmissor("Empresa Teste"),
            origem: OrigemFatura.Assinatura,
            dataEmissao: DateTime.UtcNow,
            dataVencimento: DateTime.UtcNow.AddDays(30));
    }

    private async Task<Fatura> InserirFaturaAsync(Guid empresaId)
    {
        var fatura = SeedFatura(empresaId);
        _db.Faturas.Add(fatura);
        await _db.SaveChangesAsync();
        return fatura;
    }

    private AuditoriaFalhaPagamento BuildSut() =>
        new(_db, NullLogger<AuditoriaFalhaPagamento>.Instance);

    [Fact]
    public async Task RegistrarFalhaAsync_audita_falha_em_FaturaEvento_quando_fatura_existe()
    {
        var empresaId = Guid.NewGuid();
        var fatura = await InserirFaturaAsync(empresaId);

        await BuildSut().RegistrarFalhaAsync(empresaId, fatura.Id, "Cartao recusado");

        var eventos = await _db.FaturaEventos.IgnoreQueryFilters()
            .Where(e => e.FaturaId == fatura.Id).ToListAsync();
        eventos.Should().HaveCount(1);
        eventos[0].Tipo.Should().Be(TipoEventoFatura.PagamentoFalhou);
        eventos[0].ValorDepois.Should().Be("Cartao recusado");
        eventos[0].Origem.Should().Be("auto-ticket");
    }

    [Fact]
    public async Task RegistrarFalhaAsync_ignora_quando_FaturaId_nulo()
    {
        await BuildSut().RegistrarFalhaAsync(Guid.NewGuid(), faturaId: null, "qualquer");

        (await _db.FaturaEventos.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RegistrarFalhaAsync_ignora_quando_FaturaId_Empty()
    {
        await BuildSut().RegistrarFalhaAsync(Guid.NewGuid(), faturaId: Guid.Empty, "qualquer");

        (await _db.FaturaEventos.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RegistrarFalhaAsync_ignora_quando_fatura_nao_encontrada()
    {
        await BuildSut().RegistrarFalhaAsync(Guid.NewGuid(), Guid.NewGuid(), "motivo qualquer");

        (await _db.FaturaEventos.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RegistrarFalhaAsync_substitui_motivo_vazio_por_placeholder()
    {
        var empresaId = Guid.NewGuid();
        var fatura = await InserirFaturaAsync(empresaId);

        await BuildSut().RegistrarFalhaAsync(empresaId, fatura.Id, motivo: "   ");

        var evento = await _db.FaturaEventos.IgnoreQueryFilters()
            .FirstAsync(e => e.FaturaId == fatura.Id);
        evento.ValorDepois.Should().Be("(sem motivo)");
    }
}
