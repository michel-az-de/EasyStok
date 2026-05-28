using EasyStock.Domain.Entities;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories.Fiscal;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Defesa em DB contra duplicacao NFC-e por race em retry idempotente (issue #290).
/// Cobre o partial unique index <c>ux_nfe_documentos_empresa_idempotency</c> sob
/// Postgres real — o cenario que nenhum unit test com NSubstitute consegue verificar.
/// </summary>
public class NfeRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task FindByIdempotencyKey_NaoEncontrado_RetornaNull()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var repo = new NfeRepository(context);

        var nada = await repo.FindByIdempotencyKeyAsync(empresaId, "chave-inexistente");

        nada.Should().BeNull();
    }

    [SkippableFact]
    public async Task FindByIdempotencyKey_ComMatch_RetornaSomenteDaMesmaEmpresa()
    {
        // Garante que o lookup respeita Global Query Filter — outra empresa
        // com mesma IdempotencyKey nao vaza para o caller.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaA = await SeedEmpresaEPedidoAsync("Empresa A");
        var empresaB = await SeedEmpresaEPedidoAsync("Empresa B");
        const string chaveCompartilhada = "abc-123-shared-key";

        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.NfeDocumentos.Add(CriarNfe(empresaA.empresaId, empresaA.pedidoId, serie: 1, numero: 10, key: chaveCompartilhada));
            ctx.NfeDocumentos.Add(CriarNfe(empresaB.empresaId, empresaB.pedidoId, serie: 1, numero: 99, key: chaveCompartilhada));
            await ctx.SaveChangesAsync();
        }

        await using var queryCtx = fixture.CreateDbContext();
        var repo = new NfeRepository(queryCtx);

        var doA = await repo.FindByIdempotencyKeyAsync(empresaA.empresaId, chaveCompartilhada);

        doA.Should().NotBeNull();
        doA!.EmpresaId.Should().Be(empresaA.empresaId);
        doA.Numero.Should().Be(10);
    }

    [SkippableFact]
    public async Task UniqueConstraint_RaceComMesmaIdempotencyKey_SegundoInsertLancaDbUpdateException()
    {
        // Cenario chave da issue #290: 2 calls concorrentes com mesma K1 chegam
        // ao DB; o segundo insert precisa ser BARRADO pelo partial unique index
        // ux_nfe_documentos_empresa_idempotency. Sem isso, NFC-e duplicada no SEFAZ.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var seed = await SeedEmpresaEPedidoAsync("Empresa Race");
        const string chaveDuplicada = "duplicada-race-key";

        await using (var primeiroCtx = fixture.CreateDbContext())
        {
            primeiroCtx.NfeDocumentos.Add(CriarNfe(seed.empresaId, seed.pedidoId, serie: 1, numero: 1, key: chaveDuplicada));
            await primeiroCtx.SaveChangesAsync();
        }

        await using var segundoCtx = fixture.CreateDbContext();
        segundoCtx.NfeDocumentos.Add(CriarNfe(seed.empresaId, seed.pedidoId, serie: 1, numero: 2, key: chaveDuplicada));

        var act = async () => await segundoCtx.SaveChangesAsync();

        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        ex.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ux_nfe_documentos_empresa_idempotency");
    }

    [SkippableFact]
    public async Task UniqueConstraint_IdempotencyKeyNull_PermiteMultiplosDocumentosLegados()
    {
        // Partial unique index (WHERE IdempotencyKey IS NOT NULL) precisa permitir
        // backfill: documentos pre-AddNfeF1RepoIndexes (com IdempotencyKey NULL)
        // nao sao indexados, portanto nao colidem entre si.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var seed = await SeedEmpresaEPedidoAsync("Empresa Backfill");

        await using var ctx = fixture.CreateDbContext();
        ctx.NfeDocumentos.Add(CriarNfe(seed.empresaId, seed.pedidoId, serie: 1, numero: 1, key: null));
        ctx.NfeDocumentos.Add(CriarNfe(seed.empresaId, seed.pedidoId, serie: 1, numero: 2, key: null));

        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    private async Task<(Guid empresaId, Guid pedidoId)> SeedEmpresaEPedidoAsync(string nome)
    {
        await using var ctx = fixture.CreateDbContext();
        var empresa = new Empresa
        {
            Id = Guid.NewGuid(),
            Nome = nome,
            Documento = Guid.NewGuid().ToString("N")[..11],
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        var pedido = Pedido.Criar(empresa.Id, cliente: null, lojaId: null, origem: "test");

        ctx.Empresas.Add(empresa);
        ctx.Pedidos.Add(pedido);
        await ctx.SaveChangesAsync();
        return (empresa.Id, pedido.Id);
    }

    private static NfeDocumento CriarNfe(Guid empresaId, Guid pedidoId, short serie, long numero, string? key)
    {
        var nfe = NfeDocumento.Criar(
            empresaId: empresaId,
            pedidoId: pedidoId,
            serie: serie,
            numero: numero,
            dadosEmitente: new DadosEmissor("Emp Teste", "11444777000161"),
            dadosDestinatario: null,
            totalNota: Dinheiro.FromDecimal(100m),
            idempotencyKey: key);
        nfe.AdicionarItem("Produto", 1m, Dinheiro.FromDecimal(100m), "UN");
        return nfe;
    }
}
