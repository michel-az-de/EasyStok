using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1774, lado a receber: o recebimento novo entra como inserção, e a conciliação do Pix enxerga a conta com TODAS
/// as parcelas (lendo só a parcela do txid, a conta de três parcelas virava "Paga" com duas pendentes).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ContaReceberRepositoryAgregadoTests(PostgreSqlDatabaseFixture fixture)
{
    private static async Task<(Guid EmpresaId, Guid ContaId, Guid PrimeiraParcelaId)> SemearAsync(PostgreSqlDatabaseFixture fixture)
    {
        await using var db = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Emp", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        var categoria = CategoriaFinanceira.Criar(empresaId, "Rec", TipoCategoriaFinanceira.Receita);
        db.CategoriasFinanceiras.Add(categoria);
        var conta = ContaReceber.Criar(empresaId, null, categoria.Id, "Encomenda fiada", DateTime.UtcNow.Date);
        conta.AdicionarParcela(1, 40m, DateTime.UtcNow.Date.AddDays(5));
        conta.AdicionarParcela(2, 40m, DateTime.UtcNow.Date.AddDays(35));
        conta.Emitir();
        db.ContasReceber.Add(conta);
        await db.SaveChangesAsync();
        return (empresaId, conta.Id, conta.Parcelas.Single(p => p.Numero == 1).Id);
    }

    [SkippableFact]
    public async Task Recebimento_novo_em_parcela_ja_gravada_e_inserido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, _, parcelaId) = await SemearAsync(fixture);

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var repo = new Infra.Postgre.Repositories.ContaReceberRepository(db);
            var parcela = await repo.GetParcelaWithContaAsync(empresaId, parcelaId);
            parcela!.RegistrarPagamento(
                PagamentoParcela.CriarConfirmado(empresaId, TipoLadoFinanceiro.Receber, 15m, "Pix", DateTime.UtcNow), DateTime.UtcNow);
            parcela.ContaReceber!.AtualizarStatusPorParcelas();

            await repo.UpdateAsync(parcela.ContaReceber);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        var gravada = await leitura.ParcelasReceber.Include(p => p.Pagamentos).SingleAsync(p => p.Id == parcelaId);
        gravada.Pagamentos.Should().ContainSingle(p => p.Valor == 15m);
    }

    [SkippableFact]
    public async Task Leitura_pelo_txid_do_Pix_traz_a_conta_com_todas_as_parcelas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, _, parcelaId) = await SemearAsync(fixture);
        var txid = "txid" + Guid.NewGuid().ToString("N")[..22];

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var parcela = await db.ParcelasReceber.SingleAsync(p => p.Id == parcelaId);
            parcela.AssociarPix(txid, "copia-e-cola", "qr", DateTime.UtcNow.AddHours(1));
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        var repoLeitura = new Infra.Postgre.Repositories.ContaReceberRepository(leitura);
        using (leitura.UseRowLevelSecurityBypass())
        {
            var lida = await repoLeitura.GetParcelaByEfiTxidAsync(txid);

            lida.Should().NotBeNull();
            lida!.ContaReceber!.Parcelas.Should().HaveCount(2, "o status da conta é decidido olhando todas as parcelas");
        }
    }
}
