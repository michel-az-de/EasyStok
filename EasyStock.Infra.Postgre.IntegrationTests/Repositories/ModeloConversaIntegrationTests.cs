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
    private EnviarModeloConversaUseCase UseCase(EasyStockDbContext db)
    {
        _canal.Canal.Returns(CanalConversa.WhatsApp);
        var catalogo = Substitute.For<ICatalogoModelosAtendimento>();
        catalogo.ListarAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([
            new ModeloAtendimento("retomar", "pt_BR", "UTILITY", "Olá {{1}}", 1)]);
        return new(new ConversaRepository(db), new ClienteRepository(db), catalogo,
            new(Substitute.For<IConsentimentoContatoRepository>()), new([_canal]), db, TimeProvider.System);
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
