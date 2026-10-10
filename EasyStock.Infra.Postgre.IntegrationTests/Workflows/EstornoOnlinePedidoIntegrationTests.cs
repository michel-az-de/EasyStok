using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Infra.Integrations.Pagamentos.MercadoPago;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Pagamentos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento.Ocorrencias;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Repositories.Atendimento;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

[Collection("PostgreSqlTestCollection")]
public sealed class EstornoOnlinePedidoIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 10, 10, 17, 0, 0, DateTimeKind.Utc);
    private sealed class Relogio : TimeProvider { public override DateTimeOffset GetUtcNow() => Agora; }

    private async Task<SolicitarEstornoOnlineInput> Preparar()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();
        var empresa = new Empresa { Id = Guid.NewGuid(), Nome = "Estorno online sintético", Documento = Guid.NewGuid().ToString("N")[..14], CriadoEm = Agora, AlteradoEm = Agora };
        db.Empresas.Add(empresa);
        db.SetMobileTenantContext(empresa.Id);
        var pedido = Pedido.Criar(empresa.Id);
        pedido.Status = "cancelado";
        var pagamento = new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Valor = 100, Metodo = "pix",
            Referencia = "123456789", RegistradoPorNome = "Mercado Pago", PagoEm = Agora.AddDays(-1) };
        pedido.Pagamentos.Add(pagamento);
        var cobranca = CobrancaPedido.CriarOnline(empresa.Id, pedido.Id, 100, $"pref-{pedido.Id}", "https://mp.test/link", Agora.AddHours(1), 1, Agora.AddDays(-1));
        cobranca.MarcarPaga(pagamento.Referencia, pagamento.Valor, pagamento.Metodo, pagamento.PagoEm);
        db.Pedidos.Add(pedido);
        db.CobrancasPedido.Add(cobranca);
        await db.SaveChangesAsync();
        return new(empresa.Id, pedido.Id, Guid.NewGuid(), pagamento.Id, 40, "Desistência", Guid.NewGuid(), "Dona teste", NivelAcesso.Admin);
    }

    private static EstornosOnlineService Service(EasyStockDbContext db, ProvedorHttp mp)
    {
        var tenant = Substitute.For<ITenantContextAccessor>();
        tenant.When(t => t.SetCurrentTenant(Arg.Any<Guid>())).Do(c => db.SetMobileTenantContext(c.Arg<Guid>()));
        return new(new PedidoRepository(db), new EstornoOnlineRepository(db), new CobrancaPedidoRepository(db),
            mp.Cliente, new CaixaRepository(db), db, tenant, new Relogio());
    }

    private async Task<PedidoEstornoOnline> Executar(SolicitarEstornoOnlineInput input, ProvedorHttp mp)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        return await Service(db, mp).SolicitarAsync(input);
    }

    private async Task<ResolverOcorrenciaInput> PrepararOcorrencia(SolicitarEstornoOnlineInput input)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        var cliente = Cliente.Criar(input.EmpresaId, "Cliente de teste");
        cliente.Telefone = "11999990001";
        db.Clientes.Add(cliente);
        (await db.Pedidos.SingleAsync()).ClienteId = cliente.Id;
        var o = Ocorrencia.Abrir(input.EmpresaId, input.PedidoId, cliente.Id, null, OrigemOcorrencia.Dona, CategoriaOcorrencia.Atraso, "Atraso de teste", Agora);
        db.Ocorrencias.Add(o);
        await db.SaveChangesAsync();
        return new(input.EmpresaId, o.Id, input.UsuarioId, "Cliente atendido", false, null, NivelAcesso.Admin, "Dona teste");
    }

    private async Task<ResolverOcorrenciaResult?> ResolverOcorrencia(ResolverOcorrenciaInput input, ProvedorHttp mp, bool falharNota = false)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        IClienteCrmRepository crm = new ClienteCrmRepository(db);
        if (falharNota)
        {
            crm = Substitute.For<IClienteCrmRepository>();
            crm.AdicionarNotaAsync(Arg.Any<ClienteNota>(), Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw new InvalidOperationException("Falha controlada da nota"));
        }
        var notificacoes = Substitute.For<INotificadorService>();
        notificacoes.EnfileirarEventoAsync(Arg.Any<TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(async c =>
            {
                var evento = EventoNotificacao.Criar(c.ArgAt<TipoEventoNotificacao>(0), c.ArgAt<Guid>(1), c.ArgAt<string>(2), c.ArgAt<Guid?>(3));
                await db.NotifEventos.AddAsync(evento);
                return evento.Id;
            });
        var reembolsar = new ReembolsarPedidoUseCase(new CobrancaPedidoRepository(db), Service(db, mp), crm, notificacoes);
        return await new ResolverOcorrenciaUseCase(new OcorrenciaRepository(db), reembolsar, db, new Relogio()).ExecuteAsync(input);
    }

    [SkippableFact]
    public async Task Ocorrencia_apuracao_e_encerramento_concorrentes_preservam_autoria_e_uma_nota()
    {
        var financeiro = await Preparar();
        var input = await PrepararOcorrencia(financeiro);
        using var mp = new ProvedorHttp(financeiro.PedidoId);
        async Task<OcorrenciaDto?> Apurar(Guid usuario, string nome)
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(input.EmpresaId);
            return await new ApurarOcorrenciaUseCase(new OcorrenciaRepository(db), db, new Relogio())
                .ExecuteAsync(input.EmpresaId, input.OcorrenciaId, usuario, nome, NivelAcesso.Gerente);
        }
        var apuracoes = await Task.WhenAll(Apurar(input.UsuarioId, "Primeira"), Apurar(Guid.NewGuid(), "Segunda"));
        apuracoes[0]!.ApuradaPorUsuarioId.Should().Be(apuracoes[1]!.ApuradaPorUsuarioId);
        var resultados = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ResolverOcorrencia(input, mp)));
        resultados.Should().OnlyContain(r => r!.Ocorrencia.Status == "resolvida");
        await FluentActions.Invoking(() => ResolverOcorrencia(input with { Resolucao = "Divergente" }, mp)).Should().ThrowAsync<CobrancaPedidoConflitoException>();
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(input.EmpresaId);
        (await conferir.ClienteNotas.CountAsync()).Should().Be(1);
        (await conferir.NotifEventos.CountAsync()).Should().Be(0);
        (await conferir.MovimentosCaixa.CountAsync()).Should().Be(0);
        mp.Chaves.Should().BeEmpty();
        (await Apurar(Guid.NewGuid(), "Depois"))!.ApuradaPorUsuarioId.Should().Be(apuracoes[0]!.ApuradaPorUsuarioId);
    }

    [SkippableFact]
    public async Task Ocorrencia_reembolso_em_voo_impede_encerramento_e_conclusao_concorrente_nao_duplica_efeitos()
    {
        var financeiro = await Preparar();
        var original = await PrepararOcorrencia(financeiro);
        var input = original with { Reembolsar = true, Valor = 40 };
        using var mp = new ProvedorHttp(financeiro.PedidoId);
        var enviado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reenviado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chamadas = 0;
        using var liberar = new ManualResetEventSlim();
        mp.AposPost = () =>
        {
            if (Interlocked.Increment(ref chamadas) == 1) enviado.TrySetResult(); else reenviado.TrySetResult();
            liberar.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
        };
        var primeira = Task.Run(() => ResolverOcorrencia(input, mp));
        Task<ResolverOcorrenciaResult?>? segunda = null;
        try
        {
            await enviado.Task.WaitAsync(TimeSpan.FromSeconds(20));
            segunda = Task.Run(() => ResolverOcorrencia(input, mp));
            await reenviado.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // Já há intenção persistida, mas o provedor ainda não entregou a resposta.
            await FluentActions.Invoking(() => ResolverOcorrencia(original, mp)).Should().ThrowAsync<CobrancaPedidoConflitoException>();
            await using var conferir = fixture.CreateDbContext();
            conferir.SetMobileTenantContext(input.EmpresaId);
            var aberta = await conferir.Ocorrencias.SingleAsync();
            aberta.ReembolsoSolicitadoEm.Should().NotBeNull();
            aberta.EstaAberta.Should().BeTrue();
            (await conferir.ClienteNotas.CountAsync()).Should().Be(0);
            (await conferir.MovimentosCaixa.CountAsync()).Should().Be(0);
        }
        finally { liberar.Set(); }
        await primeira;
        await segunda!;
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ResolverOcorrencia(input, mp)));
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        (await db.ClienteNotas.CountAsync()).Should().Be(1);
        (await db.NotifEventos.CountAsync()).Should().Be(1);
        (await db.MovimentosCaixa.CountAsync()).Should().Be(1);
        mp.Estornos.Should().ContainSingle();
        mp.Chaves.Should().HaveCount(2).And.OnlyContain(chave => chave == input.OcorrenciaId.ToString());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ocorrencia_falha_da_nota_reverte_encerramento_e_retoma_sem_repetir_dinheiro(bool reembolsar)
    {
        var financeiro = await Preparar();
        var original = await PrepararOcorrencia(financeiro);
        var input = original with { Reembolsar = reembolsar, Valor = reembolsar ? 40 : null };
        using var mp = new ProvedorHttp(financeiro.PedidoId);
        await FluentActions.Invoking(() => ResolverOcorrencia(input, mp, falharNota: true)).Should().ThrowAsync<InvalidOperationException>();
        await using (var conferir = fixture.CreateDbContext())
        {
            conferir.SetMobileTenantContext(input.EmpresaId);
            (await conferir.Ocorrencias.SingleAsync()).EstaAberta.Should().BeTrue();
            (await conferir.ClienteNotas.CountAsync()).Should().Be(0);
            (await conferir.NotifEventos.CountAsync()).Should().Be(0);
            (await conferir.MovimentosCaixa.CountAsync()).Should().Be(reembolsar ? 1 : 0);
        }
        (await ResolverOcorrencia(input, mp))!.Ocorrencia.Status.Should().Be("resolvida");
        mp.Chaves.Should().HaveCount(reembolsar ? 1 : 0);
    }

    [SkippableFact]
    public async Task Ocorrencias_do_pedido_preservam_historico_alem_de_cem_e_nao_vazam_tenant()
    {
        var financeiro = await Preparar();
        var input = await PrepararOcorrencia(financeiro);
        using var mp = new ProvedorHttp(financeiro.PedidoId);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        var o = await db.Ocorrencias.SingleAsync();
        for (var i = 0; i < 101; i++) db.Ocorrencias.Add(Ocorrencia.Abrir(input.EmpresaId, financeiro.PedidoId, o.ClienteId, null,
            OrigemOcorrencia.Dona, CategoriaOcorrencia.Outro, "Histórico", Agora.AddMinutes(-i - 1)));
        await db.SaveChangesAsync();
        var consulta = new ConsultarOcorrenciasUseCase(new OcorrenciaRepository(db), new PedidoRepository(db), Service(db, mp));
        (await consulta.DoPedidoAsync(input.EmpresaId, financeiro.PedidoId))!.Should().HaveCount(102);
        (await consulta.DoPedidoAsync(Guid.NewGuid(), financeiro.PedidoId)).Should().BeNull();
        await using var rls = fixture.CreateRlsClientDbContext();
        await rls.Database.OpenConnectionAsync();
        await rls.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {Guid.NewGuid().ToString()}, false)");
        (await rls.Ocorrencias.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await rls.Database.ExecuteSqlInterpolatedAsync($"UPDATE ocorrencias SET \"Resolucao\" = 'forjada' WHERE \"Id\" = {o.Id}")).Should().Be(0);
    }

    [SkippableFact]
    public async Task Parciais_replay_e_webhook_total_preservam_receita_e_uma_saida_por_estorno()
    {
        var input = await Preparar();
        using var mp = new ProvedorHttp(input.PedidoId);
        (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
        await Executar(input, mp);
        await Executar(input with { OperacaoId = Guid.NewGuid(), Valor = 60 }, mp);
        await FluentActions.Invoking(() => Executar(input with { OperacaoId = Guid.NewGuid(), Valor = 1 }, mp)).Should().ThrowAsync<CobrancaPedidoConflitoException>();
        mp.Chaves.Should().HaveCount(2);
        mp.Chaves[0].Should().Be(input.OperacaoId.ToString());
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        var service = Service(db, mp);
        await service.SincronizarPagamentoAsync(mp.Pagamento());
        await service.SincronizarPagamentoAsync(mp.Pagamento());
        var consulta = await service.ConsultarAsync(input.EmpresaId, input.PedidoId);
        consulta.Pagamentos.Single().Devolvido.Should().Be(100);
        consulta.Pagamentos.Single().Disponivel.Should().Be(0);
        consulta.Estornos.Should().HaveCount(2);
        (await db.CobrancasPedido.SingleAsync()).Status.Should().Be(StatusCobrancaPedido.Estornada);
        var caixa = new CaixaSaldoCalculator(new CaixaRepository(db));
        (await caixa.CalcularAsync(input.EmpresaId, new DateOnly(2026, 10, 9))).SaldoEsperado.Should().Be(100);
        (await caixa.CalcularAsync(input.EmpresaId, new DateOnly(2026, 10, 10))).TotalSaidas.Should().Be(100);
        (await db.Pedidos.Include(p => p.Pagamentos).SingleAsync()).TotalPago.Should().Be(100);
        (await db.MovimentosCaixa.CountAsync()).Should().Be(2);
    }

    [SkippableFact]
    public async Task Resposta_perdida_reserva_saldo_e_replay_usa_a_mesma_chave()
    {
        var input = await Preparar();
        using var mp = new ProvedorHttp(input.PedidoId) { PerderResposta = true };
        (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Pendente);
        await FluentActions.Invoking(() => Executar(input with { OperacaoId = Guid.NewGuid() }, mp)).Should().ThrowAsync<CobrancaPedidoConflitoException>();
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(input.EmpresaId);
            var consulta = await Service(db, mp).ConsultarAsync(input.EmpresaId, input.PedidoId);
            consulta.Pagamentos.Single().Reservado.Should().Be(40);
            (await db.MovimentosCaixa.CountAsync()).Should().Be(0);
            (await Service(db, mp).RetomarAsync(input.EmpresaId, input.PedidoId, input.OperacaoId, NivelAcesso.Admin)).Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
        }
        mp.Chaves.Should().Equal(input.OperacaoId.ToString(), input.OperacaoId.ToString());
        mp.Estornos.Should().ContainSingle();
        (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
        mp.Chaves.Should().HaveCount(2);
    }

    [SkippableTheory]
    [InlineData("pending")]
    [InlineData(null)]
    public async Task Pendente_e_status_ausente_nao_movimentam_caixa_e_consulta_confirma_sem_novo_post(string? status)
    {
        var input = await Preparar();
        using var mp = new ProvedorHttp(input.PedidoId) { ProximoStatus = status };
        var pendente = await Executar(input, mp);
        pendente.Situacao.Should().Be(PedidoEstornoOnline.Pendente);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        (await db.MovimentosCaixa.CountAsync()).Should().Be(0);
        mp.AprovarTodos();
        await Service(db, mp).SincronizarPagamentoAsync(mp.Pagamento());
        (await Service(db, mp).ConsultarAsync(input.EmpresaId, input.PedidoId)).Estornos.Single().Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
        mp.Chaves.Should().ContainSingle();
        (await db.MovimentosCaixa.CountAsync()).Should().Be(1);
    }

    [SkippableTheory]
    [InlineData("pagamento")]
    [InlineData("valor")]
    [InlineData("id")]
    public async Task Resposta_divergente_nao_confirma_e_nao_libera_nova_operacao(string campo)
    {
        var input = await Preparar();
        using var mp = new ProvedorHttp(input.PedidoId) { AlterarResposta = r => campo switch {
            "pagamento" => r with { PagamentoId = "999" }, "valor" => r with { Valor = 41 }, _ => r with { EstornoId = "../invalido" } } };
        (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Pendente);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        (await db.MovimentosCaixa.CountAsync()).Should().Be(0);
        await FluentActions.Invoking(() => Executar(input with { OperacaoId = Guid.NewGuid() }, mp)).Should().ThrowAsync<CobrancaPedidoConflitoException>();
        mp.AlterarResposta = null;
        (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
    }

    [SkippableFact]
    public async Task Concorrencia_de_duas_solicitacoes_e_replay_nao_duplicam_dinheiro()
    {
        var input = (await Preparar()) with { Valor = 70 };
        using var mp = new ProvedorHttp(input.PedidoId);
        async Task<bool> Tentar(SolicitarEstornoOnlineInput cmd)
        {
            try { await Executar(cmd, mp); return true; }
            catch (CobrancaPedidoConflitoException) { return false; }
        }
        (await Task.WhenAll(Tentar(input), Tentar(input with { OperacaoId = Guid.NewGuid() }))).Count(v => v).Should().Be(1);
        var outro = await Preparar();
        using var mp2 = new ProvedorHttp(outro.PedidoId);
        var r = await Task.WhenAll(Executar(outro, mp2), Executar(outro, mp2));
        r.Should().OnlyContain(e => e.Situacao == PedidoEstornoOnline.Confirmado);
        mp2.Estornos.Should().ContainSingle();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(outro.EmpresaId);
        (await db.MovimentosCaixa.CountAsync()).Should().Be(1);
    }

    [SkippableFact]
    public async Task Falha_do_banco_apos_provedor_preserva_intencao_para_retomada()
    {
        var input = await Preparar();
        using var mp = new ProvedorHttp(input.PedidoId);
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(input.EmpresaId);
            mp.AposPost = () => db.Set<PedidoEvento>().Add(new PedidoEvento { Id = Guid.NewGuid(), PedidoId = Guid.NewGuid(), Tipo = "falha", OcorridoEm = Agora });
            await FluentActions.Invoking(() => Service(db, mp).SolicitarAsync(input)).Should().ThrowAsync<DbUpdateException>();
        }
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(input.EmpresaId);
            (await db.Set<PedidoEstornoOnline>().SingleAsync()).Situacao.Should().Be(PedidoEstornoOnline.Pendente);
            (await db.MovimentosCaixa.CountAsync()).Should().Be(0);
        }
        mp.AposPost = null;
        (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
        mp.Estornos.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task Estorno_externo_e_webhook_repetido_limitam_nova_devolucao()
    {
        var input = (await Preparar()) with { Valor = 80 };
        using var mp = new ProvedorHttp(input.PedidoId);
        mp.AdicionarExterno(30);
        (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Recusado);
        mp.Chaves.Should().BeEmpty();
        await Executar(input with { OperacaoId = Guid.NewGuid(), Valor = 70 }, mp);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        await Service(db, mp).SincronizarPagamentoAsync(mp.Pagamento());
        (await db.MovimentosCaixa.SumAsync(m => m.Valor)).Should().Be(100);
        (await db.Set<PedidoEstornoOnline>().CountAsync(e => e.Situacao == PedidoEstornoOnline.Confirmado)).Should().Be(2);
    }

    [SkippableFact]
    public async Task Perfil_empresa_valores_forged_reuso_e_caixa_fechado_nao_enviam_ao_provedor()
    {
        var input = await Preparar();
        var outro = await Preparar();
        using var mp = new ProvedorHttp(input.PedidoId);
        await FluentActions.Invoking(() => Executar(input with { NivelSolicitante = NivelAcesso.Operador }, mp)).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Invoking(() => Executar(input with { EmpresaId = outro.EmpresaId }, mp)).Should().ThrowAsync<CobrancaPedidoNaoEncontradoException>();
        await FluentActions.Invoking(() => Executar(input with { PagamentoId = outro.PagamentoId }, mp)).Should().ThrowAsync<CobrancaPedidoConflitoException>();
        foreach (var i in new[] { input with { Valor = -1 }, input with { Valor = 1.001m }, input with { Motivo = " " } })
            await FluentActions.Invoking(() => Executar(i, mp)).Should().ThrowAsync<UseCaseValidationException>();
        mp.Chaves.Should().BeEmpty();
        await Executar(input, mp);
        await FluentActions.Invoking(() => Executar(input with { Valor = 41 }, mp)).Should().ThrowAsync<CobrancaPedidoConflitoException>();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        db.FechamentosCaixa.Add(FechamentoCaixa.Criar(input.EmpresaId, new DateOnly(2026, 10, 10), 0, 0, 0, 0, 40));
        await db.SaveChangesAsync();
        await FluentActions.Invoking(() => Executar(input with { OperacaoId = Guid.NewGuid() }, mp)).Should().ThrowAsync<UseCaseValidationException>();
        mp.Chaves.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task Contestacao_apos_parcial_desconta_apenas_o_restante_e_legado_nao_gera_segunda_saida()
    {
        var input = await Preparar();
        using var mp = new ProvedorHttp(input.PedidoId);
        await Executar(input, mp);
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(input.EmpresaId);
            var contestado = mp.Pagamento() with { Status = "charged_back" };
            await Service(db, mp).SincronizarPagamentoAsync(contestado);
            await Service(db, mp).SincronizarPagamentoAsync(contestado);
            (await db.MovimentosCaixa.SumAsync(m => m.Valor)).Should().Be(100);
            (await db.MovimentosCaixa.CountAsync()).Should().Be(2);
        }
        var legado = await Preparar();
        using var mpLegado = new ProvedorHttp(legado.PedidoId);
        mpLegado.AdicionarExterno(100);
        await using var antigo = fixture.CreateDbContext();
        antigo.SetMobileTenantContext(legado.EmpresaId);
        (await antigo.CobrancasPedido.SingleAsync()).MarcarEstornada("legado", Agora.AddDays(-1));
        await antigo.SaveChangesAsync();
        (await Service(antigo, mpLegado).SincronizarPagamentoAsync(mpLegado.Pagamento())).Should().BeFalse();
        (await antigo.MovimentosCaixa.CountAsync()).Should().Be(0);
        (await Service(antigo, mpLegado).ConsultarAsync(legado.EmpresaId, legado.PedidoId)).Pagamentos.Single().Legado.Should().BeTrue();
    }

    [SkippableTheory]
    [InlineData(400, "recusado")]
    [InlineData(503, "pendente")]
    public async Task Recusa_http_inicial_e_indisponibilidade_tem_estados_distintos(int status, string situacao)
    {
        var input = await Preparar();
        using var mp = new ProvedorHttp(input.PedidoId) { ErroPost = (HttpStatusCode)status };
        (await Executar(input, mp)).Situacao.Should().Be(situacao);
        mp.Estornos.Should().BeEmpty();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        (await db.MovimentosCaixa.CountAsync()).Should().Be(0);
        mp.ErroPost = null;
        if (situacao == PedidoEstornoOnline.Pendente)
            (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
        else (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Recusado);
    }

    [SkippableFact]
    public async Task Recusa_no_replay_de_resposta_perdida_nao_libera_reserva()
    {
        var input = await Preparar();
        using var mp = new ProvedorHttp(input.PedidoId) { PerderResposta = true };
        await Executar(input, mp);
        mp.ErroPost = HttpStatusCode.BadRequest;
        (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Pendente);
        await FluentActions.Invoking(() => Executar(input with { OperacaoId = Guid.NewGuid() }, mp)).Should().ThrowAsync<CobrancaPedidoConflitoException>();
        mp.ErroPost = null;
        (await Executar(input, mp)).Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
        mp.Estornos.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task Rls_bloqueia_outra_empresa_e_fk_preserva_recebimento_com_intencao_pendente()
    {
        var input = await Preparar();
        var outro = await Preparar();
        using var mp = new ProvedorHttp(input.PedidoId) { ProximoStatus = "pending" };
        await Executar(input, mp);
        await using (var db = fixture.CreateRlsClientDbContext())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {outro.EmpresaId.ToString()}, false)");
            (await db.Set<PedidoEstornoOnline>().IgnoreQueryFilters().CountAsync()).Should().Be(0);
            await FluentActions.Invoking(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pedido_estornos_online ("Id","EmpresaId","PedidoId","PagamentoId","PagamentoExternoId","Valor","Motivo","Situacao","CriadoEm")
                VALUES ({Guid.NewGuid()}, {input.EmpresaId}, {input.PedidoId}, {input.PagamentoId}, '123456789', 1, 'teste', 'pendente', now())
                """)).Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.SqlState == "42501");
        }
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(input.EmpresaId);
        conferir.Set<PedidoPagamento>().Remove(await conferir.Set<PedidoPagamento>().SingleAsync(p => p.Id == input.PagamentoId));
        await FluentActions.Invoking(() => conferir.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [SkippableFact]
    public async Task Id_da_solicitacao_nao_colide_com_outro_lancamento_do_caixa()
    {
        var input = await Preparar();
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(input.EmpresaId);
            var entrada = MovimentoCaixa.Criar(input.EmpresaId, "entrada", 5, Agora);
            entrada.Id = input.OperacaoId;
            db.MovimentosCaixa.Add(entrada);
            await db.SaveChangesAsync();
        }
        using var mp = new ProvedorHttp(input.PedidoId);
        var estorno = await Executar(input, mp);
        estorno.Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
        estorno.MovimentoCaixaId.Should().NotBe(input.OperacaoId);
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(input.EmpresaId);
        (await conferir.MovimentosCaixa.SingleAsync(m => m.Id == input.OperacaoId)).Tipo.Should().Be("entrada");
        (await conferir.MovimentosCaixa.SingleAsync(m => m.Id == estorno.MovimentoCaixaId)).Valor.Should().Be(40);
    }

    // Transporte HTTP controlado: exercita rotas, JSON, headers e timeout do adapter real sem transferir dinheiro.
    private sealed class ProvedorHttp : HttpMessageHandler
    {
        private readonly Guid _pedidoId;
        private readonly object _gate = new();
        public readonly Dictionary<string, EstornoMercadoPagoResult> Estornos = new();
        public readonly List<string> Chaves = [];
        public string? ProximoStatus { get; set; } = "approved";
        public bool PerderResposta { get; set; }
        public HttpStatusCode? ErroPost { get; set; }
        public Func<EstornoMercadoPagoResult, EstornoMercadoPagoResult>? AlterarResposta { get; set; }
        public Action? AposPost { get; set; }
        public MercadoPagoClient Cliente { get; }
        public ProvedorHttp(Guid pedidoId)
        {
            _pedidoId = pedidoId;
            Cliente = new(new HttpClient(this, false) { BaseAddress = new Uri("https://mp.test/") },
                Options.Create(new MercadoPagoOptions { AccessToken = "teste-local" }), NullLogger<MercadoPagoClient>.Instance);
        }
        public void AprovarTodos() { lock (_gate) foreach (var k in Estornos.Keys.ToArray()) Estornos[k] = Estornos[k] with { Status = "approved" }; }
        public void AdicionarExterno(decimal valor) { lock (_gate) Estornos[Guid.NewGuid().ToString()] = new((Estornos.Count + 1000).ToString(), valor, "approved", "123456789", Agora); }
        public PagamentoMercadoPago Pagamento()
        {
            lock (_gate)
            {
                var total = Estornos.Values.Where(e => e.Status == "approved").Sum(e => e.Valor!.Value);
                return new("123456789", total == 100 ? "refunded" : "approved", null, _pedidoId.ToString(), 100, Agora.AddDays(-1), "pix", "bank_transfer", total);
            }
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.Headers.Authorization!.ToString().Should().Be("Bearer teste-local");
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                path.Should().Be("/v1/payments/123456789/refunds");
                var chave = request.Headers.GetValues("X-Idempotency-Key").Single();
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                EstornoMercadoPagoResult r;
                lock (_gate)
                {
                    Chaves.Add(chave);
                    if (ErroPost is { } erro) throw new HttpRequestException("Erro HTTP sintético", null, erro);
                    if (!Estornos.TryGetValue(chave, out r!))
                        Estornos[chave] = r = new((Estornos.Count + 1000).ToString(), body.RootElement.GetProperty("amount").GetDecimal(), ProximoStatus, "123456789", Agora);
                    if (PerderResposta) { PerderResposta = false; throw new HttpRequestException("Resposta perdida após confirmação sintética"); }
                }
                AposPost?.Invoke();
                return Json(Render(AlterarResposta?.Invoke(r) ?? r));
            }
            lock (_gate)
            {
                if (path == "/v1/payments/123456789")
                {
                    var p = Pagamento();
                    return Json(new { id = p.Id, status = p.Status, external_reference = p.ExternalReference, transaction_amount = p.TransactionAmount, transaction_amount_refunded = p.TransactionAmountRefunded });
                }
                if (path == "/v1/payments/123456789/refunds") return Json(Estornos.Values.Select(Render).ToArray());
                var estorno = Estornos.Values.SingleOrDefault(e => path == $"/v1/payments/123456789/refunds/{e.EstornoId}");
                return estorno is null ? new(HttpStatusCode.NotFound) : Json(Render(estorno));
            }
        }
        private static object Render(EstornoMercadoPagoResult r) => new { id = r.EstornoId, payment_id = r.PagamentoId, amount = r.Valor, status = r.Status, date_created = r.CriadoEm };
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
