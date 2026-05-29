using EasyStock.Application.UseCases.Storefront.Aprovacao;
using EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Integration;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

/// <summary>
/// Testa a serialização do <c>SELECT FOR UPDATE</c> no fluxo de aprovação/recusa
/// storefront (TASK-EZ-APROVAR-001, ADR-0014).
///
/// <para>
/// Com 2 transações concorrentes tentando aprovar/recusar o MESMO pedido,
/// exatamente uma vence (200) e a outra recebe <see cref="PedidoJaResolvidoException"/>
/// (mapeada para 409 pelo controller).
/// </para>
///
/// <para>
/// O lock pessimista é validado contra o Postgres real (Testcontainers) — não
/// é possível reproduzir o cenário em SQLite/InMemory porque o <c>FOR UPDATE</c>
/// é descartado silenciosamente nesses providers.
/// </para>
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class AprovacaoConcorrenciaTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Aprovar_DuasRequestsSimultaneas_ApenasUmaVence()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        await fixture.ResetDatabaseAsync();

        // ── Setup: Empresa + Pedido em AguardandoAprovacaoBaba ──────────────
        var (empresaId, pedidoId) = await SeedPedidoAguardandoAprovacaoAsync();

        // ── Execução concorrente: 2 aprovações simultâneas ──────────────────
        var task1 = AprovarAsync(empresaId, pedidoId);
        var task2 = AprovarAsync(empresaId, pedidoId);

        var results = await Task.WhenAll(
            CapturarResultadoAsync(task1),
            CapturarResultadoAsync(task2));

        // ── Asserções ────────────────────────────────────────────────────────
        var sucessos = results.Count(r => r.Sucesso);
        var falhas = results.Count(r => !r.Sucesso);

        sucessos.Should().Be(1, "exatamente 1 aprovação deve vencer o lock");
        falhas.Should().Be(1, "a outra deve receber PedidoJaResolvidoException");

        results.Where(r => !r.Sucesso).Single().Excecao
            .Should().BeOfType<PedidoJaResolvidoException>(
                "lock pessimista FOR UPDATE serializa as transações; a 2ª lê status mudado e lança 409");

        // Pedido final está em AprovadoBaba (não em estado inconsistente).
        await using var db = fixture.CreateDbContext();
        var pedidoFinal = await db.Pedidos.FindAsync(pedidoId);
        pedidoFinal.Should().NotBeNull();
        pedidoFinal!.Status.Should().Be(StatusPedidoMapper.AprovadoBaba);
        pedidoFinal.AprovadoEm.Should().NotBeNull();
        pedidoFinal.AprovadoPorUsuarioId.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task AprovarVsRecusar_Simultaneos_ApenasUmaVence()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        await fixture.ResetDatabaseAsync();

        var (empresaId, pedidoId) = await SeedPedidoAguardandoAprovacaoAsync();

        var taskAprovar = AprovarAsync(empresaId, pedidoId);
        var taskRecusar = RecusarAsync(empresaId, pedidoId);

        var results = await Task.WhenAll(
            CapturarResultadoAsync(taskAprovar),
            CapturarResultadoAsync(taskRecusar));

        results.Count(r => r.Sucesso).Should().Be(1, "exatamente uma ação vence o lock");
        results.Count(r => !r.Sucesso).Should().Be(1, "a outra recebe 409");
        results.Where(r => !r.Sucesso).Single().Excecao
            .Should().BeOfType<PedidoJaResolvidoException>();
    }

    [SkippableFact]
    public async Task Recusar_DuasRequestsSimultaneas_ApenasUmaVence()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        await fixture.ResetDatabaseAsync();

        var (empresaId, pedidoId) = await SeedPedidoAguardandoAprovacaoAsync();

        var t1 = RecusarAsync(empresaId, pedidoId);
        var t2 = RecusarAsync(empresaId, pedidoId);

        var results = await Task.WhenAll(
            CapturarResultadoAsync(t1),
            CapturarResultadoAsync(t2));

        results.Count(r => r.Sucesso).Should().Be(1);
        results.Count(r => !r.Sucesso).Should().Be(1);
        results.Where(r => !r.Sucesso).Single().Excecao
            .Should().BeOfType<PedidoJaResolvidoException>();

        await using var db = fixture.CreateDbContext();
        var pedidoFinal = await db.Pedidos.FindAsync(pedidoId);
        pedidoFinal!.Status.Should().Be(StatusPedidoMapper.Cancelado);
        pedidoFinal.RecusadoEm.Should().NotBeNull();
        pedidoFinal.MotivoRecusa.Should().NotBeNullOrWhiteSpace();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(Guid EmpresaId, Guid PedidoId)> SeedPedidoAguardandoAprovacaoAsync()
    {
        await using var db = fixture.CreateDbContext();

        var empresaId = Guid.NewGuid();
        db.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Aprovacao",
            Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        });

        var pedido = Pedido.Criar(empresaId, origem: "storefront");
        pedido.Status = StatusPedidoMapper.AguardandoAprovacaoBaba;
        pedido.ClienteNome = "Cliente Concorrência";
        pedido.ClienteTelefone = "11999990000";
        pedido.Total = Dinheiro.FromDecimal(120m);
        db.Pedidos.Add(pedido);

        await db.SaveChangesAsync();

        return (empresaId, pedido.Id);
    }

    private Task AprovarAsync(Guid empresaId, Guid pedidoId) =>
        ExecutarUseCaseAsync(async (pedidoRepo, publicador, uow) =>
        {
            var useCase = new AprovarPedidoStorefrontUseCase(
                pedidoRepo, publicador, uow,
                NullLogger<AprovarPedidoStorefrontUseCase>.Instance);

            await useCase.ExecuteAsync(new AprovarPedidoStorefrontInput(
                PedidoId: pedidoId,
                EmpresaId: empresaId,
                UsuarioId: Guid.NewGuid(),
                UsuarioNome: "Babá Concorrência"));
        });

    private Task RecusarAsync(Guid empresaId, Guid pedidoId) =>
        ExecutarUseCaseAsync(async (pedidoRepo, publicador, uow) =>
        {
            var useCase = new RecusarPedidoStorefrontUseCase(
                pedidoRepo, publicador, uow,
                NullLogger<RecusarPedidoStorefrontUseCase>.Instance);

            await useCase.ExecuteAsync(new RecusarPedidoStorefrontInput(
                PedidoId: pedidoId,
                EmpresaId: empresaId,
                UsuarioId: Guid.NewGuid(),
                Motivo: MotivoRecusa.Operacional,
                MensagemCliente: "Reagende, por favor."));
        });

    private async Task ExecutarUseCaseAsync(
        Func<PedidoStorefrontRepository, PublicadorEventoIntegracao, EasyStockDbContext, Task> action)
    {
        // Cada concorrente abre seu próprio DbContext (= conexão própria) — só
        // assim os locks FOR UPDATE de transações distintas competem de verdade.
        await using var db = fixture.CreateDbContext();
        var pedidoRepo = new PedidoStorefrontRepository(db);
        var outboxRepo = new OutboxEventoIntegracaoRepository(db);
        var publicador = new PublicadorEventoIntegracao(
            outboxRepo, NullLogger<PublicadorEventoIntegracao>.Instance);

        await action(pedidoRepo, publicador, db);
    }

    private static async Task<(bool Sucesso, Exception? Excecao)> CapturarResultadoAsync(Task task)
    {
        try
        {
            await task;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }
}
