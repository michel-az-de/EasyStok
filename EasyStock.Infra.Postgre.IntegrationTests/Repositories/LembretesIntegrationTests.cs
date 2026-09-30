using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Atendimento.Lembretes;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
    public async Task AvaliadorCriaUmPorFatoResolveAoResponderETemRls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var agora = DateTime.UtcNow;
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
        var respostaDona = Mensagem.Saida(empresa.Id, respondida.Id, AutorMensagem.Dona, agora.AddMinutes(-11), TipoConteudoMensagem.Texto, "já vou");
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
            db.AtendimentoMensagens.Add(Mensagem.Saida(empresa.Id, esperando.Id, AutorMensagem.Dona, agora.AddMinutes(1), TipoConteudoMensagem.Texto, "oi!"));
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

    private async Task RodarAvaliadorAsync(DateTime instante)
    {
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        var avaliador = new AvaliarLembretesUseCase(
            new LembreteRepository(db), new CandidatosLembreteQuery(db), Substitute.For<INotificadorService>(),
            Substitute.For<IOperacaoEventPublisher>(), db, new RelogioFixo(instante));
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
