using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class ReabrirConversaIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static ReabrirConversaUseCase UseCase(EasyStockDbContext db) =>
        new(new ConversaRepository(db), new ClienteRepository(db), db, TimeProvider.System, new PedidoRepository(db));

    private async Task<Conversa> Preparar(Guid empresa, string contato = "5511999990001")
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var conversa = Conversa.Abrir(empresa, contato, DateTime.UtcNow.AddDays(-2));
        conversa.RegistrarEntrada(DateTime.UtcNow.AddDays(-2));
        conversa.DefinirContexto("{\"observacao\":\"sem cebola\"}");
        conversa.DefinirPedidoEmAndamento(Guid.NewGuid());
        conversa.Encerrar(DateTime.UtcNow.AddDays(-1));
        db.AtendimentoConversas.Add(conversa);
        db.AtendimentoMensagens.Add(Mensagem.Entrada(empresa, conversa.Id, conversa.UltimaMensagemEntradaEm!.Value,
            TipoConteudoMensagem.Texto, "mensagem original", $"entrada-{conversa.Id}"));
        await db.SaveChangesAsync();
        return conversa;
    }

    // #1839: atendimento encerrado vira história; o novo começa sem o pedido já entregue ou cancelado.
    [SkippableTheory]
    [InlineData(StatusPedidoMapper.Entregue, true)]
    [InlineData(StatusPedidoMapper.Cancelado, true)]
    [InlineData(StatusPedidoMapper.Preparando, false)]
    public async Task Reabrir_SoltaPedidoFinalizado_EMantemPedidoEmAndamento(string status, bool solta)
    {
        var empresa = Guid.NewGuid();
        var conversa = await Preparar(empresa);
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            db.Empresas.Add(new Empresa { Id = empresa, Nome = "Casa", Documento = empresa.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            db.Pedidos.Add(new Pedido { Id = conversa.PedidoEmAndamentoId!.Value, EmpresaId = empresa, Status = status, Total = Dinheiro.Zero, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            await UseCase(db).ExecuteAsync(new(empresa, Guid.NewGuid(), conversa.Id, "Atendente"));
        }

        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresa);
        var salva = await conferir.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);
        salva.EstaAberta.Should().BeTrue();
        salva.PedidoEmAndamentoId.Should().Be(solta ? null : conversa.PedidoEmAndamentoId);
    }

    [SkippableFact]
    public async Task RepeticoesConcorrentesInclusiveComEntidadeJaRastreada_GravamUmaNota()
    {
        var empresa = Guid.NewGuid();
        var conversa = await Preparar(empresa);
        var usuario = Guid.NewGuid();
        await using var db1 = fixture.CreateDbContext();
        await using var db2 = fixture.CreateDbContext();
        db1.SetMobileTenantContext(empresa);
        db2.SetMobileTenantContext(empresa);
        await db1.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);
        await db2.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);
        var comando = new AcaoConversaCommand(empresa, usuario, conversa.Id, "Atendente");

        var resultados = await Task.WhenAll(UseCase(db1).ExecuteAsync(comando), UseCase(db2).ExecuteAsync(comando));

        resultados.Should().OnlyContain(r => r.Situacao == SituacaoConversa.Assumida && r.AssumidaPorUsuarioId == null);
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresa);
        var salva = await conferir.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);
        salva.PedidoEmAndamentoId.Should().Be(conversa.PedidoEmAndamentoId);
        salva.UltimaMensagemEntradaEm.Should().BeCloseTo(conversa.UltimaMensagemEntradaEm!.Value, TimeSpan.FromMilliseconds(1));
        salva.ContextoJson.Should().Contain("sem cebola");
        salva.EncerradaEm.Should().BeNull();
        salva.DentroDaJanela(DateTime.UtcNow).Should().BeFalse();
        var notas = await conferir.AtendimentoMensagens.Where(m => m.ConversaId == conversa.Id && m.Autor == AutorMensagem.Sistema).ToListAsync();
        notas.Should().ContainSingle();
        notas[0].EnviadaPorUsuarioId.Should().Be(usuario);
        (await conferir.AtendimentoMensagens.CountAsync(m => m.ConversaId == conversa.Id)).Should().Be(2);
        (await new ConversaRepository(conferir).ObterAbertaPorContatoAsync(empresa, conversa.Canal, conversa.ContatoIdExterno))!.Id.Should().Be(conversa.Id);
    }

    [SkippableTheory]
    [InlineData("5511999990001")]
    [InlineData("551199990001")]
    public async Task HistoricosConcorrentesDoMesmoContato_SoUmReabre(string outroContato)
    {
        var empresa = Guid.NewGuid();
        var primeira = await Preparar(empresa);
        var segunda = await Preparar(empresa, outroContato);

        async Task<bool> Tentar(Guid id)
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(empresa);
            try
            {
                await UseCase(db).ExecuteAsync(new(empresa, Guid.NewGuid(), id));
                return true;
            }
            catch (RegraDeDominioVioladaException ex)
            {
                ex.Message.Should().Be(ReabrirConversaUseCase.ConflitoContato);
                return false;
            }
        }

        var resultados = await Task.WhenAll(Tentar(primeira.Id), Tentar(segunda.Id));
        resultados.Count(r => r).Should().Be(1);
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresa);
        (await conferir.AtendimentoConversas.CountAsync(c => c.Situacao != SituacaoConversa.Encerrada)).Should().Be(1);
        (await conferir.AtendimentoMensagens.CountAsync(m => m.Autor == AutorMensagem.Sistema)).Should().Be(1);
    }

    [SkippableFact]
    public async Task EntradaAbreOutraDuranteReabertura_IndiceRecusaERollbackNaoDeixaNota()
    {
        var empresa = Guid.NewGuid();
        var antiga = await Preparar(empresa);
        await using var entrada = fixture.CreateDbContext();
        entrada.SetMobileTenantContext(empresa);
        await using var tx = await entrada.Database.BeginTransactionAsync();
        var nova = Conversa.Abrir(empresa, antiga.ContatoIdExterno, DateTime.UtcNow);
        entrada.AtendimentoConversas.Add(nova);
        await entrada.SaveChangesAsync();

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        await db.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
        var reabertura = UseCase(db).ExecuteAsync(new(empresa, Guid.NewGuid(), antiga.Id));
        await using var monitor = fixture.CreateDbContext();
        var esperandoIndice = false;
        for (var tentativa = 0; tentativa < 100 && !reabertura.IsCompleted; tentativa++)
        {
            esperandoIndice = await monitor.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND wait_event = 'transactionid') AS "Value"
                """).SingleAsync();
            if (esperandoIndice) break;
            await Task.Delay(25);
        }
        await tx.CommitAsync();
        var act = () => reabertura;
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(ReabrirConversaUseCase.ConflitoContato);
        esperandoIndice.Should().BeTrue("a entrada ainda não confirmada deve disputar o índice com a reabertura");

        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresa);
        (await conferir.AtendimentoConversas.SingleAsync(c => c.Id == antiga.Id)).EstaAberta.Should().BeFalse();
        (await conferir.AtendimentoMensagens.CountAsync(m => m.Autor == AutorMensagem.Sistema)).Should().Be(0);
    }

    [SkippableFact]
    public async Task RlsEConsultaExplicita_NaoReabremConversaAlheia()
    {
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        var alheia = await Preparar(empresaB);
        var propria = await Preparar(empresaA);
        await using var db = fixture.CreateRlsClientDbContext();
        db.SetMobileTenantContext(empresaA);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresaA.ToString()}, false)");

        var act = () => UseCase(db).ExecuteAsync(new(empresaA, Guid.NewGuid(), alheia.Id));
        await act.Should().ThrowAsync<ConversaNaoEncontradaException>();
        (await UseCase(db).ExecuteAsync(new(empresaA, Guid.NewGuid(), propria.Id))).Situacao.Should().Be(SituacaoConversa.Assumida);
        // Empresa do comando divergente da sessão SQL continua bloqueada pelo RLS, inclusive no FOR UPDATE.
        (await db.ExecuteInTransactionSemRetryAsync(token =>
            new ConversaRepository(db).ObterPorIdComLockAsync(empresaB, alheia.Id, token))).Should().BeNull();
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresaB);
        (await conferir.AtendimentoConversas.SingleAsync(c => c.Id == alheia.Id)).EstaAberta.Should().BeFalse();
    }
}
