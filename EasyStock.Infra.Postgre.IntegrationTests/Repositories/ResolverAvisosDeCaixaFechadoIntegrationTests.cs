using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2258 (F1): o sino não mostra caixa que já fechou. Os avisos acham a abertura pelo <c>RefEntidadeId</c> do evento ou,
/// nos avisos antigos (sem ele), pelo <c>aberturaId</c> do payload; só os de abertura com fechamento posterior da mesma
/// empresa/loja viram lidos.
/// </summary>
public class ResolverAvisosDeCaixaFechadoIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Abriu = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task ResolveSoOsAvisosDeCaixaComFechamentoPosteriorDaMesmaLojaEEmpresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();
        var casa = Empresa.Criar("Casa caixa 2258", null);
        var outra = Empresa.Criar("Outra caixa 2258", null);
        db.Empresas.AddRange(casa, outra);
        var loja = Loja.Criar(casa.Id, "Loja A");
        var lojaB = Loja.Criar(casa.Id, "Loja B");
        db.Lojas.AddRange(loja, lojaB);
        var pessoa = Guid.NewGuid();

        // Fechado no dia seguinte: o aviso antigo (sem RefEntidadeId, só payload) e o PrazoEstourado (com chave) saem.
        var fechada = Abertura(db, casa.Id, Abriu, fechadaEm: Abriu.AddHours(30));
        var antigo = Aviso(db, casa.Id, pessoa, TipoEventoNotificacao.CaixaAbertoEsquecido, refEntidadeId: null,
            $$"""{"usuarioId":"{{pessoa}}","aberturaId":"{{fechada}}","valor_abertura":300.03}""", correlation: "abc");
        var novo = Aviso(db, casa.Id, pessoa, TipoEventoNotificacao.CaixaAbertoEsquecido, fechada, "{}", correlation: "def");
        var prazo = Aviso(db, casa.Id, pessoa, TipoEventoNotificacao.PrazoEstourado, fechada, "{}",
            PrazoEstouradoEvento.CorrelationId(TipoPrazo.CaixaEsquecido, fechada));

        // Segue aberto (sem fechamento): o aviso continua valendo. Fechamento de OUTRA loja não fecha este caixa.
        var aberta = Abertura(db, casa.Id, Abriu.AddDays(17), fechadaEm: null);
        var segue = Aviso(db, casa.Id, pessoa, TipoEventoNotificacao.CaixaAbertoEsquecido, aberta, "{}", "ghi");
        var outraLoja = Abertura(db, casa.Id, Abriu.AddDays(20), fechadaEm: null, lojaId: loja.Id, fechamentoDaLoja: lojaB.Id);
        var segueNaLoja = Aviso(db, casa.Id, pessoa, TipoEventoNotificacao.CaixaAbertoEsquecido, outraLoja, "{}", "jkl");

        // PrazoEstourado de outro tipo nunca é tocado, mesmo com id parecido com o de um caixa fechado.
        var pedido = Aviso(db, casa.Id, pessoa, TipoEventoNotificacao.PrazoEstourado, fechada, "{}",
            PrazoEstouradoEvento.CorrelationId(TipoPrazo.PedidoAtrasado, fechada));

        // Outra empresa: com o filtro por empresa fica de fora; sem filtro (job) também sai.
        var fechadaOutra = Abertura(db, outra.Id, Abriu, fechadaEm: Abriu.AddHours(5));
        var deOutra = Aviso(db, outra.Id, pessoa, TipoEventoNotificacao.CaixaAbertoEsquecido, fechadaOutra, "{}", "mno");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new NotificacaoRepository(db);

        (await repo.ResolverAvisosDeCaixaFechadoAsync(casa.Id)).Should().Be(3);
        (await Lidos(db, antigo, novo, prazo, segue, segueNaLoja, pedido, deOutra))
            .Should().Equal(true, true, true, false, false, false, false);

        (await repo.ResolverAvisosDeCaixaFechadoAsync()).Should().Be(1, "a rodada do job varre todas as empresas");
        (await Lidos(db, deOutra)).Should().Equal(true);

        (await repo.ResolverAvisosDeCaixaFechadoAsync()).Should().Be(0, "a segunda rodada não escreve nada");
    }

    private static async Task<bool[]> Lidos(EasyStockDbContext db, params Guid[] ids)
    {
        db.ChangeTracker.Clear();
        var lidos = await db.Notificacoes.IgnoreQueryFilters().Where(n => ids.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, n => n.Lida);
        return ids.Select(i => lidos[i]).ToArray();
    }

    /// <summary>Grava a abertura (e o fechamento posterior, se houver) e devolve o id da abertura.</summary>
    private static Guid Abertura(EasyStockDbContext db, Guid empresaId, DateTime em, DateTime? fechadaEm,
        Guid? lojaId = null, Guid? fechamentoDaLoja = null)
    {
        var abertura = MovimentoCaixa.Criar(empresaId, "abertura", 300.03m, em, lojaId);
        db.MovimentosCaixa.Add(abertura);
        if (fechadaEm is { } f)
            db.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "fechamento", 0m, f, lojaId));
        if (fechamentoDaLoja is { } outraLoja)
            db.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "fechamento", 0m, em.AddHours(3), outraLoja));
        return abertura.Id;
    }

    private static Guid Aviso(EasyStockDbContext db, Guid empresaId, Guid pessoa, TipoEventoNotificacao tipo,
        Guid? refEntidadeId, string payload, string correlation)
    {
        var evento = EventoNotificacao.Criar(tipo, empresaId, payload, refEntidadeId, correlation);
        var template = TemplateNotificacao.Criar($"caixa-{Guid.NewGuid():N}", "Teste", CanalNotificacao.InApp, tipo,
            "Caixa aberto", "Confira o caixa.", empresaId);
        var outbox = OutboxMensagemNotificacao.Criar(evento.Id, template.Id, empresaId, CanalNotificacao.InApp,
            pessoa.ToString(), "Caixa aberto", "Confira o caixa.", CategoriaConteudoNotificacao.Operacional);
        var aviso = Notificacao.Criar(empresaId, TipoAlertaEstoque.ValidadeProxima, "Caixa aberto", "Confira o caixa.",
            SeveridadeNotificacao.Media);
        aviso.OutboxMensagemId = outbox.Id;
        aviso.UsuarioId = pessoa;
        db.NotifEventos.Add(evento);
        db.NotifTemplates.Add(template);
        db.NotifOutboxMensagens.Add(outbox);
        db.Notificacoes.Add(aviso);
        return aviso.Id;
    }
}
