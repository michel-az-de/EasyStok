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

        // #3402: o envio antigo gravado como ValidadeProxima sai como AvisoOperacional, para o sino do Console não
        // oferecer o Estoque do dia a um aviso de caixa; o alerta real de validade, sem outbox, continua ValidadeProxima.
        var tiposEsperados = casos.Select(_ => TipoAlertaEstoque.AvisoOperacional).Append(TipoAlertaEstoque.ValidadeProxima).ToArray();

        var recentes = (await repo.GetRecentesNaoLidasAsync(casa.Id, 10, pessoa)).ToList();
        recentes.Select(n => n.Titulo).Should().Equal(esperados);
        recentes.Select(n => n.TipoAlerta).Should().Equal(tiposEsperados);
        for (var pagina = 1; pagina <= 3; pagina++)
        {
            var resultado = await repo.GetByEmpresaAsync(casa.Id, page: pagina, pageSize: 2, usuarioId: pessoa);
            resultado.TotalCount.Should().Be(6);
            resultado.Items.Select(n => n.Titulo).Should().Equal(esperados.Skip((pagina - 1) * 2).Take(2));
            resultado.Items.Select(n => n.TipoAlerta).Should().Equal(tiposEsperados.Skip((pagina - 1) * 2).Take(2));
        }
        // O filtro por tipo e o resumo seguem o mesmo tipo da lista: o envio antigo conta como aviso operacional.
        (await repo.GetByEmpresaAsync(casa.Id, tipo: TipoAlertaEstoque.ValidadeProxima, usuarioId: pessoa)).Items
            .Select(n => n.Id).Should().Equal(validade.Id);
        (await repo.GetByEmpresaAsync(casa.Id, tipo: TipoAlertaEstoque.AvisoOperacional, usuarioId: pessoa)).Items
            .Select(n => n.Id).Should().Equal(avisos.Take(casos.Length).Select(n => n.Id));
        (await repo.GetResumoAsync(casa.Id, pessoa)).PorTipo.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            [TipoAlertaEstoque.AvisoOperacional.ToString()] = casos.Length,
            [TipoAlertaEstoque.ValidadeProxima.ToString()] = 1,
        });
        db.ChangeTracker.Entries<Notificacao>().Should().BeEmpty();
        var registro = await db.Notificacoes.SingleAsync(n => n.Id == avisos[0].Id);
        registro.Titulo.Should().Be("Validade Próxima");
        registro.TipoAlerta.Should().Be(TipoAlertaEstoque.ValidadeProxima);
    }

    // #3402 (revisão da PR #3405): aviso novo e antigo do envio convivem com pedido do envio e alertas reais; só o
    // antigo muda de tipo, e o resumo soma os dois operacionais numa chave só.
    [SkippableFact]
    public async Task TipoExibidoSoReclassificaOEnvioAntigoNaListaNoFiltroENoResumo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();
        var casa = Empresa.Criar("Casa tipo exibido", null);
        db.Empresas.Add(casa);
        var pessoa = Guid.NewGuid();
        var minuto = 0;
        Notificacao DoEnvio(TipoAlertaEstoque tipo, string titulo, CategoriaConteudoNotificacao categoria)
        {
            var evento = EventoNotificacao.Criar(TipoEventoNotificacao.CaixaAbertoEsquecido, casa.Id, "{}");
            var template = TemplateNotificacao.Criar($"tipo-{Guid.NewGuid():N}", "Tipo exibido", CanalNotificacao.InApp,
                evento.Tipo, titulo, "corpo", casa.Id);
            var outbox = OutboxMensagemNotificacao.Criar(evento.Id, template.Id, casa.Id, CanalNotificacao.InApp,
                pessoa.ToString(), titulo, "corpo", categoria);
            db.NotifEventos.Add(evento);
            db.NotifTemplates.Add(template);
            db.NotifOutboxMensagens.Add(outbox);
            var aviso = Notificacao.Criar(casa.Id, tipo, titulo, "corpo", SeveridadeNotificacao.Media);
            aviso.OutboxMensagemId = outbox.Id;
            aviso.UsuarioId = pessoa;
            aviso.CriadaEm = DateTime.UtcNow.AddMinutes(-minuto++);
            return aviso;
        }
        var antigo = DoEnvio(TipoAlertaEstoque.ValidadeProxima, "Caixa aberto", CategoriaConteudoNotificacao.Operacional);
        var novo = DoEnvio(TipoAlertaEstoque.AvisoOperacional, "Prazo estourado", CategoriaConteudoNotificacao.Operacional);
        var pedido = DoEnvio(TipoAlertaEstoque.PedidoRecebido, "Pedido chegou", CategoriaConteudoNotificacao.Transacional);
        var validade = Notificacao.Criar(casa.Id, TipoAlertaEstoque.ValidadeProxima, "Validade Próxima", "L-1 vence", SeveridadeNotificacao.Alta);
        validade.CriadaEm = DateTime.UtcNow.AddMinutes(-minuto++);
        var critico = Notificacao.Criar(casa.Id, TipoAlertaEstoque.EstoqueCritico, "Estoque Crítico", "abaixo", SeveridadeNotificacao.Alta);
        critico.CriadaEm = DateTime.UtcNow.AddMinutes(-minuto++);
        db.Notificacoes.AddRange(antigo, novo, pedido, validade, critico);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.SetMobileTenantContext(casa.Id);
        var repo = new NotificacaoRepository(db);

        (await repo.GetRecentesNaoLidasAsync(casa.Id, 10, pessoa)).Select(n => n.TipoAlerta).Should().Equal(
            TipoAlertaEstoque.AvisoOperacional, TipoAlertaEstoque.AvisoOperacional, TipoAlertaEstoque.PedidoRecebido,
            TipoAlertaEstoque.ValidadeProxima, TipoAlertaEstoque.EstoqueCritico);
        async Task<Guid[]> Ids(TipoAlertaEstoque tipo) =>
            (await repo.GetByEmpresaAsync(casa.Id, tipo: tipo, usuarioId: pessoa)).Items.Select(n => n.Id).ToArray();
        (await Ids(TipoAlertaEstoque.AvisoOperacional)).Should().Equal(antigo.Id, novo.Id);
        (await Ids(TipoAlertaEstoque.ValidadeProxima)).Should().Equal(validade.Id);
        (await Ids(TipoAlertaEstoque.ProximoVencimento)).Should().Equal(validade.Id);
        (await Ids(TipoAlertaEstoque.PedidoRecebido)).Should().Equal(pedido.Id);
        (await Ids(TipoAlertaEstoque.EstoqueCritico)).Should().Equal(critico.Id);
        var resumo = await repo.GetResumoAsync(casa.Id, pessoa);
        resumo.PorTipo.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            [TipoAlertaEstoque.AvisoOperacional.ToString()] = 2,
            [TipoAlertaEstoque.ValidadeProxima.ToString()] = 1,
            [TipoAlertaEstoque.PedidoRecebido.ToString()] = 1,
            [TipoAlertaEstoque.EstoqueCritico.ToString()] = 1,
        });
        resumo.TotalNaoLidas.Should().Be(5);
        (await db.Notificacoes.AsNoTracking().SingleAsync(n => n.Id == antigo.Id)).TipoAlerta.Should().Be(TipoAlertaEstoque.ValidadeProxima);
    }
}
