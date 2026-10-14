using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1769: registrar pagamento e adicionar parcela numa conta já gravada respondiam "dados alterados por outro
/// processo" em toda tentativa (homologação de 13/10: 3 de 3 e 7 de 7). O filho novo, com a chave gerada no app, era
/// tratado como linha existente. Aqui a conta é lida num contexto novo, como na requisição real.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ContaPagarRepositoryAgregadoTests(PostgreSqlDatabaseFixture fixture)
{
    private static async Task<(Guid EmpresaId, Guid ContaId, Guid ParcelaId)> SemearAsync(PostgreSqlDatabaseFixture fixture, bool emitir = true)
    {
        await using var db = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Emp", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        var categoria = CategoriaFinanceira.Criar(empresaId, "Desp", TipoCategoriaFinanceira.Despesa);
        db.CategoriasFinanceiras.Add(categoria);
        var conta = ContaPagar.Criar(empresaId, null, categoria.Id, "Fornecedor de queijo", DateTime.UtcNow.Date);
        conta.AdicionarParcela(1, 100m, DateTime.UtcNow.Date.AddDays(10));
        // Parcela só entra em conta em rascunho; pagamento só em conta emitida.
        if (emitir) conta.Emitir();
        db.ContasPagar.Add(conta);
        await db.SaveChangesAsync();
        return (empresaId, conta.Id, conta.Parcelas.Single().Id);
    }

    [SkippableFact]
    public async Task Pagamento_novo_em_parcela_ja_gravada_e_inserido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, _, parcelaId) = await SemearAsync(fixture);

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var repo = new Infra.Postgre.Repositories.ContaPagarRepository(db);
            var parcela = await repo.GetParcelaWithContaAsync(empresaId, parcelaId);
            var pagamento = PagamentoParcela.CriarConfirmado(
                empresaId, TipoLadoFinanceiro.Pagar, 40m, "Pix", DateTime.UtcNow, "Manual", null, null, null, null);
            parcela!.RegistrarPagamento(pagamento, DateTime.UtcNow);
            parcela.ContaPagar!.AtualizarStatusPorParcelas();

            await repo.UpdateAsync(parcela.ContaPagar);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        var gravada = await leitura.ParcelasPagar.Include(p => p.Pagamentos).SingleAsync(p => p.Id == parcelaId);
        gravada.Pagamentos.Should().ContainSingle(p => p.Valor == 40m);
        gravada.ValorPago.Should().Be(40m);
    }

    [SkippableFact]
    public async Task Parcela_nova_em_conta_ja_gravada_e_inserida()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, contaId, _) = await SemearAsync(fixture, emitir: false);

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var repo = new Infra.Postgre.Repositories.ContaPagarRepository(db);
            var conta = await repo.GetByIdAsync(empresaId, contaId);
            conta!.AdicionarParcela(2, 50m, DateTime.UtcNow.Date.AddDays(40));

            await repo.UpdateAsync(conta);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        (await leitura.ParcelasPagar.CountAsync(p => p.ContaPagarId == contaId)).Should().Be(2);
    }
}
