using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.Data.DemoStore;

// Dataset opt-in da sprint #1693. Nenhum contato real ou envio externo é criado.
public sealed class LogisticaDemoSeeder(EasyStockDbContext db, IConfiguration config, TimeProvider clock, IUnitOfWork unitOfWork)
{
    public Task<object> CarregarAsync(Guid empresaId, DateOnly dia, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"logistica-demo:" + empresaId}, 0))", token);
            return await CarregarCoreAsync(empresaId, dia, token);
        }, ct);

    private async Task<object> CarregarCoreAsync(Guid empresaId, DateOnly dia, CancellationToken ct)
    {
        if (!Guid.TryParse(config["Logistica:EmpresaDemonstracaoId"], out var permitida)
            || permitida == Guid.Empty || empresaId != permitida)
            throw new UseCaseValidationException("O dia de teste só pode ser criado na empresa de demonstração configurada.");
        var empresa = await db.Empresas.SingleAsync(e => e.Id == empresaId, ct);
        if (empresa.Nome != "Demonstração de entregas")
            throw new UseCaseValidationException("A empresa configurada não está identificada como Demonstração de entregas.");
        var origem = $"demo1693-{dia:yyyyMMdd}";
        var anteriores = await db.Pedidos.Where(p => p.EmpresaId == empresaId && p.Origem == origem)
            .Select(p => p.Id).ToListAsync(ct);
        if (anteriores.Count > 0) return new { criados = 0, pedidoIds = anteriores };

        var agora = clock.GetUtcNow().UtcDateTime;
        if (!await db.MovimentosCaixa.AnyAsync(m => m.EmpresaId == empresaId, ct))
        {
            var abertura = MovimentoCaixa.Criar(empresaId, "abertura", 500m, agora);
            abertura.Descricao = "Saldo fictício para testar pagamentos de frete. Demonstração isolada.";
            abertura.Origem = "api";
            db.MovimentosCaixa.Add(abertura);
        }
        var loja = await db.Lojas.FirstOrDefaultAsync(l => l.EmpresaId == empresaId, ct);
        if (loja is null)
        {
            loja = Loja.Criar(empresaId, "Cozinha de demonstração");
            loja.Endereco = "Origem sintética de teste, São Paulo";
            db.Lojas.Add(loja);
        }
        var vitrine = await db.Storefronts.SingleOrDefaultAsync(s => s.EmpresaId == empresaId, ct);
        if (vitrine is null)
        {
            vitrine = StorefrontEntity.Criar(empresaId, $"demo-{empresaId:N}", "Demonstração de entregas", 0);
            vitrine.ConfigurarFreteRaio(-23.55, -46.63, null, null, null, null);
            db.Storefronts.Add(vitrine);
        }
        (int Hora, int Minuto, int FimHora, int FimMinuto)[] horarios =
            [(10, 0, 11, 0), (11, 30, 12, 30), (14, 0, 15, 0), (16, 0, 17, 0), (17, 0, 18, 0)];
        var janelas = new List<JanelaEntrega>();
        foreach (var h in horarios)
        {
            var janela = JanelaEntrega.Criar(vitrine.Id, (int)dia.DayOfWeek,
                new TimeOnly(h.Hora, h.Minuto), new TimeOnly(h.FimHora, h.FimMinuto), 10, "Janela de teste");
            db.JanelasEntrega.Add(janela);
            janelas.Add(janela);
        }
        string[] nomes = ["Ana", "Bruno", "Carla", "Diego", "Elisa", "Fábio", "Gabi", "Hugo", "Iris", "João"];
        string[] bairros = ["Centro A", "Centro B", "Centro C", "Centro D", "Norte A", "Norte B", "Sul A", "Sul B", "Leste A", "Oeste A"];
        int[] janelaIndices = [0, 0, 0, 0, 1, 1, 2, 2, 3, 4];
        var ids = new List<Guid>();
        for (var i = 0; i < nomes.Length; i++)
        {
            var pedido = Pedido.Criar(empresaId, lojaId: loja.Id, origem: origem);
            pedido.ClienteNome = $"Teste {i + 1:00} · {nomes[i]}";
            // Número reservado ao cenário, nunca associado a conversa ou canal real.
            pedido.ClienteTelefone = "+5500000000000";
            pedido.EnderecoEntrega = i == 4 ? null : $"Destino sintético {i + 1:00}, {bairros[i]}, São Paulo";
            pedido.BairroEntrega = bairros[i];
            pedido.EntregaLat = i == 4 ? null : -23.55 + (i - 4) * .003;
            pedido.EntregaLng = i == 4 ? null : -46.63 + (i % 3 - 1) * .004;
            pedido.Status = i == 5 ? "preparando" : "pronto";
            pedido.Observacoes = "SIMULAÇÃO: endereços, distâncias, custos e contatos fictícios. Não despachar para serviços reais.";
            pedido.CriadoEm = agora;
            var item = new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Encomenda de teste", Quantidade = 1, PrecoUnitario = 40 + i * 5, CriadoEm = agora };
            item.RecalcularSubtotal();
            pedido.Itens.Add(item);
            var frete = new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Entrega — Teste", Quantidade = 1, PrecoUnitario = 8, CriadoEm = agora };
            frete.RecalcularSubtotal();
            pedido.Itens.Add(frete);
            pedido.RecalcularTotal();
            db.Pedidos.Add(pedido);
            db.VagasOcupadas.Add(VagaOcupada.Ocupar(janelas[janelaIndices[i]].Id, dia, pedido.Id));
            ids.Add(pedido.Id);
        }
        await db.SaveChangesAsync(ct);
        return new { criados = ids.Count, pedidoIds = ids };
    }
}
