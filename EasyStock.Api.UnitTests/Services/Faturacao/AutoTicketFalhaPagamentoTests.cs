using EasyStock.Api.Services.Faturacao;
using EasyStock.Api.Services.Helpdesk;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.UnitTests.Services.Faturacao;

/// <summary>
/// Cobre o fluxo F14: registro de falha de pagamento + abertura automatica
/// de ticket Financeiro/Alta quando o limiar (default 3 falhas em 7 dias) e
/// atingido. Idempotente via Fatura.TicketRelacionadoId — uma fatura ja
/// vinculada nao gera ticket novo.
/// </summary>
public class AutoTicketFalhaPagamentoTests : IAsyncDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly HelpdeskTicketService _ticketService;

    public AutoTicketFalhaPagamentoTests()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"AutoTicketTests_{Guid.NewGuid()}")
            .Options;
        _db = new EasyStockDbContext(options);

        // SlaResolver real mas inacessivel — AbrirAsync e mockado e nao chama base.
        var sla = new SlaResolver(_db);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        var notificador = Substitute.For<INotificadorService>();

        _ticketService = Substitute.For<HelpdeskTicketService>(_db, currentUser, sla, notificador);
        _ticketService.AbrirAsync(Arg.Any<AbrirAdminTicketCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = (AbrirAdminTicketCommand)call[0];
                return Task.FromResult(new AdminTicket
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = cmd.EmpresaId,
                    Titulo = cmd.Titulo
                });
            });
    }

    private static IConfiguration BuildConfig(int? limiar = null, int? janelaDias = null)
    {
        var dict = new Dictionary<string, string?>();
        if (limiar.HasValue) dict["Faturacao:AutoTicketLimiar"] = limiar.Value.ToString();
        if (janelaDias.HasValue) dict["Faturacao:AutoTicketJanelaDias"] = janelaDias.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
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

    private async Task SemearFalhasAsync(Guid faturaId, int quantidade, DateTime? referencia = null)
    {
        var agora = referencia ?? DateTime.UtcNow;
        for (int i = 0; i < quantidade; i++)
        {
            var evento = FaturaEvento.Criar(
                faturaId,
                TipoEventoFatura.PagamentoFalhou,
                origem: "seed",
                valorDepois: $"falha-{i}");
            evento.OcorridoEm = agora.AddMinutes(-i);
            _db.FaturaEventos.Add(evento);
        }
        await _db.SaveChangesAsync();
    }

    private AutoTicketFalhaPagamento BuildSut(IConfiguration? config = null) =>
        new(_db, _ticketService, config ?? BuildConfig(), NullLogger<AutoTicketFalhaPagamento>.Instance);

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
        await _ticketService.DidNotReceive().AbrirAsync(Arg.Any<AbrirAdminTicketCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrarFalhaAsync_ignora_quando_FaturaId_Empty()
    {
        await BuildSut().RegistrarFalhaAsync(Guid.NewGuid(), faturaId: Guid.Empty, "qualquer");

        (await _db.FaturaEventos.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        await _ticketService.DidNotReceive().AbrirAsync(Arg.Any<AbrirAdminTicketCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrarFalhaAsync_ignora_quando_fatura_nao_encontrada()
    {
        await BuildSut().RegistrarFalhaAsync(Guid.NewGuid(), Guid.NewGuid(), "motivo qualquer");

        (await _db.FaturaEventos.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        await _ticketService.DidNotReceive().AbrirAsync(Arg.Any<AbrirAdminTicketCommand>(), Arg.Any<CancellationToken>());
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

    [Fact]
    public async Task RegistrarFalhaAsync_nao_cria_ticket_quando_abaixo_do_limiar_default()
    {
        // Default: limiar=3 em 7d. Pre-seed 1 falha + 1 nova = 2 < 3 -> sem ticket.
        var empresaId = Guid.NewGuid();
        var fatura = await InserirFaturaAsync(empresaId);
        await SemearFalhasAsync(fatura.Id, 1);

        await BuildSut().RegistrarFalhaAsync(empresaId, fatura.Id, "segunda falha");

        await _ticketService.DidNotReceive().AbrirAsync(Arg.Any<AbrirAdminTicketCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrarFalhaAsync_cria_ticket_quando_atinge_limiar_3_em_7d()
    {
        // Pre-seed 2 falhas + 1 nova = 3 >= 3 -> ticket Financeiro/Alta criado.
        var empresaId = Guid.NewGuid();
        var fatura = await InserirFaturaAsync(empresaId);
        await SemearFalhasAsync(fatura.Id, 2);

        await BuildSut().RegistrarFalhaAsync(empresaId, fatura.Id, "terceira falha");

        await _ticketService.Received(1).AbrirAsync(
            Arg.Is<AbrirAdminTicketCommand>(cmd =>
                cmd.EmpresaId == empresaId &&
                cmd.Categoria == TicketCategoria.Financeiro &&
                cmd.Prioridade == TicketPrioridade.Alta &&
                cmd.Nivel == NivelAtendimento.N2 &&
                cmd.FaturaId == fatura.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrarFalhaAsync_nao_cria_ticket_quando_fatura_ja_tem_ticket_relacionado()
    {
        var empresaId = Guid.NewGuid();
        var fatura = await InserirFaturaAsync(empresaId);
        fatura.TicketRelacionadoId = Guid.NewGuid();
        await _db.SaveChangesAsync();

        // Mesmo com 5 falhas, ja vinculada -> nao duplica.
        await SemearFalhasAsync(fatura.Id, 5);

        await BuildSut().RegistrarFalhaAsync(empresaId, fatura.Id, "sexta falha");

        await _ticketService.DidNotReceive().AbrirAsync(Arg.Any<AbrirAdminTicketCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrarFalhaAsync_conta_apenas_falhas_dentro_da_janela_de_7_dias()
    {
        var empresaId = Guid.NewGuid();
        var fatura = await InserirFaturaAsync(empresaId);

        // Seed manual: 5 falhas antigas (>7d atras) + 1 nova = 1 efetiva < 3.
        var agora = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            var evento = FaturaEvento.Criar(
                fatura.Id, TipoEventoFatura.PagamentoFalhou,
                origem: "seed", valorDepois: $"antiga-{i}");
            evento.OcorridoEm = agora.AddDays(-30 - i);
            _db.FaturaEventos.Add(evento);
        }
        await _db.SaveChangesAsync();

        await BuildSut().RegistrarFalhaAsync(empresaId, fatura.Id, "nova");

        await _ticketService.DidNotReceive().AbrirAsync(Arg.Any<AbrirAdminTicketCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrarFalhaAsync_respeita_limiar_customizado_via_configuration()
    {
        // Override pra limiar=2 em 7d. Pre-seed 1 + 1 nova = 2 >= 2 -> ticket.
        var empresaId = Guid.NewGuid();
        var fatura = await InserirFaturaAsync(empresaId);
        await SemearFalhasAsync(fatura.Id, 1);

        var config = BuildConfig(limiar: 2, janelaDias: 7);
        await BuildSut(config).RegistrarFalhaAsync(empresaId, fatura.Id, "segunda falha custom");

        await _ticketService.Received(1).AbrirAsync(
            Arg.Is<AbrirAdminTicketCommand>(cmd => cmd.FaturaId == fatura.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrarFalhaAsync_nao_propaga_excecao_quando_AbrirAsync_lanca()
    {
        var empresaId = Guid.NewGuid();
        var fatura = await InserirFaturaAsync(empresaId);
        await SemearFalhasAsync(fatura.Id, 2);

        // Forca AbrirAsync a lancar.
        _ticketService.AbrirAsync(Arg.Any<AbrirAdminTicketCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("Helpdesk down"));

        // Nao deve propagar — AutoTicketFalhaPagamento engole + loga.
        var act = () => BuildSut().RegistrarFalhaAsync(empresaId, fatura.Id, "terceira falha");
        await act.Should().NotThrowAsync();

        // Mas AbrirAsync foi chamado (limiar atingido).
        await _ticketService.Received(1).AbrirAsync(Arg.Any<AbrirAdminTicketCommand>(), Arg.Any<CancellationToken>());
    }

}
