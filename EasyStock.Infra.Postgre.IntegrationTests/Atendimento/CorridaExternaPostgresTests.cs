using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

/// <summary>
/// I9.1 (#1669): corrida externa no Postgres real. Registrar e corrigir gravam o custo; o relatório soma o
/// custo das corridas da viagem contra a linha de frete do pedido, sem viagem desfeita e sem outra loja.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CorridaExternaPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Operadora = Guid.NewGuid();

    [SkippableFact]
    public async Task Registrar_corrigir_e_relatorio_de_custo_contra_frete()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = Guid.NewGuid();
        var outraEmpresaId = Guid.NewGuid();
        var viagem = await SemearViagemComFreteAsync(empresaId, freteReais: 12m);
        var desfeita = await SemearViagemComFreteAsync(empresaId, freteReais: 9m, desfazer: true, criarEmpresa: false);
        var alheia = await SemearViagemComFreteAsync(outraEmpresaId, freteReais: 30m);

        Guid corridaId;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var registrar = new RegistrarCorridaExternaUseCase(new ViagemRepository(db), new CorridaExternaRepository(db), db, TimeProvider.System);
            corridaId = (await registrar.ExecuteAsync(new RegistrarCorridaExternaInput(empresaId, Operadora, viagem.Id, EmpresaEntregador.NoveNove, 1890, "João"))).Id;
            await registrar.ExecuteAsync(new RegistrarCorridaExternaInput(empresaId, Operadora, viagem.Id, EmpresaEntregador.Uber, 300, null));

            var deOutraLoja = () => registrar.ExecuteAsync(new RegistrarCorridaExternaInput(empresaId, Operadora, alheia.Id, EmpresaEntregador.NoveNove, 100, null));
            await deOutraLoja.Should().ThrowAsync<ViagemNaoEncontradaException>();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var corrigir = new CorrigirCorridaExternaUseCase(new ViagemRepository(db), new CorridaExternaRepository(db), db, TimeProvider.System);
            var corrigida = await corrigir.ExecuteAsync(new CorrigirCorridaExternaInput(empresaId, Operadora, viagem.Id, corridaId, 2150, "João"));
            corrigida.CorrigidoPorUsuarioId.Should().Be(Operadora);
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        (await new CorridaExternaRepository(leitura).ListarDaViagemAsync(empresaId, viagem.Id))
            .Select(c => (c.Provedor, c.CustoCentavos)).Should().Equal((EmpresaEntregador.NoveNove, 2150), (EmpresaEntregador.Uber, 300));

        var relatorio = await new CustoDeEntregaUseCase(new CustoDeEntregaQuery(leitura))
            .ExecuteAsync(empresaId, Agora.AddDays(-1), Agora.AddDays(1));

        var linha = relatorio.Viagens.Should().ContainSingle("viagem desfeita e viagem de outra loja ficam fora").Subject;
        linha.ViagemId.Should().Be(viagem.Id);
        linha.Paradas.Should().Be(1);
        linha.FreteCobradoCentavos.Should().Be(1200);
        linha.CustoCentavos.Should().Be(2450);
        linha.SaldoCentavos.Should().Be(-1250);
        desfeita.Situacao.Should().Be(SituacaoViagem.Desfeita);
    }

    [SkippableFact]
    public async Task Custo_negativo_e_barrado_tambem_no_banco()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = Guid.NewGuid();
        var viagem = await SemearViagemComFreteAsync(empresaId, freteReais: 12m);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.CorridasExternas.Add(CorridaExterna.RegistrarManual(viagem, EmpresaEntregador.NoveNove, 100, null, Operadora, Agora));
        await db.SaveChangesAsync();

        var act = () => db.Database.ExecuteSqlRawAsync("UPDATE corridas_externas SET \"CustoCentavos\" = -1 WHERE \"ViagemId\" = {0}", viagem.Id);

        (await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("ck_corridas_externas_custo");
    }

    private async Task<Viagem> SemearViagemComFreteAsync(Guid empresaId, decimal freteReais, bool desfazer = false, bool criarEmpresa = true)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        if (criarEmpresa)
            db.Empresas.Add(new Empresa
            {
                Id = empresaId,
                Nome = "Empresa Corrida",
                Documento = empresaId.ToString("N")[..14],
                CriadoEm = Agora,
                AlteradoEm = Agora,
            });
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = StatusPedidoMapper.Pronto;
        pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Marmita", LinhaSnapshot = "{}", Quantidade = 1, PrecoUnitario = 40m, Subtotal = 40m });
        pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Entrega — Centro", Quantidade = 1, PrecoUnitario = freteReais, Subtotal = freteReais });
        // Linha solta sem produto que não é frete: não entra na soma.
        pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Taxa de embalagem", Quantidade = 1, PrecoUnitario = 2m, Subtotal = 2m });
        db.Pedidos.Add(pedido);

        var viagem = Viagem.Criar(empresaId, Agora);
        viagem.IncluirParada(pedido.Id, clienteBloqueado: false);
        if (desfazer) viagem.Desfazer();
        db.Viagens.Add(viagem);
        await db.SaveChangesAsync();
        return viagem;
    }
}