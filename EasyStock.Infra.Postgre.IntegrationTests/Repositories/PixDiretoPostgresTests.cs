using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.ValueObjects.Pix;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Pagamentos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2233 em Postgres real: a migração aditiva grava a chave Pix da loja e o copia e cola da cobrança, a loja de outro
/// tenant não aparece, e o job de vencimento nunca pega o Pix direto (nem dias depois). Só chaves fictícias.
/// </summary>
public class PixDiretoPostgresTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Agora = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly RecebedorPix Recebedor =
        RecebedorPix.Criar(ChavePix.Criar(TipoChavePix.Email, "loja@exemplo.test"), "Casa de Doces", "Sao Paulo");

    private static async Task<(Loja Loja, Pedido Pedido)> CriarAsync(EasyStockDbContext db, Guid empresaId)
    {
        var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
        empresa.Id = empresaId;
        db.Empresas.Add(empresa);
        var loja = Loja.Criar(empresaId, "Loja Centro");
        db.Lojas.Add(loja);
        var pedido = Pedido.Criar(empresaId, origem: "whatsapp");
        pedido.LojaId = loja.Id;
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return (loja, pedido);
    }

    [SkippableFact]
    public async Task ChaveDaLoja_ECopiaECola_IdaEVolta()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        Guid lojaId, cobrancaId;
        string copiaCola;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var (loja, pedido) = await CriarAsync(db, empresa);
            loja.DefinirRecebedorPix(Recebedor);
            var cobranca = CobrancaPedido.CriarPixDireto(empresa, pedido.Id, 57.9m, Recebedor, Agora);
            db.CobrancasPedido.Add(cobranca);
            await db.SaveChangesAsync();
            (lojaId, cobrancaId, copiaCola) = (loja.Id, cobranca.Id, cobranca.PixCopiaCola!);
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresa);
        var lida = await new LojaRepository(leitura).GetByIdAsync(empresa, lojaId);
        lida!.ObterRecebedorPix().Should().Be(Recebedor);
        lida.PixTipo.Should().Be(TipoChavePix.Email);
        var cobrancaLida = await leitura.CobrancasPedido.AsNoTracking().SingleAsync(c => c.Id == cobrancaId);
        cobrancaLida.Provedor.Should().Be(CobrancaPedido.ProvedorPixDireto);
        cobrancaLida.PixCopiaCola.Should().Be(copiaCola);
        cobrancaLida.ExpiraEm.Should().BeNull();
    }

    [SkippableFact]
    public async Task LojaDeOutroTenant_NaoAparece()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        Guid lojaId;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var (loja, _) = await CriarAsync(db, empresa);
            loja.DefinirRecebedorPix(Recebedor);
            await db.SaveChangesAsync();
            lojaId = loja.Id;
        }

        var outra = Guid.NewGuid();
        await using var alheio = fixture.CreateDbContext();
        alheio.SetMobileTenantContext(outra);
        (await new LojaRepository(alheio).GetByIdAsync(outra, lojaId)).Should().BeNull();
        (await new LojaRepository(alheio).GetByIdAsync(empresa, lojaId)).Should().BeNull("o filtro de tenant esconde a loja alheia");
    }

    [SkippableFact]
    public async Task JobDeVencimento_NuncaPegaPixDireto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        Guid pixId;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var (_, pedido) = await CriarAsync(db, empresa);
            var pix = CobrancaPedido.CriarPixDireto(empresa, pedido.Id, 25m, Recebedor, Agora, Guid.NewGuid());
            db.CobrancasPedido.Add(pix);
            await db.SaveChangesAsync();
            pixId = pix.Id;
        }

        await using var semTenant = fixture.CreateDbContext();
        var repo = new CobrancaPedidoRepository(semTenant);
        (await repo.ListarPendentesVencidasAsync(Agora.AddDays(30), 500)).Should().NotContain(v => v.CobrancaId == pixId);
        (await repo.ListarPendentesVencendoAsync(Agora, TimeSpan.FromDays(30), 500)).Should().NotContain(v => v.CobrancaId == pixId);
    }
}
