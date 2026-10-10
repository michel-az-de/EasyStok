using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.AlterarAgendamentoPedido;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

[Collection("PostgreSqlTestCollection")]
public sealed class TrocarJanelaPedidoTests(PostgreSqlDatabaseFixture fixture)
{
    private readonly IOperacaoEventPublisher _eventos = Substitute.For<IOperacaoEventPublisher>();

    [SkippableFact]
    public async Task Troca_vaga_recalcula_prazo_muda_dia_do_kds_e_reenvio_nao_duplica()
    {
        var c = await PrepararAsync();
        await using var provider = Provider();
        var cmd = new TrocarJanelaPedidoCommand(c.Empresa, c.Pedidos[0], c.Nova.Id, c.DataNova);
        (await Executar(provider, cmd))!.Alterado.Should().BeTrue();
        (await Executar(provider, cmd))!.Alterado.Should().BeFalse();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(c.Empresa);
        var pedido = await db.Pedidos.SingleAsync(p => p.Id == cmd.PedidoId);
        pedido.AgendadoParaEm.Should().Be(HorarioBrasil.InstanteUtc(c.DataNova, c.Nova.HoraInicio));
        var prazo = await scope.ServiceProvider.GetRequiredService<IPrazoPreparoPedidoQueries>().ObterAsync(c.Empresa, pedido.Id);
        pedido.InicioPrevistoEm.Should().Be(pedido.AgendadoParaEm!.Value.AddMinutes(
            -(prazo!.TempoPreparoPadraoMinutos + prazo.RespiroMinutos)));
        pedido.AtrasoNotificadoEm.Should().BeNull();
        var historico = await db.VagasOcupadas.Where(v => v.PedidoId == pedido.Id).ToListAsync();
        historico.Should().HaveCount(2);
        historico.Should().ContainSingle(v => v.LiberadoEm == null && v.JanelaEntregaId == c.Nova.Id);
        historico.Should().ContainSingle(v => v.LiberadoEm != null && v.MotivoLiberacao == "reagendado");
        (await db.Set<PedidoEvento>().CountAsync(e => e.PedidoId == pedido.Id && e.Tipo == "agendamento_alterado")).Should().Be(1);
        var principal = await scope.ServiceProvider.GetRequiredService<IVagaOcupadaRepository>().GetByPedidoIdsAsync([pedido.Id]);
        principal[pedido.Id].Vaga.JanelaEntregaId.Should().Be(c.Nova.Id);
        var kds = scope.ServiceProvider.GetRequiredService<IKdsPedidoQueries>();
        (await kds.ListarAsync(c.Empresa, ["aguardando"], c.DataAntiga, c.DataAntiga)).Should().NotContain(p => p.Id == pedido.Id);
        var novoKds = (await kds.ListarAsync(c.Empresa, ["aguardando"], c.DataNova, c.DataNova)).Single(p => p.Id == pedido.Id);
        novoKds.Janela!.Label.Should().Be(c.Nova.Label);
        novoKds.InicioPrevistoEm.Should().Be(pedido.InicioPrevistoEm);
        await _eventos.Received(1).PublicarAsync(EventosOperacao.PedidoReagendado, c.Empresa, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [SkippableFact]
    public async Task Janela_cheia_preserva_vaga_horario_e_auditoria()
    {
        var c = await PrepararAsync();
        await using var provider = Provider();
        await Executar(provider, new(c.Empresa, c.Pedidos[0], c.Nova.Id, c.DataNova));
        await FluentActions.Invoking(() => Executar(provider, new(c.Empresa, c.Pedidos[1], c.Nova.Id, c.DataNova)))
            .Should().ThrowAsync<JanelaSemVagasException>();
        await ConferirPreservado(c, c.Pedidos[1]);
    }

    [SkippableFact]
    public async Task Duas_trocas_disputam_ultima_vaga_e_rollback_restaura_a_vaga_da_perdedora()
    {
        var c = await PrepararAsync();
        await using var provider = Provider();
        await using var bloqueio = new NpgsqlConnection(fixture.ConnectionString);
        await bloqueio.OpenAsync();
        await using var tx = await bloqueio.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@janela))", bloqueio, tx))
        {
            lockCmd.Parameters.AddWithValue("janela", c.Nova.Id.ToString());
            await lockCmd.ExecuteNonQueryAsync();
        }
        var tarefas = c.Pedidos.Select(id => Record.ExceptionAsync(() => Executar(provider,
            new(c.Empresa, id, c.Nova.Id, c.DataNova)))).ToArray();
        long esperando = 0;
        try
        {
            // As duas requisições já liberaram a vaga antiga dentro de suas transações e
            // disputam o MESMO lock de capacidade. Prova a corrida após a leitura de vagas.
            for (var i = 0; i < 100 && esperando < 2; i++)
            {
                await using var consulta = new NpgsqlCommand(
                    "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())", bloqueio, tx);
                esperando = (long)(await consulta.ExecuteScalarAsync())!;
                if (esperando < 2) await Task.Delay(50);
            }
        }
        finally { await tx.CommitAsync(); }
        var erros = await Task.WhenAll(tarefas);
        esperando.Should().Be(2);
        erros.Count(e => e is null).Should().Be(1);
        erros.OfType<JanelaSemVagasException>().Should().ContainSingle();
        await ConferirPreservado(c, c.Pedidos[Array.FindIndex(erros, e => e is not null)]);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(c.Empresa);
        (await db.VagasOcupadas.CountAsync(v => v.JanelaEntregaId == c.Nova.Id && v.LiberadoEm == null)).Should().Be(1);
    }

    [SkippableTheory]
    [InlineData("bloqueada")]
    [InlineData("dia_incorreto")]
    [InlineData("inativa")]
    [InlineData("passado")]
    [InlineData("prazo")]
    [InlineData("outro_tenant")]
    [InlineData("entregue")]
    [InlineData("cancelado")]
    public async Task Rejeita_janela_invalida_sem_alterar_pedido(string caso)
    {
        var c = await PrepararAsync();
        var data = c.DataNova;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(c.Empresa);
            if (caso == "bloqueada") db.BloqueiosEntrega.Add(BloqueioEntrega.Criar(c.Nova.StorefrontId, data, "Fechado", c.Nova.Id));
            if (caso == "inativa")
                (await db.JanelasEntrega.SingleAsync(j => j.Id == c.Nova.Id)).Desativar();
            if (caso == "dia_incorreto") data = data.AddDays(1);
            if (caso == "passado") data = data.AddDays(-14);
            if (caso is "entregue" or "cancelado")
            {
                var pedido = await db.Pedidos.SingleAsync(p => p.Id == c.Pedidos[0]);
                pedido.Status = caso;
            }
            await db.SaveChangesAsync();
        }
        await using var provider = Provider(caso == "prazo" ? new DateTimeOffset(HorarioBrasil.InstanteUtc(c.DataNova, new(14, 55))) : null);
        if (caso == "outro_tenant")
            (await Executar(provider, new(Guid.NewGuid(), c.Pedidos[0], c.Nova.Id, data))).Should().BeNull();
        else
            await FluentActions.Invoking(() => Executar(provider, new(c.Empresa, c.Pedidos[0], c.Nova.Id, data)))
                .Should().ThrowAsync<UseCaseValidationException>();
        await ConferirPreservado(c, c.Pedidos[0]);
    }

    [SkippableFact]
    public async Task Aviso_persiste_na_mesma_transacao_com_faixa_nova_e_chave_por_troca()
    {
        var c = await PrepararAsync();
        await using var provider = Provider();
        var cmd = new TrocarJanelaPedidoCommand(c.Empresa, c.Pedidos[0], c.Nova.Id, c.DataNova, true);
        (await Executar(provider, cmd))!.AvisoEnfileirado.Should().BeTrue();
        (await Executar(provider, cmd))!.Alterado.Should().BeFalse();
        await Executar(provider, cmd with { JanelaId = c.Antiga.Id, Data = c.DataAntiga });
        await Executar(provider, cmd);
        await using var db = fixture.CreateDbContext();
        var avisos = await db.NotifEventos.IgnoreQueryFilters().Where(e => e.EmpresaId == c.Empresa && e.Tipo == TipoEventoNotificacao.PedidoReagendado).ToListAsync();
        avisos.Should().HaveCount(3);
        avisos.Select(e => System.Text.Json.JsonDocument.Parse(e.PayloadJson).RootElement.GetProperty("chaveIdempotencia").GetString()).Distinct().Should().HaveCount(3);
        avisos.Count(e => e.PayloadJson.Contains(c.DataNova.ToString("dd/MM/yyyy"))).Should().Be(2);
    }

    [SkippableFact]
    public async Task Janela_de_outra_empresa_nao_pode_ser_usada()
    {
        var c = await PrepararAsync();
        var outra = await PrepararAsync();
        await using var provider = Provider();
        await FluentActions.Invoking(() => Executar(provider, new(c.Empresa, c.Pedidos[0], outra.Nova.Id, outra.DataNova)))
            .Should().ThrowAsync<UseCaseValidationException>();
        await ConferirPreservado(c, c.Pedidos[0]);
    }

    [SkippableFact]
    public async Task Falha_ao_gravar_aviso_reverte_inclusive_a_nova_vaga_ja_inserida()
    {
        var c = await PrepararAsync();
        await using var provider = Provider(falharAviso: true);
        await FluentActions.Invoking(() => Executar(provider, new(c.Empresa, c.Pedidos[0], c.Nova.Id, c.DataNova, true)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Falha controlada ao gravar aviso");
        await ConferirPreservado(c, c.Pedidos[0]);
        await using var db = fixture.CreateDbContext();
        (await db.VagasOcupadas.CountAsync(v => v.PedidoId == c.Pedidos[0])).Should().Be(1);
        await _eventos.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default, default!, default);
    }

    [SkippableFact]
    public async Task Rota_antiga_de_agendamento_nao_desalinha_pedido_com_vaga()
    {
        var c = await PrepararAsync();
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(c.Empresa);
        await FluentActions.Invoking(() => scope.ServiceProvider.GetRequiredService<AlterarAgendamentoPedidoUseCase>()
            .ExecuteAsync(new(c.Empresa, c.Pedidos[0], DateTime.UtcNow.AddDays(5))))
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("*troca de janela*");
        await ConferirPreservado(c, c.Pedidos[0]);
    }

    private async Task ConferirPreservado(Cenario c, Guid pedidoId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(c.Empresa);
        var vaga = await db.VagasOcupadas.SingleAsync(v => v.PedidoId == pedidoId && v.LiberadoEm == null);
        vaga.JanelaEntregaId.Should().Be(c.Antiga.Id);
        (await db.Pedidos.SingleAsync(p => p.Id == pedidoId)).AgendadoParaEm.Should().Be(HorarioBrasil.InstanteUtc(c.DataAntiga, c.Antiga.HoraInicio));
        (await db.Set<PedidoEvento>().CountAsync(e => e.PedidoId == pedidoId)).Should().Be(0);
    }

    private async Task<Cenario> PrepararAsync()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresa = Guid.NewGuid();
        var dataAntiga = HorarioBrasil.DataOperacional(DateTime.UtcNow).AddDays(2);
        var dataNova = dataAntiga.AddDays(1);
        var sf = StorefrontEntity.Criar(empresa, $"rg-{Guid.NewGuid():N}", "Teste reagendamento", 0);
        sf.Ativar();
        var antiga = JanelaEntrega.Criar(sf.Id, (int)dataAntiga.DayOfWeek, new(12, 0), new(13, 0), 2, "Almoço");
        var nova = JanelaEntrega.Criar(sf.Id, (int)dataNova.DayOfWeek, new(15, 0), new(16, 0), 1, "Tarde");
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        db.Empresas.Add(new Empresa { Id = empresa, Nome = "Teste", Documento = empresa.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        db.Storefronts.Add(sf);
        db.JanelasEntrega.AddRange(antiga, nova);
        var pedidos = Enumerable.Range(0, 2).Select(_ => Pedido.Criar(empresa)).ToArray();
        foreach (var p in pedidos)
        {
            p.ClienteTelefone = "5511999998888";
            p.AgendadoParaEm = HorarioBrasil.InstanteUtc(dataAntiga, antiga.HoraInicio);
            p.DefinirInicioPrevisto(DateTime.UtcNow.AddHours(-1));
            p.MarcarAtrasoNotificado(DateTime.UtcNow);
            db.Pedidos.Add(p);
            db.VagasOcupadas.Add(VagaOcupada.Ocupar(antiga.Id, dataAntiga, p.Id));
        }
        await db.SaveChangesAsync();
        return new(empresa, antiga, nova, dataAntiga, dataNova, pedidos.Select(p => p.Id).ToArray());
    }

    private ServiceProvider Provider(DateTimeOffset? agora = null, bool falharAviso = false)
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        services.AddSingleton(Substitute.For<IRendererTemplate>());
        if (falharAviso)
        {
            var repositorio = Substitute.For<IEventoNotificacaoRepository>();
            repositorio.AddAsync(Arg.Any<EventoNotificacao>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("Falha controlada ao gravar aviso")));
            services.AddSingleton(repositorio);
        }
        if (agora is { } instante) services.AddSingleton<TimeProvider>(new Relogio(instante));
        services.AddSingleton(_eventos);
        return services.BuildServiceProvider();
    }

    private static async Task<TrocarJanelaPedidoResult?> Executar(ServiceProvider provider, TrocarJanelaPedidoCommand cmd)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(cmd.EmpresaId);
        return await scope.ServiceProvider.GetRequiredService<TrocarJanelaPedidoUseCase>().ExecuteAsync(cmd);
    }

    private sealed record Cenario(Guid Empresa, JanelaEntrega Antiga, JanelaEntrega Nova, DateOnly DataAntiga, DateOnly DataNova, Guid[] Pedidos);
    private sealed class Relogio(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
