using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

/// <summary>
/// Testa a atomicidade do INSERT da <see cref="VagaOcupada"/> sob carga concorrente (ADR-0014).
/// Garante que com 1 vaga disponível, exatamente 1 request tem sucesso e o restante recebe 409.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class VagaOcupadaConcorrenciaTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task OcuparAsync_DuasRequestsSimultaneas_ApenasUmaVenceConcorrencia()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        // ── Setup ───────────────────────────────────────────────────────
        await using var dbSetup = fixture.CreateDbContext();
        await dbSetup.Database.MigrateAsync();

        var empresaId = Guid.NewGuid();
        dbSetup.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Concorrência",
            Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        });

        var storefront = Domain.Entities.Storefront.Storefront.Criar(
            empresaId: empresaId,
            slug: $"sf-conc-{Guid.NewGuid():N}",
            tituloPublico: "SF Concorrência",
            pedidoMinimoEntrega: 0m);
        storefront.Ativar();
        dbSetup.Storefronts.Add(storefront);

        // Janela com capacidade 1 (pior caso — 2 pedidos disputam a única vaga)
        var janela = JanelaEntrega.Criar(
            storefrontId: storefront.Id,
            diaDaSemana: 1, // segunda
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: 1,
            label: "Capacidade 1");
        dbSetup.JanelasEntrega.Add(janela);

        // 2 pedidos em rascunho
        var pedido1 = CriarPedidoRascunho(empresaId);
        var pedido2 = CriarPedidoRascunho(empresaId);
        dbSetup.Pedidos.Add(pedido1);
        dbSetup.Pedidos.Add(pedido2);

        await dbSetup.SaveChangesAsync();

        var dataEntrega = new DateOnly(2026, 6, 1); // próxima segunda

        // ── Execução concorrente ────────────────────────────────────────
        var task1 = OcuparAsync(fixture, janela.Id, dataEntrega, pedido1.Id);
        var task2 = OcuparAsync(fixture, janela.Id, dataEntrega, pedido2.Id);

        var results = await Task.WhenAll(
            CapturarResultadoAsync(task1),
            CapturarResultadoAsync(task2));

        // ── Asserções ────────────────────────────────────────────────────
        var sucessos = results.Count(r => r.Sucesso);
        var falhas = results.Count(r => !r.Sucesso);

        sucessos.Should().Be(1, "exatamente 1 request deve ocupar a única vaga");
        falhas.Should().Be(1, "o outro deve receber JanelaSemVagasException");

        results.Where(r => !r.Sucesso).Single().Excecao
            .Should().BeOfType<JanelaSemVagasException>();
    }

    private static async Task OcuparAsync(
        PostgreSqlDatabaseFixture fixture,
        Guid janelaId,
        DateOnly data,
        Guid pedidoId)
    {
        await using var db = fixture.CreateDbContext();
        var repo = new VagaOcupadaRepository(db);
        await repo.OcuparAsync(janelaId, data, pedidoId);
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

    private static Pedido CriarPedidoRascunho(Guid empresaId)
    {
        var p = Pedido.Criar(empresaId, origem: "storefront");
        p.Status = StatusPedidoMapper.Rascunho;
        return p;
    }
}
