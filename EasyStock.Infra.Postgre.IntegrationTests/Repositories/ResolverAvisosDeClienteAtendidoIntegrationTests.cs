using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2303: o aviso "Cliente sem resposta" sai do sino quando o lembrete que o gerou deixa de estar aberto (a dona
/// respondeu, a conversa fechou) ou some. O <c>RefEntidadeId</c> do evento é o id do lembrete.
/// </summary>
public class ResolverAvisosDeClienteAtendidoIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Criado = new(2026, 10, 28, 15, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task ResolveSoOsAvisosCujoLembreteNaoEstaMaisAberto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();
        var casa = Empresa.Criar("Casa cliente 2303", null);
        var outra = Empresa.Criar("Outra cliente 2303", null);
        db.Empresas.AddRange(casa, outra);
        var pessoa = Guid.NewGuid();

        var respondido = Lembrete(db, casa.Id, concluido: true);
        var resolvido = Aviso(db, casa.Id, pessoa, respondido, TipoPrazo.ClienteSemResposta);
        var esperando = Lembrete(db, casa.Id, concluido: false);
        var segue = Aviso(db, casa.Id, pessoa, esperando, TipoPrazo.ClienteSemResposta);
        // Lembrete apagado: não há mais o que atender.
        var sumiu = Aviso(db, casa.Id, pessoa, Guid.NewGuid(), TipoPrazo.ClienteSemResposta);
        // Outro prazo com o mesmo id nunca é tocado.
        var pedido = Aviso(db, casa.Id, pessoa, respondido, TipoPrazo.PedidoAtrasado);

        var respondidoOutra = Lembrete(db, outra.Id, concluido: true);
        var deOutra = Aviso(db, outra.Id, pessoa, respondidoOutra, TipoPrazo.ClienteSemResposta);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new NotificacaoRepository(db);

        (await repo.ResolverAvisosDeClienteAtendidoAsync(casa.Id)).Should().Be(2);
        (await Lidos(db, resolvido, segue, sumiu, pedido, deOutra)).Should().Equal(true, false, true, false, false);

        (await repo.ResolverAvisosDeClienteAtendidoAsync()).Should().Be(1, "a rodada do job varre todas as empresas");
        (await Lidos(db, deOutra)).Should().Equal(true);

        (await repo.ResolverAvisosDeClienteAtendidoAsync()).Should().Be(0, "a segunda rodada não escreve nada");
    }

    private static async Task<bool[]> Lidos(EasyStockDbContext db, params Guid[] ids)
    {
        db.ChangeTracker.Clear();
        var lidos = await db.Notificacoes.IgnoreQueryFilters().Where(n => ids.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, n => n.Lida);
        return ids.Select(i => lidos[i]).ToArray();
    }

    private static Guid Lembrete(EasyStockDbContext db, Guid empresaId, bool concluido)
    {
        var lembrete = Domain.Entities.Atendimento.Lembrete.Automatico(empresaId, TipoLembrete.ClienteSemResposta,
            Guid.NewGuid().ToString(), "Fulana está há 5 min sem resposta.", Criado);
        if (concluido) lembrete.Concluir(Criado.AddMinutes(20));
        db.Lembretes.Add(lembrete);
        return lembrete.Id;
    }

    private static Guid Aviso(EasyStockDbContext db, Guid empresaId, Guid pessoa, Guid lembreteId, TipoPrazo tipo)
    {
        var evento = EventoNotificacao.Criar(TipoEventoNotificacao.PrazoEstourado, empresaId, "{}", lembreteId,
            PrazoEstouradoEvento.CorrelationId(tipo, lembreteId));
        var template = TemplateNotificacao.Criar($"prazo-{Guid.NewGuid():N}", "Teste", CanalNotificacao.InApp,
            TipoEventoNotificacao.PrazoEstourado, "Cliente sem resposta", "Atraso de 5 minutos.", empresaId);
        var outbox = OutboxMensagemNotificacao.Criar(evento.Id, template.Id, empresaId, CanalNotificacao.InApp,
            pessoa.ToString(), "Cliente sem resposta", "Atraso de 5 minutos.", CategoriaConteudoNotificacao.Operacional);
        var aviso = Notificacao.Criar(empresaId, TipoAlertaEstoque.AvisoOperacional, "Cliente sem resposta",
            "Atraso de 5 minutos.", SeveridadeNotificacao.Media);
        aviso.OutboxMensagemId = outbox.Id;
        aviso.UsuarioId = pessoa;
        db.NotifEventos.Add(evento);
        db.NotifTemplates.Add(template);
        db.NotifOutboxMensagens.Add(outbox);
        db.Notificacoes.Add(aviso);
        return aviso.Id;
    }
}
