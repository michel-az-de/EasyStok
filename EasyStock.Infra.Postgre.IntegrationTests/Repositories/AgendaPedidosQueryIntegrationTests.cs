using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
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
        var semConversa = Pedido(empresa.Id, "Sem conversa");
        var liberado = Pedido(empresa.Id, "Liberada");
        var outroDia = Pedido(empresa.Id, "Outro dia");
        var deOutraEmpresa = Pedido(externa.Id, "Externa");
        var vagaLiberada = VagaOcupada.Ocupar(janela.Id, data, liberado.Id);
        vagaLiberada.Liberar("cancelado");
        // #1883 (#1902): o número da comanda é o da conversa dona do pedido (identity do Postgres), e a
        // conversa de outra empresa apontando o mesmo pedido não pode emprestar o número dela.
        var conversa = Conversa.Abrir(empresa.Id, "5511999998888", DateTime.UtcNow, "Ativa");
        conversa.DefinirPedidoEmAndamento(ativo.Id);
        var conversaExterna = Conversa.Abrir(externa.Id, "5511999997777", DateTime.UtcNow, "Externa");
        conversaExterna.DefinirPedidoEmAndamento(ativo.Id);
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.AddRange(empresa, externa);
            db.Storefronts.AddRange(loja, outraLoja);
            db.JanelasEntrega.AddRange(janela, janelaExterna);
            db.Pedidos.AddRange(ativo, semConversa, liberado, outroDia, deOutraEmpresa);
            db.VagasOcupadas.AddRange(VagaOcupada.Ocupar(janela.Id, data, ativo.Id), vagaLiberada,
                VagaOcupada.Ocupar(janela.Id, data, semConversa.Id),
                VagaOcupada.Ocupar(janela.Id, data.AddDays(1), outroDia.Id),
                VagaOcupada.Ocupar(janelaExterna.Id, data, deOutraEmpresa.Id));
            db.AtendimentoConversas.AddRange(conversaExterna, conversa);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateRlsClientDbContext();
        await leitura.Database.OpenConnectionAsync();
        await leitura.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.Id.ToString()}, false)");
        leitura.SetMobileTenantContext(empresa.Id);
        var resultado = await new AgendaPedidosQuery(leitura).ListarAsync(empresa.Id, loja.Id, data, data);

        resultado.Should().HaveCount(2);
        conversa.NumeroComanda.Should().BePositive();
        resultado.Should().ContainSingle(p => p.PedidoId == ativo.Id && p.ClienteNome == "Ativa")
            .Which.NumeroComanda.Should().Be(conversa.NumeroComanda);
        resultado.Should().ContainSingle(p => p.PedidoId == semConversa.Id)
            .Which.NumeroComanda.Should().BeNull();
    }

    private static Pedido Pedido(Guid empresaId, string nome) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, Status = StatusPedidoMapper.Aguardando,
        ClienteNome = nome, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
    };
}
