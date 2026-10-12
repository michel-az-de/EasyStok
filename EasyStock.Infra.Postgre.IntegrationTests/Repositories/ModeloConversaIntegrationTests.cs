using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class ModeloConversaIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria>();
    private EnviarModeloConversaUseCase UseCase(EasyStockDbContext db, TimeProvider? relogio = null)
    {
        _canal.Canal.Returns(CanalConversa.WhatsApp);
        var catalogo = Substitute.For<ICatalogoModelosAtendimento>();
        catalogo.ListarAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([
            new ModeloAtendimento("retomar", "pt_BR", "UTILITY", "Olá {{1}}", 1)]);
        return new(new ConversaRepository(db), new ClienteRepository(db), catalogo,
            new(Substitute.For<IConsentimentoContatoRepository>()), new([_canal]), db, relogio ?? TimeProvider.System);
    }

    private async Task<EnviarModeloConversaCommand> Preparar()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var empresa = Empresa.Criar("Modelos", Guid.NewGuid().ToString("N")[..14]);
        db.SetMobileTenantContext(empresa.Id);
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Ana" };
        var conversa = Conversa.Abrir(empresa.Id, "5511999991111", DateTime.UtcNow.AddDays(-2), clienteId: cliente.Id);
        conversa.RegistrarEntrada(DateTime.UtcNow.AddDays(-2));
        db.Empresas.Add(empresa); db.Clientes.Add(cliente); db.AtendimentoConversas.Add(conversa);
        await db.SaveChangesAsync();
        return new(empresa.Id, Guid.NewGuid(), conversa.Id, Guid.NewGuid(), "retomar", "pt_BR", ["Ana"]);
    }

    [SkippableFact]
    public async Task IntencaoVisivelAntesDoCanal_DuasRequisicoes_Reinicio_ReplayUmEnvio()
    {
        var command = await Preparar();
        var chegou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resposta = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { chegou.TrySetResult(); return resposta.Task; });
        await using var db1 = fixture.CreateDbContext(); db1.SetMobileTenantContext(command.EmpresaId);
        await using var db2 = fixture.CreateDbContext(); db2.SetMobileTenantContext(command.EmpresaId);
        // A segunda requisição já carregou a conversa antes da primeira obter o lock.
        await db2.AtendimentoConversas.SingleAsync(c => c.Id == command.ConversaId);
        var primeiro = UseCase(db1).ExecuteAsync(command);
        await chegou.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pendente = await UseCase(db2).ExecuteAsync(command);
        pendente.Status.Should().Be(StatusMensagem.Pendente);
        resposta.SetResult("wamid.modelo-pg");
        (await primeiro).Status.Should().Be(StatusMensagem.Enviada);

        await using var conferir = fixture.CreateDbContext(); conferir.SetMobileTenantContext(command.EmpresaId);
        var retomado = await UseCase(conferir).ExecuteAsync(command);
        retomado.ExternoId.Should().Be("wamid.modelo-pg");
        retomado.Modelo!.Parametros.Should().Equal("Ana");
        (await conferir.AtendimentoMensagens.CountAsync()).Should().Be(1);
        var conversa = await conferir.AtendimentoConversas.SingleAsync();
        conversa.DentroDaJanela(DateTime.UtcNow).Should().BeFalse();
        conversa.AssumidaPorUsuarioId.Should().Be(command.UsuarioId);
        await _canal.Received(1).EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TentativaPersistidaSemConfirmacaoOuComFalha_NuncaReenviaComoTexto(bool falhou)
    {
        var command = await Preparar();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(command.EmpresaId);
            var m = Mensagem.SaidaModelo(command.TentativaId, command.EmpresaId, command.ConversaId, command.UsuarioId,
                DateTime.UtcNow, new(command.Nome, command.Idioma, command.Parametros), "Olá Ana");
            if (falhou) m.RegistrarFalhaEnvio("sem resposta", TipoFalhaEnvio.Incerta, DateTime.UtcNow);
            seed.AtendimentoMensagens.Add(m); await seed.SaveChangesAsync();
        }
        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(command.EmpresaId);
        var m2 = await UseCase(db).ExecuteAsync(command);
        m2.Status.Should().Be(falhou ? StatusMensagem.Falhou : StatusMensagem.Pendente);
        var salva = await db.AtendimentoMensagens.SingleAsync();
        salva.PodeReenviar.Should().BeFalse(); salva.PrecisaDeReservaSms.Should().BeFalse();
        salva.ProximoReenvioEm.Should().BeNull();
        _canal.ReceivedCalls().Count(c => c.GetMethodInfo().Name.StartsWith("Enviar", StringComparison.Ordinal)).Should().Be(0);
    }

    // #1562: o processo caiu entre gravar a intenção e o POST. Passado o prazo ela vira falha incerta; a nota
    // interna, o texto e a intenção recente continuam pendentes, e repetir a tentativa não chama o canal.
    [SkippableFact]
    public async Task IntencaoPresaAlemDoPrazoViraFalhaIncerta_SemReenvio()
    {
        var command = await Preparar();
        var agora = DateTime.UtcNow;
        var prazo = EasyStock.Application.UseCases.Atendimento.Reenvio.EncerrarIntencoesModeloSemDesfechoUseCase.PrazoSemDesfecho;
        var modelo = new ModeloMensagem(command.Nome, command.Idioma, command.Parametros);
        var presa = Mensagem.SaidaModelo(command.TentativaId, command.EmpresaId, command.ConversaId, command.UsuarioId,
            agora - prazo - TimeSpan.FromMinutes(1), modelo, "Olá Ana");
        var recente = Mensagem.SaidaModelo(Guid.NewGuid(), command.EmpresaId, command.ConversaId, command.UsuarioId,
            agora - prazo + TimeSpan.FromMinutes(1), modelo, "Olá Ana");
        var nota = Mensagem.Saida(command.EmpresaId, command.ConversaId, AutorMensagem.Sistema, agora.AddDays(-1),
            TipoConteudoMensagem.Texto, "Nota interna");
        var texto = Mensagem.Saida(command.EmpresaId, command.ConversaId, AutorMensagem.Dona, agora.AddDays(-1),
            TipoConteudoMensagem.Texto, "Texto sem confirmação");
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(command.EmpresaId);
            seed.AtendimentoMensagens.AddRange(presa, recente, nota, texto);
            await seed.SaveChangesAsync();
        }

        // Duas rodadas: a segunda não acha mais a intenção e não soma outra falha.
        for (var rodada = 0; rodada < 2; rodada++)
        {
            await using var varredura = fixture.CreateDbContext();
            using var bypass = varredura.UseRowLevelSecurityBypass();
            await new EasyStock.Application.UseCases.Atendimento.Reenvio.EncerrarIntencoesModeloSemDesfechoUseCase(
                new ConversaRepository(varredura), varredura, new RelogioFixo(agora)).ExecuteAsync(50);
        }

        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(command.EmpresaId);
        var repetida = await UseCase(db).ExecuteAsync(command);
        repetida.Status.Should().Be(StatusMensagem.Falhou);
        var salvas = await db.AtendimentoMensagens.AsNoTracking().ToDictionaryAsync(m => m.Id);
        var encerrada = salvas[presa.Id];
        encerrada.Status.Should().Be(StatusMensagem.Falhou);
        encerrada.UltimaFalhaEnvio.Should().Be(TipoFalhaEnvio.Incerta);
        encerrada.Erro.Should().Contain(EnviarModeloConversaUseCase.AvisoEnvioIncerto);
        encerrada.TentativasEnvio.Should().Be(1);
        encerrada.ProximoReenvioEm.Should().BeNull();
        encerrada.PodeReenviar.Should().BeFalse(); encerrada.PrecisaDeReservaSms.Should().BeFalse();
        salvas[recente.Id].Status.Should().Be(StatusMensagem.Pendente, "o envio ainda pode estar em andamento");
        salvas[nota.Id].Status.Should().Be(StatusMensagem.Pendente, "nota interna não é intenção de modelo");
        salvas[texto.Id].Status.Should().Be(StatusMensagem.Pendente, "texto pendente não é intenção de modelo");
        _canal.ReceivedCalls().Count(c => c.GetMethodInfo().Name.StartsWith("Enviar", StringComparison.Ordinal)).Should().Be(0);
    }

    // #1599: a varredura marca a intenção como falha incerta com o POST ainda no ar e o canal confirma depois.
    // A entrega vence: a mensagem fica enviada, sem o erro, a tentativa e o tipo de falha que a varredura gravou.
    [SkippableFact]
    public async Task CanalConfirmaDepoisDaVarredura_SucessoVenceELimpaAFalhaIncerta()
    {
        var command = await Preparar();
        var agora = DateTime.UtcNow;
        Mensagem? marcada = null;
        var gravadaEm = agora - EasyStock.Application.UseCases.Atendimento.Reenvio.EncerrarIntencoesModeloSemDesfechoUseCase.PrazoSemDesfecho
            - TimeSpan.FromMinutes(1);
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await using var varredura = fixture.CreateDbContext();
                using var bypass = varredura.UseRowLevelSecurityBypass();
                var encerrar = new EasyStock.Application.UseCases.Atendimento.Reenvio.EncerrarIntencoesModeloSemDesfechoUseCase(
                    new ConversaRepository(varredura), varredura, new RelogioFixo(agora));
                while (await encerrar.ExecuteAsync(50) > 0) { }
                // Conferido fora daqui: o use case trata exceção do canal como falha de envio e engoliria a asserção.
                marcada = await varredura.AtendimentoMensagens.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == command.TentativaId);
                return "wamid.tardio";
            });

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(command.EmpresaId);
            await UseCase(db, new RelogioFixo(gravadaEm)).ExecuteAsync(command);
        }

        marcada.Should().NotBeNull("o canal foi chamado e a varredura rodou no meio do envio");
        marcada!.Status.Should().Be(StatusMensagem.Falhou, "a varredura chegou antes da resposta do canal");
        marcada.TentativasEnvio.Should().Be(1);

        await using var conferir = fixture.CreateDbContext(); conferir.SetMobileTenantContext(command.EmpresaId);
        var salva = await conferir.AtendimentoMensagens.AsNoTracking().SingleAsync(m => m.Id == command.TentativaId);
        salva.Status.Should().Be(StatusMensagem.Enviada);
        salva.ExternoId.Should().Be("wamid.tardio");
        salva.Erro.Should().BeNull();
        salva.TentativasEnvio.Should().Be(0);
        salva.UltimaFalhaEnvio.Should().BeNull();
        salva.ProximoReenvioEm.Should().BeNull();
    }

    // #1599: a varredura roda a cada 30 s; o índice parcial cobre exatamente o predicado dela.
    [SkippableFact]
    public async Task IndiceParcialDaVarreduraExisteComOFiltroDaConsulta()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var definicoes = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'atendimento_mensagens' AND indexname = 'ix_atendimento_mensagens_modelo_pendente'")
            .ToListAsync();
        var definicao = definicoes.Should().ContainSingle().Subject;
        definicao.Should().Contain("USING btree (\"EnviadaEm\")");
        definicao.Should().Contain("\"ModeloJson\" IS NOT NULL");
        definicao.Should().Contain($"\"Status\" = {(int)StatusMensagem.Pendente}");
    }

    private sealed class RelogioFixo(DateTime agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(agora, TimeSpan.Zero);
    }

    [SkippableFact]
    public async Task RlsImpedeConsultarOuEnviarModeloDeOutraEmpresa()
    {
        var propria = await Preparar(); var alheia = await Preparar();
        await using var db = fixture.CreateRlsClientDbContext(); db.SetMobileTenantContext(propria.EmpresaId);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {propria.EmpresaId.ToString()}, false)");
        var act = () => UseCase(db).ExecuteAsync(propria with { ConversaId = alheia.ConversaId });
        await act.Should().ThrowAsync<ConversaNaoEncontradaException>();
        (await db.AtendimentoConversas.IgnoreQueryFilters().CountAsync(c => c.Id == alheia.ConversaId)).Should().Be(0);
    }
}
