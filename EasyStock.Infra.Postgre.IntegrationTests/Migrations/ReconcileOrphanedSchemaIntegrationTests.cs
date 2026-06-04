using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Migrations;

/// <summary>
/// Verificacao FROM-ZERO do incidente #465. Sobe um PostgreSQL LIMPO
/// (Testcontainers) e roda TODAS as migrations reconhecidas pelo EF
/// (<see cref="PostgreSqlDatabaseFixture.ResetDatabaseAsync"/> faz
/// EnsureDeleted + MigrateAsync). Antes do #465, as 3 migrations sem
/// <c>.Designer.cs</c> eram invisiveis ao EF, entao um banco novo
/// (CI/Testcontainers, deploy novo, DR) subia SEM estas colunas/indices,
/// causando <c>42703 column does not exist</c> silencioso em runtime.
///
/// Este teste reproduz esse caminho e falha se a regressao voltar. Roda como
/// <see cref="SkippableFactAttribute"/>: pulado (nao falhado) onde o Docker nao
/// esta disponivel; verde no CI/maquina com Docker.
/// </summary>
public class ReconcileOrphanedSchemaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task BancoNovo_AplicaTudo_SemPendentes_ComColunasEIndicesDoIssue465()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync(); // EnsureDeleted + MigrateAsync = banco do zero

        await using var context = fixture.CreateDbContext();

        // (1) Drift guard: aplicou TODAS as migrations, nada pode ficar pendente.
        var pendentes = await context.Database.GetPendingMigrationsAsync();
        pendentes.Should().BeEmpty(because: "MigrateAsync aplicou todas as migrations reconhecidas pelo EF");

        // (2) Colunas que SO existiam nas 3 migrations orfas agora sao criadas pela
        //     migration de reconciliacao (#465). Antes do fix, faltavam em banco novo.
        (await ColunaExiste(context, "pedidos", "aprovado_em")).Should().BeTrue();
        (await ColunaExiste(context, "pedidos", "recusado_em")).Should().BeTrue();
        (await ColunaExiste(context, "pedidos", "mensagem_recusa_cliente")).Should().BeTrue();
        (await ColunaExiste(context, "nfe_documentos", "IdempotencyKey")).Should().BeTrue();

        // pedidos.Status alargado de varchar(20) -> varchar(32) (cabe "aguardando_aprovacao_baba", 25 chars).
        (await TamanhoColuna(context, "pedidos", "Status")).Should().BeGreaterThanOrEqualTo(32);

        // (3) Indices de performance reconciliados existem.
        (await IndiceExiste(context, "ix_itens_estoque_empresa_loja")).Should().BeTrue();
        (await IndiceExiste(context, "ix_audit_logs_usuario_data")).Should().BeTrue();

        // (4) #290: indice unico PARCIAL de idempotencia NFC-e. A existencia de um indice
        //     UNIQUE no Postgres E a garantia de dedup (um INSERT duplicado de
        //     (EmpresaId, IdempotencyKey) viola 23505). Asserir a definicao prova o
        //     mecanismo sem exigir construir uma NfeDocumento valida inteira (muitos
        //     campos NOT NULL + FKs) so para forcar a colisao.
        var defNfe = await DefinicaoIndice(context, "ux_nfe_documentos_empresa_idempotency");
        defNfe.Should().NotBeNull(because: "o indice unico de idempotencia (#290) deve existir em banco novo");
        defNfe!.Should().Contain("UNIQUE");
        defNfe.Should().Contain("IdempotencyKey");
        defNfe.Should().Contain("IS NOT NULL", because: "e parcial: so dedupa quando IdempotencyKey esta preenchido");
    }

    private static async Task<bool> ColunaExiste(DbContext ctx, string tabela, string coluna) =>
        await ctx.Database.SqlQuery<bool>(
            $@"SELECT EXISTS(SELECT 1 FROM information_schema.columns
                WHERE table_name = {tabela} AND column_name = {coluna}) AS ""Value""").SingleAsync();

    private static async Task<int> TamanhoColuna(DbContext ctx, string tabela, string coluna) =>
        await ctx.Database.SqlQuery<int>(
            $@"SELECT COALESCE(character_maximum_length, 0) AS ""Value"" FROM information_schema.columns
                WHERE table_name = {tabela} AND column_name = {coluna}").SingleAsync();

    private static async Task<bool> IndiceExiste(DbContext ctx, string indice) =>
        await ctx.Database.SqlQuery<bool>(
            $@"SELECT EXISTS(SELECT 1 FROM pg_indexes WHERE indexname = {indice}) AS ""Value""").SingleAsync();

    private static async Task<string?> DefinicaoIndice(DbContext ctx, string indice) =>
        (await ctx.Database.SqlQuery<string>(
            $@"SELECT indexdef AS ""Value"" FROM pg_indexes WHERE indexname = {indice}").ToListAsync())
        .SingleOrDefault();
}
