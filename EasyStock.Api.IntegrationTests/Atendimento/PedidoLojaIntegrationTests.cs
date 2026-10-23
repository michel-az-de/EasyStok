using System.Net;
using System.Net.Http.Json;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    /// <summary>
    /// #1623-b: <c>POST /api/pedidos</c> gravava o <c>LojaId</c> sem validar. Loja inexistente dava 500 pela FK; loja de
    /// outra empresa era gravada e o pedido seguia até Entregue sem baixar estoque.
    /// </summary>
    [SkippableFact]
    public async Task CriarPedido_LojaInexistenteOuDeOutraEmpresaResponde400SemGravar()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        await SemearAsync(factory);
        var outraEmpresa = Guid.NewGuid();
        var lojaAlheia = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(outraEmpresa);
            var agora = DateTime.UtcNow;
            db.Empresas.Add(new Empresa { Id = outraEmpresa, Nome = "Outra empresa", Documento = outraEmpresa.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
            db.Set<Loja>().Add(new Loja { Id = lojaAlheia, EmpresaId = outraEmpresa, Nome = "Loja alheia", Ativa = true, CriadoEm = agora, AlteradoEm = agora });
            await db.SaveChangesAsync();
        }

        foreach (var lojaId in new[] { Guid.NewGuid(), lojaAlheia })
        {
            var resposta = await client.PostAsJsonAsync("/api/pedidos", new
            {
                EmpresaId, LojaId = lojaId, ClienteNomeAdHoc = "Balcão", Origem = "balcao",
                Itens = new[] { new { Nome = "Brigadeiro", Quantidade = 1m, PrecoUnitario = 5m } },
            });
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resposta.Content.ReadAsStringAsync());
        }

        using var leitura = factory.Services.CreateScope();
        var dbLeitura = leitura.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        dbLeitura.SetMobileTenantContext(EmpresaId);
        (await dbLeitura.Pedidos.CountAsync(p => p.EmpresaId == EmpresaId)).Should().Be(0, "nenhum pedido nasce com loja inválida");
    }
}
