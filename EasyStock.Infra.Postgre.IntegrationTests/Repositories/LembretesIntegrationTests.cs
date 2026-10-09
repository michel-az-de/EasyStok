using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Atendimento.Lembretes;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Options;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Lembretes da dona (S43) em Postgres real: as consultas do avaliador (15 min em
/// <c>AguardandoPagamento</c>, 10 min sem resposta numa conversa assumida), a idempotência com o
/// avaliador rodando três vezes, a resolução sozinha quando a dona responde e a RLS da tabela nova.
/// </summary>
public class LembretesIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task MigrationSlaAdicionaPadraoERetiraColunaPreservandoConfiguracao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();
        await db.Database.OpenConnectionAsync();
        // A tabela temporária limita o Up/Down real da migration a esta conexão.
        await db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE configuracoes_atendimento (\"EmpresaId\" uuid PRIMARY KEY, \"Tom\" text NOT NULL)");
        var empresa = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO pg_temp.configuracoes_atendimento (\"EmpresaId\", \"Tom\") VALUES ({empresa}, 'preservado')");
        var migration = new EasyStock.Infra.Postgre.Migrations.AddSlaRespostaConfiguracaoAtendimento();
        var gerador = db.GetService<IMigrationsSqlGenerator>();
        foreach (var sql in gerador.Generate(migration.UpOperations))
            await db.Database.ExecuteSqlRawAsync(sql.CommandText);
        (await db.Database.SqlQueryRaw<int>("SELECT \"SlaRespostaMinutos\" AS \"Value\" FROM pg_temp.configuracoes_atendimento").SingleAsync()).Should().Be(5);
        foreach (var sql in gerador.Generate(migration.DownOperations))
            await db.Database.ExecuteSqlRawAsync(sql.CommandText);
        (await db.Database.SqlQueryRaw<string>("SELECT \"Tom\" AS \"Value\" FROM pg_temp.configuracoes_atendimento").SingleAsync()).Should().Be("preservado");
        foreach (var sql in gerador.Generate(migration.UpOperations))
            await db.Database.ExecuteSqlRawAsync(sql.CommandText);
        (await db.Database.SqlQueryRaw<int>("SELECT \"SlaRespostaMinutos\" AS \"Value\" FROM pg_temp.configuracoes_atendimento").SingleAsync()).Should().Be(5);
    }

    [SkippableFact]
    public async Task SlaPersistePorEmpresa_PausaDuranteANoite_EFalhaDeEnvioNaoResponde()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var entradaEm = DateTimeOffset.Parse("2026-10-09T21:58:00-03:00").UtcDateTime;
        var casa = Empresa.Criar("SLA expediente", null);
        var outra = Empresa.Criar("SLA expediente outra", null);
        var conversa = Conversa.Abrir(casa.Id, "5511999990099", entradaEm, "Cliente SLA");
        conversa.RegistrarEntrada(entradaEm);
        conversa.Assumir(entradaEm, Guid.NewGuid());
        var entrada = Mensagem.Entrada(casa.Id, conversa.Id, entradaEm, TipoConteudoMensagem.Texto, "Tem nhoque?");
        var falha = Mensagem.Saida(casa.Id, conversa.Id, AutorMensagem.Dona, entradaEm.AddSeconds(1), TipoConteudoMensagem.Texto, "Tem sim");
        falha.RegistrarFalhaEnvio("Canal indisponível", TipoFalhaEnvio.Permanente, entradaEm.AddSeconds(1));
        var nota = Mensagem.Saida(casa.Id, conversa.Id, AutorMensagem.Sistema, entradaEm.AddSeconds(2), TipoConteudoMensagem.Texto, "Nota interna");
        conversa.RegistrarSaida(nota.EnviadaEm);
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.AddRange(casa, outra);
            db.ExpedientesLoja.Add(ExpedienteLoja.CriarPadrao(casa.Id));
            db.ConfiguracoesAtendimento.Add(ConfiguracaoAtendimento.CriarPadrao(outra.Id));
            db.AtendimentoConversas.Add(conversa);
            db.AtendimentoMensagens.AddRange(entrada, falha, nota);
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(casa.Id);
            var config = new ConfiguracaoAtendimentoRepository(db);
            await new AtualizarConfiguracaoAtendimentoUseCase(config, db).ExecuteAsync(
                new AtualizarConfiguracaoAtendimentoCommand(casa.Id, null, null, null, null, null, null, null, null, null, 5));
        }
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(casa.Id);
            var config = new ConfiguracaoAtendimentoRepository(db);
            (await config.GetByEmpresaIdAsync(casa.Id))!.SlaRespostaMinutos.Should().Be(5);
            (await config.GetByEmpresaIdAsync(outra.Id)).Should().BeNull();
            var inbox = new ListarConversasAtendimentoUseCase(new ConversaRepository(db), config, Options.Create(new PrazosOptions()));
            var resumo = (await inbox.ExecuteAsync(new ListarConversasAtendimentoQuery(casa.Id, null, null, 1, 10))).Single();
            resumo.AguardaResposta.Should().BeTrue("falha de envio e nota não respondem ao cliente");
            resumo.SlaRespostaMinutos.Should().Be(5);
        }
        await RodarAvaliadorAsync(DateTimeOffset.Parse("2026-10-09T23:30:00-03:00").UtcDateTime);
        await RodarAvaliadorAsync(DateTimeOffset.Parse("2026-10-10T08:03:00-03:00").UtcDateTime);
        await using (var db = fixture.CreateDbContext())
            (await db.Lembretes.IgnoreQueryFilters().CountAsync(l => l.EmpresaId == casa.Id)).Should().Be(0);
        await RodarAvaliadorAsync(DateTimeOffset.Parse("2026-10-10T08:03:01-03:00").UtcDateTime);
        await using (var db = fixture.CreateDbContext())
        {
            var aviso = await db.Lembretes.IgnoreQueryFilters().SingleAsync(l => l.EmpresaId == casa.Id);
            aviso.AvisadoEm.Should().NotBeNull();
            aviso.ConversaId.Should().Be(conversa.Id);
            db.AtendimentoMensagens.Add(Mensagem.Saida(casa.Id, conversa.Id, AutorMensagem.Dona,
                DateTimeOffset.Parse("2026-10-10T08:04:00-03:00").UtcDateTime, TipoConteudoMensagem.Texto, "Tem sim", "resposta-sla"));
            await db.SaveChangesAsync();
        }
        await RodarAvaliadorAsync(DateTimeOffset.Parse("2026-10-10T08:05:00-03:00").UtcDateTime);
        await using (var db = fixture.CreateDbContext())
            (await db.Lembretes.IgnoreQueryFilters().SingleAsync(l => l.EmpresaId == casa.Id)).EstaAberto.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Manual_PersisteVistoEConclusaoSemAlterarOutraEmpresaOuDestinatario()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc);
        var casa = Empresa.Criar("Casa lembretes manuais", null);
        var outra = Empresa.Criar("Outra casa lembretes", null);
        var pessoa = Guid.NewGuid();
        var colega = Guid.NewGuid();
        var meu = Lembrete.Manual(casa.Id, "Meu", agora, pessoa, agora, paraUsuarioId: pessoa);
        var equipe = Lembrete.Manual(casa.Id, "Equipe", agora, pessoa, agora);
        var alheio = Lembrete.Manual(casa.Id, "Colega", agora, colega, agora, paraUsuarioId: colega);
        var externo = Lembrete.Manual(outra.Id, "Outra empresa", agora, pessoa, agora);
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.AddRange(casa, outra);
            db.Lembretes.AddRange(meu, equipe, alheio, externo);
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(casa.Id);
            var repo = new LembreteRepository(db);
            var lista = await new ListarLembretesUseCase(repo).ExecuteAsync(casa.Id, pessoa, false, false);
            lista.Select(l => l.Id).Should().BeEquivalentTo([meu.Id, equipe.Id]);
            (await new MarcarLembretesVistosUseCase(repo, db, new RelogioFixo(agora)).ExecuteAsync(casa.Id, pessoa)).Should().Be(2);
            var concluir = new ConcluirLembreteUseCase(repo, db, new RelogioFixo(agora));
            var invadir = () => concluir.ExecuteAsync(casa.Id, pessoa, alheio.Id);
            await invadir.Should().ThrowAsync<LembreteNaoEncontradoException>();
            var invadirEmpresa = () => concluir.ExecuteAsync(casa.Id, pessoa, externo.Id);
            await invadirEmpresa.Should().ThrowAsync<LembreteNaoEncontradoException>();
            await concluir.ExecuteAsync(casa.Id, pessoa, meu.Id);
        }
        await using (var db = fixture.CreateDbContext())
        {
            var gravados = await db.Lembretes.IgnoreQueryFilters().Where(l => l.EmpresaId == casa.Id || l.EmpresaId == outra.Id).ToListAsync();
            gravados.Single(l => l.Id == meu.Id).ConcluidoPorUsuarioId.Should().Be(pessoa);
            gravados.Single(l => l.Id == equipe.Id).VistoEm.Should().NotBeNull();
            gravados.Where(l => l.Id == alheio.Id || l.Id == externo.Id).Should().OnlyContain(l => l.VistoEm == null && l.ConcluidoEm == null);
        }
    }

    [SkippableFact]
    public async Task AvaliadorCriaUmPorFatoResolveAoResponderETemRls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var agora = new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc);
        var empresa = Empresa.Criar("Casa da Baba Lembretes", "11222333000181");
        var parado = NovoPedido(empresa.Id, StatusPedidoMapper.AguardandoPagamento, agora.AddMinutes(-16));
        var recente = NovoPedido(empresa.Id, StatusPedidoMapper.AguardandoPagamento, agora.AddMinutes(-5));
        var pago = NovoPedido(empresa.Id, StatusPedidoMapper.Aguardando, agora.AddMinutes(-30));
        var atendente = Guid.NewGuid();

        // Assumida, cliente esperando há 11 min; a nota interna do sistema depois dele não é resposta.
        var esperando = Conversa.Abrir(empresa.Id, "5511999990001", agora.AddMinutes(-20));
        esperando.Assumir(agora.AddMinutes(-19), atendente);
        esperando.DefinirPedidoEmAndamento(parado.Id);
        var entradaEsperando = Mensagem.Entrada(empresa.Id, esperando.Id, agora.AddMinutes(-11), TipoConteudoMensagem.Texto, "oi?");
        var notaInterna = Mensagem.Saida(empresa.Id, esperando.Id, AutorMensagem.Sistema, agora.AddMinutes(-10.5), TipoConteudoMensagem.Texto, "escalado");

        // Assumida e respondida pela dona; automática; e assumida com o cliente esperando há pouco.
        var respondida = Conversa.Abrir(empresa.Id, "5511999990002", agora.AddMinutes(-20));
        respondida.Assumir(agora.AddMinutes(-19), atendente);
        var entradaRespondida = Mensagem.Entrada(empresa.Id, respondida.Id, agora.AddMinutes(-12), TipoConteudoMensagem.Texto, "oi");
        var respostaDona = Mensagem.Saida(empresa.Id, respondida.Id, AutorMensagem.Dona, agora.AddMinutes(-11), TipoConteudoMensagem.Texto, "já vou", "resposta-confirmada");
        var automatica = Conversa.Abrir(empresa.Id, "5511999990003", agora.AddMinutes(-30));
        var entradaAutomatica = Mensagem.Entrada(empresa.Id, automatica.Id, agora.AddMinutes(-20), TipoConteudoMensagem.Texto, "oi");
        var poucoTempo = Conversa.Abrir(empresa.Id, "5511999990004", agora.AddMinutes(-5));
        poucoTempo.Assumir(agora.AddMinutes(-4), atendente);
        var entradaPoucoTempo = Mensagem.Entrada(empresa.Id, poucoTempo.Id, agora.AddMinutes(-3), TipoConteudoMensagem.Texto, "oi");

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.Add(empresa);
            db.Pedidos.AddRange(parado, recente, pago);
            db.AtendimentoConversas.AddRange(esperando, respondida, automatica, poucoTempo);
            db.AtendimentoMensagens.AddRange(entradaEsperando, notaInterna, entradaRespondida, respostaDona, entradaAutomatica, entradaPoucoTempo);
            await db.SaveChangesAsync();
        }

        // Três rodadas: um lembrete por fato, nunca mais.
        for (var rodada = 0; rodada < 3; rodada++)
            await RodarAvaliadorAsync(agora.AddSeconds(rodada));

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            var lembretes = await db.Lembretes.IgnoreQueryFilters().Where(l => l.EmpresaId == empresa.Id).ToListAsync();
            lembretes.Should().HaveCount(2);
            var pagamento = lembretes.Should().ContainSingle(l => l.Tipo == TipoLembrete.PagamentoSemBaixa).Subject;
            pagamento.PedidoId.Should().Be(parado.Id);
            pagamento.ConversaId.Should().Be(esperando.Id, "o pedido está em andamento nessa conversa");
            var semResposta = lembretes.Should().ContainSingle(l => l.Tipo == TipoLembrete.ClienteSemResposta).Subject;
            semResposta.ConversaId.Should().Be(esperando.Id);
            semResposta.Referencia.Should().Be(entradaEsperando.Id.ToString());
            semResposta.ParaUsuarioId.Should().Be(atendente);
            lembretes.Should().OnlyContain(l => l.EstaAberto && l.AvisadoEm != null);

            // A dona responde: na próxima rodada o lembrete some.
            db.AtendimentoMensagens.Add(Mensagem.Saida(empresa.Id, esperando.Id, AutorMensagem.Dona, agora.AddMinutes(1), TipoConteudoMensagem.Texto, "oi!", "resposta-confirmada-2"));
            await db.SaveChangesAsync();
        }

        await RodarAvaliadorAsync(agora.AddMinutes(2));

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            var lembretes = await db.Lembretes.IgnoreQueryFilters().Where(l => l.EmpresaId == empresa.Id).ToListAsync();
            lembretes.Single(l => l.Tipo == TipoLembrete.ClienteSemResposta).EstaAberto.Should().BeFalse();
            lembretes.Single(l => l.Tipo == TipoLembrete.PagamentoSemBaixa).EstaAberto.Should().BeTrue();

            // O índice único é a segunda trava da idempotência; o manual (sem referência) não entra nela.
            db.Lembretes.Add(Lembrete.Automatico(empresa.Id, TipoLembrete.PagamentoSemBaixa, parado.Id.ToString(), "dup", agora));
            var duplicar = () => db.SaveChangesAsync();
            await duplicar.Should().ThrowAsync<DbUpdateException>();
            db.ChangeTracker.Clear();
            db.Lembretes.Add(Lembrete.Manual(empresa.Id, "um", null, atendente, agora));
            db.Lembretes.Add(Lembrete.Manual(empresa.Id, "dois", null, atendente, agora));
            await db.SaveChangesAsync();

            (await db.Database
                .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'lembretes'")
                .SingleAsync())
                .Should().Be(1);
        }
    }

    [SkippableFact]
    public async Task SlaDaLojaDecideQuandoOClienteSemRespostaVence()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        // #1427: com SLA de 2 min gravado na loja, 3 min de espera já vencem (o padrão das opções é 10).
        var agora = new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc);
        var comSla = Empresa.Criar("Casa da Baba SLA", "11222333000262");
        var semConfiguracao = Empresa.Criar("Casa da Baba Sem SLA", "11222333000343");
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(comSla.Id);
        configuracao.Atualizar(null, null, null, null, null, null, null, null, null, slaRespostaMinutos: 2);

        Conversa Esperando(Empresa empresa, string waId, out Mensagem entrada)
        {
            var conversa = Conversa.Abrir(empresa.Id, waId, agora.AddMinutes(-10));
            conversa.Assumir(agora.AddMinutes(-9), Guid.NewGuid());
            entrada = Mensagem.Entrada(empresa.Id, conversa.Id, agora.AddMinutes(-3), TipoConteudoMensagem.Texto, "oi?");
            return conversa;
        }

        var daLoja = Esperando(comSla, "5511999990011", out var entradaDaLoja);
        var padrao = Esperando(semConfiguracao, "5511999990012", out var entradaPadrao);

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(comSla, semConfiguracao);
            db.ConfiguracoesAtendimento.Add(configuracao);
            db.AtendimentoConversas.AddRange(daLoja, padrao);
            db.AtendimentoMensagens.AddRange(entradaDaLoja, entradaPadrao);
            await db.SaveChangesAsync();

            var candidatos = await new CandidatosLembreteQuery(db).ListarConversasSemRespostaAsync(agora.AddMinutes(-1));
            candidatos.Should().Contain(c => c.ConversaId == daLoja.Id && c.SlaRespostaMinutos == 2 && c.EntradaEm != null);
            candidatos.Should().Contain(c => c.ConversaId == padrao.Id && c.SlaRespostaMinutos == null);
        }

        await RodarAvaliadorAsync(agora);

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            var lembretes = await db.Lembretes.IgnoreQueryFilters()
                .Where(l => l.EmpresaId == comSla.Id || l.EmpresaId == semConfiguracao.Id).ToListAsync();
            var vencido = lembretes.Should().ContainSingle().Subject;
            vencido.ConversaId.Should().Be(daLoja.Id);
            vencido.Texto.Should().Contain("há 2 min");
        }
    }

    private async Task RodarAvaliadorAsync(DateTime instante)
    {
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        var avaliador = new AvaliarLembretesUseCase(
            new LembreteRepository(db), new CandidatosLembreteQuery(db), Substitute.For<INotificadorService>(),
            Substitute.For<IOperacaoEventPublisher>(), db, new RelogioFixo(instante),
            Microsoft.Extensions.Options.Options.Create(new EasyStock.Application.Services.Notifications.PrazosOptions()));
        await avaliador.ExecuteAsync();
    }

    private static Pedido NovoPedido(Guid empresaId, string status, DateTime alteradoEm) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        Status = status,
        ClienteNome = "Fulana",
        Total = Dinheiro.Zero,
        CriadoEm = alteradoEm,
        AlteradoEm = alteradoEm,
    };

    private sealed class RelogioFixo(DateTime agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(agora, TimeSpan.Zero);
    }
}
