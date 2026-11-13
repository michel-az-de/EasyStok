using System.Reflection;
using EasyStock.Api.BackgroundServices;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Pagamentos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.BackgroundServices;

public sealed class CobrancaPedidoJobRotacaoTests
{
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Cobrancas_que_sempre_falham_nao_prendem_a_pagina_do_pedido_seguinte(int ruins)
    {
        using var cenario = new Cenario(ruins, saudaveis: 1);
        for (var pagina = 0; pagina < ruins / 50; pagina++)
        {
            await cenario.RodarAsync();
            cenario.Tentadas.Should().HaveCount(50, "a rodada continua limitada mesmo quando todas falham");
            cenario.Tentadas.Should().OnlyContain(id => cenario.PedidosComFalha.Contains(id));
        }

        await cenario.RodarAsync();
        cenario.Tentadas.Should().Equal(cenario.PedidosSaudaveis);
        cenario.Pendentes().Should().Be(ruins, "o pedido alcançado foi processado pelo use case real");

        await cenario.RodarAsync();
        cenario.Tentadas.Should().HaveCount(50, "a página curta devolve a fila ao início, sem abandonar as falhas");
        cenario.Tentadas.Should().OnlyContain(id => cenario.PedidosComFalha.Contains(id));
    }

    [Fact]
    public async Task Pagina_que_some_apos_sucesso_nao_faz_o_resto_da_fila_ser_abandonado()
    {
        using var cenario = new Cenario(ruins: 0, saudaveis: 100);
        await cenario.RodarAsync();
        cenario.Tentadas.Should().HaveCount(50);
        var primeiras = cenario.Tentadas.ToArray();
        cenario.Pendentes().Should().Be(50);

        // Os cinquenta processados saíram da consulta; o offset encontra uma página vazia.
        await cenario.RodarAsync();
        cenario.Tentadas.Should().BeEmpty();

        await cenario.RodarAsync();
        cenario.Tentadas.Should().HaveCount(50);
        cenario.Tentadas.Should().NotIntersectWith(primeiras);
        cenario.Pendentes().Should().Be(0);
    }

    [Fact]
    public async Task Cancelamento_no_meio_da_rodada_nao_pula_a_pagina_na_retomada()
    {
        using var cenario = new Cenario(ruins: 0, saudaveis: 51);
        using var cts = new CancellationTokenSource();
        cenario.DuranteConsulta = (_, token) =>
        {
            token.Should().Be(cts.Token, "o desligamento do host precisa alcançar a consulta do provedor");
            cts.Cancel();
            token.ThrowIfCancellationRequested();
        };
        var rodar = () => cenario.RodarAsync(cts.Token);
        await rodar.Should().ThrowAsync<OperationCanceledException>();
        var primeiro = cenario.Tentadas.Single();
        cenario.Pendentes().Should().Be(51);

        cenario.DuranteConsulta = null;
        await cenario.RodarAsync();
        cenario.Tentadas.Should().HaveCount(50).And.Contain(primeiro);
        cenario.Pendentes().Should().Be(1);
    }

    [Fact]
    public async Task Reiniciar_o_job_recomeca_no_inicio_sem_retirar_cobrancas_da_fila()
    {
        using var cenario = new Cenario(ruins: 50, saudaveis: 1);
        await cenario.RodarAsync();
        var primeiraPagina = cenario.Tentadas.ToArray();

        cenario.VencerNovas(2);
        cenario.ReiniciarJob();
        await cenario.RodarAsync();
        cenario.Tentadas.Should().Equal(primeiraPagina);
        cenario.Pendentes().Should().Be(53);

        await cenario.RodarAsync();
        cenario.Tentadas.Should().Equal(cenario.PedidosSaudaveis);
    }

    [Fact]
    public async Task Novos_vencimentos_continuos_nao_abandonam_cobrancas_anteriores()
    {
        using var cenario = new Cenario(ruins: 0, saudaveis: 100);
        var anteriores = cenario.PedidosSaudaveis.ToArray();
        var tentadas = new HashSet<Guid>();
        IReadOnlyList<Guid> primeirasNovas = [];

        for (var rodada = 0; rodada < 6; rodada++)
        {
            await cenario.RodarAsync();
            cenario.Tentadas.Should().HaveCountLessThanOrEqualTo(50);
            tentadas.UnionWith(cenario.Tentadas);
            var novas = cenario.VencerNovas(100);
            if (rodada == 0) primeirasNovas = novas;
        }

        anteriores.Except(tentadas).Should().BeEmpty(
            "novos vencimentos não podem impedir o retorno às cobranças puladas quando a página anterior saiu da fila");
        primeirasNovas.Any(tentadas.Contains).Should().BeTrue(
            "voltar ao início precisa renovar o corte para incluir as cobranças que venceram depois");
    }

    [Fact]
    public async Task Novos_vencimentos_nao_impedem_a_retentativa_das_falhas_anteriores()
    {
        using var cenario = new Cenario(ruins: 50, saudaveis: 1);
        await cenario.RodarAsync();
        var primeiraPagina = cenario.Tentadas.ToArray();

        cenario.VencerNovas(100);
        await cenario.RodarAsync();
        cenario.Tentadas.Should().Contain(cenario.PedidosSaudaveis[0]);

        cenario.VencerNovas(100);
        await cenario.RodarAsync();
        cenario.Tentadas.Should().Equal(primeiraPagina,
            "a varredura precisa terminar e tentar de novo as falhas mesmo com novas cobranças vencendo");
    }

    [Fact]
    public async Task Cancelamento_conserva_o_corte_da_varredura_na_retomada()
    {
        using var cenario = new Cenario(ruins: 50, saudaveis: 1);
        await cenario.RodarAsync();
        var anterior = cenario.PedidosSaudaveis.Single();
        cenario.VencerNovas(100);
        using var cts = new CancellationTokenSource();
        cenario.DuranteConsulta = (_, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
        };
        var rodar = () => cenario.RodarAsync(cts.Token);
        await rodar.Should().ThrowAsync<OperationCanceledException>();
        cenario.Tentadas.Should().Equal(anterior);

        cenario.DuranteConsulta = null;
        cenario.VencerNovas(100);
        await cenario.RodarAsync();
        cenario.Tentadas.Should().Equal(new[] { anterior },
            "cancelar não troca o corte nem inclui cobranças de uma varredura seguinte");
    }

    // Exercita seleção, tentativas e remoção da consulta pelo job e repositório reais. InMemory não
    // valida locks/transações; a tradução de ordenação e paginação tem cenário próprio em PostgreSQL.
    private sealed class Cenario : IDisposable
    {
        private static readonly DateTime Agora = new(2026, 10, 25, 15, 0, 0, DateTimeKind.Utc);
        private static readonly MethodInfo Rodada = typeof(CobrancaPedidoJob).GetMethod(
            "ProcessarVencidasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private readonly DbContextOptions<EasyStockDbContext> _options;
        private readonly ServiceProvider _services;
        private readonly RelogioControlado _relogio = new();
        private CobrancaPedidoJob _job;
        public List<Guid> Tentadas { get; } = [];
        public HashSet<Guid> PedidosComFalha { get; } = [];
        public List<Guid> PedidosSaudaveis { get; } = [];
        public Action<Guid, CancellationToken>? DuranteConsulta { get; set; }

        public Cenario(int ruins, int saudaveis)
        {
            _options = new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"fila-cobrancas-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            using (var db = new EasyStockDbContext(_options))
            {
                var empresaId = Guid.NewGuid();
                for (var i = 0; i < ruins + saudaveis; i++)
                {
                    var pedido = Pedido.Criar(empresaId);
                    pedido.Status = StatusPedidoMapper.Preparando;
                    db.Pedidos.Add(pedido);
                    db.CobrancasPedido.Add(CobrancaPedido.CriarOnline(empresaId, pedido.Id, 25m,
                        $"pref-{pedido.Id:N}", "https://mp.test/link", Agora.AddHours(-1).AddSeconds(i), 1, Agora.AddHours(-2)));
                    if (i < ruins) PedidosComFalha.Add(pedido.Id);
                    else PedidosSaudaveis.Add(pedido.Id);
                }
                db.SaveChanges();
            }

            var mercadoPago = Substitute.For<IMercadoPagoClient>();
            mercadoPago.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).Returns("conta-teste");
            mercadoPago.BuscarPagamentosPorReferenciaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var pedidoId = Guid.Parse(call.Arg<string>());
                Tentadas.Add(pedidoId);
                DuranteConsulta?.Invoke(pedidoId, call.Arg<CancellationToken>());
                if (PedidosComFalha.Contains(pedidoId)) throw new HttpRequestException("Falha persistente desta cobrança");
                return Task.FromResult<IReadOnlyList<PagamentoMercadoPago>>([]);
            });

            var services = new ServiceCollection();
            services.AddScoped(_ => new EasyStockDbContext(_options));
            services.AddScoped<ICobrancaPedidoRepository, CobrancaPedidoRepository>();
            services.AddScoped(sp => new RetomarCobrancasAguardandoCredencialUseCase(mercadoPago,
                sp.GetRequiredService<ICobrancaPedidoRepository>(), NullLogger<RetomarCobrancasAguardandoCredencialUseCase>.Instance));
            services.AddScoped(sp =>
            {
                var db = sp.GetRequiredService<EasyStockDbContext>();
                var pedidos = Substitute.For<IPedidoStorefrontRepository>();
                pedidos.GetForUpdateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    var empresaId = call.ArgAt<Guid>(0);
                    var pedidoId = call.ArgAt<Guid>(1);
                    return db.Pedidos.SingleOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == pedidoId, call.Arg<CancellationToken>());
                });
                var tenant = Substitute.For<ITenantContextAccessor>();
                tenant.When(t => t.SetCurrentTenant(Arg.Any<Guid>())).Do(call => db.SetMobileTenantContext(call.Arg<Guid>()));
                return new ProcessarCobrancaVencidaUseCase(pedidos, sp.GetRequiredService<ICobrancaPedidoRepository>(),
                    null!, null!, null!, mercadoPago, null!, tenant, db, _relogio,
                    NullLogger<ProcessarCobrancaVencidaUseCase>.Instance, null!, null!);
            });
            _services = services.BuildServiceProvider();
            _job = CriarJob();
        }

        public Task RodarAsync(CancellationToken ct = default)
        {
            Tentadas.Clear();
            return (Task)Rodada.Invoke(_job, [ct])!;
        }

        public IReadOnlyList<Guid> VencerNovas(int quantidade)
        {
            // São criadas ainda válidas e vencem somente ao avançar o relógio da rodada.
            var criadasEm = _relogio.GetUtcNow().UtcDateTime;
            var novas = new List<Guid>();
            using var db = new EasyStockDbContext(_options);
            var empresaId = Guid.NewGuid();
            for (var i = 0; i < quantidade; i++)
            {
                var pedido = Pedido.Criar(empresaId);
                pedido.Status = StatusPedidoMapper.Preparando;
                db.Pedidos.Add(pedido);
                db.CobrancasPedido.Add(CobrancaPedido.CriarOnline(empresaId, pedido.Id, 25m,
                    $"pref-{pedido.Id:N}", "https://mp.test/link", criadasEm.AddSeconds(30).AddMilliseconds(i), 1, criadasEm));
                novas.Add(pedido.Id);
                PedidosSaudaveis.Add(pedido.Id);
            }
            db.SaveChanges();
            _relogio.Avancar(TimeSpan.FromMinutes(1));
            return novas;
        }

        public int Pendentes()
        {
            using var db = new EasyStockDbContext(_options);
            return db.CobrancasPedido.IgnoreQueryFilters().Count(c => c.Status == StatusCobrancaPedido.Pendente);
        }

        private CobrancaPedidoJob CriarJob() => new(_services, _relogio, NullLogger<CobrancaPedidoJob>.Instance);

        public void ReiniciarJob()
        {
            _job.Dispose();
            _job = CriarJob();
        }

        public void Dispose()
        {
            _job.Dispose();
            _services.Dispose();
        }

        private sealed class RelogioControlado : TimeProvider
        {
            private DateTimeOffset _agora = new(Agora);
            public override DateTimeOffset GetUtcNow() => _agora;
            public void Avancar(TimeSpan intervalo) => _agora += intervalo;
        }
    }
}
