using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class NotificacaoTituloIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task ListasRecuperamAssuntoLegadoSemAlterarRegistroNemMisturarTenantOuCanal()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();
        var casa = Empresa.Criar("Casa títulos", null);
        var outra = Empresa.Criar("Outra títulos", null);
        db.Empresas.AddRange(casa, outra);
        var pessoa = Guid.NewGuid();
        (Guid Empresa, CanalNotificacao Canal, string Assunto, string Titulo, string Esperado)[] casos =
        {
            (casa.Id, CanalNotificacao.InApp, "Caixa aberto desde 11/10/2026", "Validade Próxima", "Caixa aberto desde 11/10/2026"),
            (casa.Id, CanalNotificacao.InApp, "Título do envio", "Título atual", "Título atual"),
            (casa.Id, CanalNotificacao.InApp, "  ", "Validade Próxima", "Validade Próxima"),
            (outra.Id, CanalNotificacao.InApp, "Assunto de outra empresa", "Validade Próxima", "Validade Próxima"),
            (casa.Id, CanalNotificacao.Email, "Assunto de email", "Validade Próxima", "Validade Próxima"),
        };
        var avisos = new List<Notificacao>();
        foreach (var caso in casos)
        {
            var evento = EventoNotificacao.Criar(TipoEventoNotificacao.CaixaAbertoEsquecido, caso.Empresa, "{}");
            var template = TemplateNotificacao.Criar($"titulo-{Guid.NewGuid():N}", "Teste de título", caso.Canal,
                evento.Tipo, caso.Assunto, "Confira o caixa.", caso.Empresa);
            var outbox = OutboxMensagemNotificacao.Criar(evento.Id, template.Id, caso.Empresa, caso.Canal,
                pessoa.ToString(), caso.Assunto, "Confira o caixa.", CategoriaConteudoNotificacao.Operacional);
            var aviso = Notificacao.Criar(casa.Id, TipoAlertaEstoque.ValidadeProxima, caso.Titulo,
                "Confira o caixa.", SeveridadeNotificacao.Media);
            aviso.OutboxMensagemId = outbox.Id;
            aviso.UsuarioId = pessoa;
            aviso.CriadaEm = DateTime.UtcNow.AddMinutes(-avisos.Count);
            db.NotifEventos.Add(evento);
            db.NotifTemplates.Add(template);
            db.NotifOutboxMensagens.Add(outbox);
            avisos.Add(aviso);
        }
        // O alerta real de validade, sem outbox, conserva seu título.
        var validade = Notificacao.Criar(casa.Id, TipoAlertaEstoque.ValidadeProxima, "Confira a validade.");
        validade.CriadaEm = DateTime.UtcNow.AddMinutes(-10);
        avisos.Add(validade);
        db.Notificacoes.AddRange(avisos);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.SetMobileTenantContext(casa.Id);
        var repo = new NotificacaoRepository(db);
        var esperados = casos.Select(c => c.Esperado).Append("Validade Próxima").ToArray();

        (await repo.GetRecentesNaoLidasAsync(casa.Id, 10, pessoa)).Select(n => n.Titulo).Should().Equal(esperados);
        for (var pagina = 1; pagina <= 3; pagina++)
        {
            var resultado = await repo.GetByEmpresaAsync(casa.Id, page: pagina, pageSize: 2, usuarioId: pessoa);
            resultado.TotalCount.Should().Be(6);
            resultado.Items.Select(n => n.Titulo).Should().Equal(esperados.Skip((pagina - 1) * 2).Take(2));
        }
        db.ChangeTracker.Entries<Notificacao>().Should().BeEmpty();
        (await db.Notificacoes.SingleAsync(n => n.Id == avisos[0].Id)).Titulo.Should().Be("Validade Próxima");
    }
}
