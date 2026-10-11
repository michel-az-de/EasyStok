using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class AgendaPedidosQueryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task ListaApenasVagaAtivaDaEmpresaEPeriodoComRlsReal()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var empresa = Empresa.Criar("Agenda casa", null);
        var externa = Empresa.Criar("Agenda externa", null);
        var loja = StorefrontEntity.Criar(empresa.Id, $"agenda-{Guid.NewGuid():N}", "Agenda", 0);
        var outraLoja = StorefrontEntity.Criar(externa.Id, $"agenda-{Guid.NewGuid():N}", "Externa", 0);
        var data = new DateOnly(2026, 10, 20);
        var janela = JanelaEntrega.Criar(loja.Id, (int)data.DayOfWeek, new(9, 0), new(12, 0), 3, "Manhã");
        var janelaExterna = JanelaEntrega.Criar(outraLoja.Id, (int)data.DayOfWeek, new(9, 0), new(12, 0), 3, "Externa");
        var ativo = Pedido(empresa.Id, "Ativa");
        var liberado = Pedido(empresa.Id, "Liberada");
        var outroDia = Pedido(empresa.Id, "Outro dia");
        var deOutraEmpresa = Pedido(externa.Id, "Externa");
        var vagaLiberada = VagaOcupada.Ocupar(janela.Id, data, liberado.Id);
        vagaLiberada.Liberar("cancelado");
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.AddRange(empresa, externa);
            db.Storefronts.AddRange(loja, outraLoja);
            db.JanelasEntrega.AddRange(janela, janelaExterna);
            db.Pedidos.AddRange(ativo, liberado, outroDia, deOutraEmpresa);
            db.VagasOcupadas.AddRange(VagaOcupada.Ocupar(janela.Id, data, ativo.Id), vagaLiberada,
                VagaOcupada.Ocupar(janela.Id, data.AddDays(1), outroDia.Id),
                VagaOcupada.Ocupar(janelaExterna.Id, data, deOutraEmpresa.Id));
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateRlsClientDbContext();
        await leitura.Database.OpenConnectionAsync();
        await leitura.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.Id.ToString()}, false)");
        leitura.SetMobileTenantContext(empresa.Id);
        var resultado = await new AgendaPedidosQuery(leitura).ListarAsync(empresa.Id, loja.Id, data, data);

        resultado.Should().ContainSingle(p => p.PedidoId == ativo.Id && p.ClienteNome == "Ativa");
    }

    private static Pedido Pedido(Guid empresaId, string nome) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, Status = StatusPedidoMapper.Aguardando,
        ClienteNome = nome, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
    };
}
