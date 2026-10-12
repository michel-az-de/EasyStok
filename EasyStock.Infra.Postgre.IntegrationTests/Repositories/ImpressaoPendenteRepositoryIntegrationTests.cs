using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Operacao;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Fila de impressão (S20) em Postgres real: polling por empresa na ordem de chegada, isolamento de
/// tenant (filtro + RLS), varredura cross-tenant do alerta e a migration subindo e descendo com a policy.
/// Fixture por classe: a migration é desfeita e refeita no último cenário.
/// </summary>
public class ImpressaoPendenteRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string MigrationAnterior = "20260930132753_AddClienteTagNotaBloqueioPreferencias";
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Falha_no_commit_reverte_documento_e_fila_e_permite_confirmar_em_nova_requisicao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker indisponível");
        var empresa = Guid.NewGuid();
        Guid documentoId, filaId;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa);
            var pedido = await CriarPedidoAsync(seed, empresa);
            var documento = ImpressaoPendente.PrepararDocumento(empresa, null, pedido.Id, Guid.NewGuid(), null,
                "cozinha", "a4", "qr", "HP", "1", "html", "hash", "intencao", Agora);
            var fila = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora);
            seed.ImpressoesPendentes.AddRange(documento, fila);
            await seed.SaveChangesAsync();
            (documentoId, filaId) = (documento.Id, fila.Id);
        }
        var falha = new FalharCommit();
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString, o => o.EnableRetryOnFailure())
            .AddInterceptors(falha).Options;
        var ator = Guid.NewGuid();
        await using (var db = new EasyStockDbContext(options))
        {
            db.SetMobileTenantContext(empresa);
            var caso = new EasyStock.Application.UseCases.Operacao.Impressao.RegistrarRetornoImpressaoUseCase(
                new ImpressaoPendenteRepository(db), db, TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EasyStock.Application.UseCases.Operacao.Impressao.RegistrarRetornoImpressaoUseCase>.Instance);
            Func<Task> confirmar = async () => await caso.ExecuteAsync(new(empresa, documentoId, true,
                UsuarioId: ator, CanhotoId: filaId));
            await confirmar.Should().ThrowAsync<Npgsql.NpgsqlException>().WithMessage("falha simulada no commit");
        }
        falha.Chamadas.Should().Be(1);
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresa);
        var pendentes = await conferir.ImpressoesPendentes.AsNoTracking()
            .Where(i => i.Id == documentoId || i.Id == filaId).ToListAsync();
        pendentes.Single(i => i.Id == documentoId).Status.Should().Be(EasyStock.Domain.Enums.Operacao.StatusImpressao.Preparada);
        pendentes.Single(i => i.Id == filaId).Status.Should().Be(EasyStock.Domain.Enums.Operacao.StatusImpressao.Pendente);
        pendentes.Should().OnlyContain(i => i.ImpressaEm == null && i.ConfirmadaPor == null);
        var retomada = new EasyStock.Application.UseCases.Operacao.Impressao.RegistrarRetornoImpressaoUseCase(
            new ImpressaoPendenteRepository(conferir), conferir, TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EasyStock.Application.UseCases.Operacao.Impressao.RegistrarRetornoImpressaoUseCase>.Instance);
        (await retomada.ExecuteAsync(new(empresa, documentoId, true, UsuarioId: ator, CanhotoId: filaId))).Should().NotBeNull();
        var confirmadas = await conferir.ImpressoesPendentes.AsNoTracking()
            .Where(i => i.Id == documentoId || i.Id == filaId).ToListAsync();
        confirmadas.Should().OnlyContain(i => i.Status == EasyStock.Domain.Enums.Operacao.StatusImpressao.Impressa
            && i.ConfirmadaPor == ator && i.Tentativas == 1);
    }

    private sealed class FalharCommit : DbTransactionInterceptor
    {
        public int Chamadas { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Chamadas++;
            throw new Npgsql.NpgsqlException("falha simulada no commit", new TimeoutException());
        }
    }

    [SkippableFact]
    public async Task Confirmacao_atomica_e_concorrente_preserva_saida_e_ator_da_primeira_confirmacao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker indisponível");
        var empresa = Guid.NewGuid();
        Guid documentoId, filaId, falhaId;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa);
            var pedido = await CriarPedidoAsync(seed, empresa);
            var doc = ImpressaoPendente.PrepararDocumento(empresa, null, pedido.Id, Guid.NewGuid(), null,
                "cozinha", "a4", "qr", "HP", "1", "html", "hash", "intencao", Agora);
            var fila = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora);
            var falha = ImpressaoPendente.PrepararDocumento(empresa, null, pedido.Id, Guid.NewGuid(), null,
                "pedido", "a4", "qr", "HP", "1", "html", "hash", "intencao", Agora);
            seed.ImpressoesPendentes.AddRange(doc, fila, falha);
            await seed.SaveChangesAsync();
            (documentoId, filaId, falhaId) = (doc.Id, fila.Id, falha.Id);
        }
        async Task Responder(Guid id, bool impressa, Guid ator, Guid? fila = null)
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(empresa);
            var caso = new EasyStock.Application.UseCases.Operacao.Impressao.RegistrarRetornoImpressaoUseCase(
                new ImpressaoPendenteRepository(db), db, TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EasyStock.Application.UseCases.Operacao.Impressao.RegistrarRetornoImpressaoUseCase>.Instance);
            (await caso.ExecuteAsync(new(empresa, id, impressa, "erro", ator, fila))).Should().NotBeNull();
        }
        await Task.WhenAll(Responder(documentoId, true, Guid.NewGuid(), filaId), Responder(documentoId, true, Guid.NewGuid(), filaId));
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresa);
        var documento = await conferir.ImpressoesPendentes.AsNoTracking().SingleAsync(i => i.Id == documentoId);
        var filaConfirmada = await conferir.ImpressoesPendentes.AsNoTracking().SingleAsync(i => i.Id == filaId);
        documento.ImpressaEm.Should().Be(filaConfirmada.ImpressaEm);
        documento.ConfirmadaPor.Should().Be(filaConfirmada.ConfirmadaPor);
        documento.Tentativas.Should().Be(1);
        filaConfirmada.Tentativas.Should().Be(1);
        await Responder(documentoId, false, Guid.NewGuid());
        var intacto = await conferir.ImpressoesPendentes.AsNoTracking().SingleAsync(i => i.Id == documentoId);
        intacto.ImpressaEm.Should().Be(documento.ImpressaEm);
        intacto.ConfirmadaPor.Should().Be(documento.ConfirmadaPor);
        var operador = Guid.NewGuid();
        await Task.WhenAll(Responder(falhaId, true, operador), Responder(falhaId, false, Guid.NewGuid()));
        var final = await conferir.ImpressoesPendentes.AsNoTracking().SingleAsync(i => i.Id == falhaId);
        final.Status.Should().Be(EasyStock.Domain.Enums.Operacao.StatusImpressao.Impressa);
        final.ConfirmadaPor.Should().Be(operador);
    }

    [SkippableFact]
    public async Task Snapshot_com_login_sem_bypass_obedece_RLS_mesmo_sem_filtro_global()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker indisponível");
        // O teste de downgrade desta classe recria a tabela e seus grants.
        await fixture.ResetDatabaseAsync();
        var empresa = Guid.NewGuid();
        Guid id;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa);
            var pedido = await CriarPedidoAsync(seed, empresa);
            var i = ImpressaoPendente.PrepararDocumento(empresa, null, pedido.Id, Guid.NewGuid(), null,
                "pedido", "a4", "qr", "HP", "1", "snapshot privado", "hash", "intencao", Agora);
            seed.ImpressoesPendentes.Add(i);
            await seed.SaveChangesAsync();
            id = i.Id;
        }
        await using var db = fixture.CreateRlsClientDbContext();
        await db.Database.OpenConnectionAsync();
        var outra = Guid.NewGuid().ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {outra}, false)");
        (await db.ImpressoesPendentes.IgnoreQueryFilters().CountAsync(i => i.Id == id)).Should().Be(0);
        var correta = empresa.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {correta}, false)");
        (await db.ImpressoesPendentes.IgnoreQueryFilters().SingleAsync(i => i.Id == id)).Html.Should().Be("snapshot privado");
    }

    [SkippableFact]
    public async Task Documento_preparado_preserva_snapshot_sem_bloquear_canhoto_e_historico_e_paginado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker indisponível");
        var empresa = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var pedido = await CriarPedidoAsync(db, empresa);
        var repo = new ImpressaoPendenteRepository(db);
        var primeiro = ImpressaoPendente.PrepararDocumento(empresa, null, pedido.Id, Guid.NewGuid(), null,
            "pedido", "a4", "barras", "HP", "1", "<html>conteúdo original</html>", "hash", "intencao", Agora);
        await repo.AddAsync(primeiro);
        await db.SaveChangesAsync();
        (await repo.ExisteCanhotoAsync(empresa, pedido.Id)).Should().BeFalse();
        (await repo.ListarPendentesAsync(empresa, 20)).Should().BeEmpty();
        for (var n = 1; n < 25; n++) await repo.AddAsync(ImpressaoPendente.PrepararDocumento(empresa, null,
            pedido.Id, Guid.NewGuid(), null, "pedido", "a4", "qr", "HP", "1", "html", "hash", "intencao", Agora.AddMinutes(n)));
        await db.SaveChangesAsync();
        (await repo.HistoricoAsync(empresa, pedido.Id, 1)).Should().HaveCount(20);
        (await repo.HistoricoAsync(empresa, pedido.Id, 2)).Should().HaveCount(5);
        db.ChangeTracker.Clear();
        (await repo.GetByIdAsync(empresa, primeiro.Id))!.Html.Should().Be("<html>conteúdo original</html>");
        await repo.AddAsync(ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora));
        await db.SaveChangesAsync();
        (await repo.ExisteCanhotoAsync(empresa, pedido.Id)).Should().BeTrue();
    }

    [SkippableFact]
    public async Task Solicitacoes_concorrentes_devolvem_um_documento_e_guids_sao_isolados_por_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker indisponível");
        var empresa = Guid.NewGuid();
        Guid pedidoId;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa);
            pedidoId = (await CriarPedidoAsync(seed, empresa)).Id;
        }
        var solicitacao = Guid.NewGuid();
        async Task<Guid> Preparar(Guid emp, Guid pid)
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(emp);
            var pedidos = NSubstitute.Substitute.For<EasyStock.Application.Ports.Output.Persistence.IPedidoRepository>();
            var pedido = await db.Pedidos.SingleAsync(p => p.EmpresaId == emp && p.Id == pid);
            NSubstitute.SubstituteExtensions.Returns(pedidos.GetByIdAsync(emp, pid), pedido);
            var caso = new EasyStock.Application.UseCases.Operacao.Impressao.PrepararDocumentoImpressoUseCase(
                new ImpressaoPendenteRepository(db), pedidos, db, TimeProvider.System);
            return (await caso.ExecuteAsync(new(emp, pid, solicitacao, null, "pedido", "a4", "qr", "HP", false, "html", "1")))!.Id;
        }
        var ids = await Task.WhenAll(Preparar(empresa, pedidoId), Preparar(empresa, pedidoId));
        ids.Distinct().Should().HaveCount(1);
        await using var outro = fixture.CreateDbContext();
        var empresaB = Guid.NewGuid();
        outro.SetMobileTenantContext(empresaB);
        var pedidoB = await CriarPedidoAsync(outro, empresaB);
        (await Preparar(empresaB, pedidoB.Id)).Should().NotBe(ids[0]);
        var repoB = new ImpressaoPendenteRepository(outro);
        (await repoB.GetByIdAsync(empresaB, ids[0])).Should().BeNull();
        (await repoB.HistoricoAsync(empresaB, pedidoId, 1)).Should().BeEmpty();
    }

    private static async Task<Pedido> CriarPedidoAsync(EasyStockDbContext db, Guid empresaId)
    {
        var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
        empresa.Id = empresaId;
        db.Empresas.Add(empresa);
        var pedido = Pedido.Criar(empresaId, origem: "whatsapp");
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    [SkippableFact]
    public async Task PendentesDaEmpresaNaOrdemDeChegadaSemAsImpressas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var pedido = await CriarPedidoAsync(db, empresa);
        var repo = new ImpressaoPendenteRepository(db);
        var segunda = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora.AddMinutes(1));
        var primeira = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora);
        var impressa = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora.AddMinutes(-1));
        impressa.MarcarImpressa(Agora);
        await repo.AddAsync(segunda);
        await repo.AddAsync(primeira);
        await repo.AddAsync(impressa);
        await db.SaveChangesAsync();

        var pendentes = await repo.ListarPendentesAsync(empresa, 10);

        pendentes.Select(p => p.Id).Should().Equal(primeira.Id, segunda.Id);
        (await repo.GetByIdAsync(empresa, impressa.Id))!.ImpressaEm.Should().Be(Agora);
    }

    [SkippableFact]
    public async Task IsolamentoDeTenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        Guid impressaoB;
        await using (var dbB = fixture.CreateDbContext())
        {
            dbB.SetMobileTenantContext(empresaB);
            var pedido = await CriarPedidoAsync(dbB, empresaB);
            var i = ImpressaoPendente.CriarCanhoto(empresaB, null, pedido.Id, Agora);
            dbB.ImpressoesPendentes.Add(i);
            await dbB.SaveChangesAsync();
            impressaoB = i.Id;
        }

        await using var dbA = fixture.CreateDbContext();
        dbA.SetMobileTenantContext(empresaA);
        var repo = new ImpressaoPendenteRepository(dbA);
        (await repo.ListarPendentesAsync(empresaA, 50)).Should().NotContain(p => p.Id == impressaoB);
        (await repo.GetByIdAsync(empresaA, impressaoB)).Should().BeNull("impressão de outra empresa é invisível");
        (await dbA.ImpressoesPendentes.CountAsync(i => i.Id == impressaoB)).Should().Be(0, "RLS esconde a linha");
    }

    [SkippableFact]
    public async Task AtrasadasCrossTenantDentroDaJanela()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        Guid atrasada, recente, antiga;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var pedido = await CriarPedidoAsync(db, empresa);
            var a = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora.AddMinutes(-5));
            var r = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora.AddMinutes(-1));
            var v = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, Agora.AddHours(-13));
            db.ImpressoesPendentes.AddRange(a, r, v);
            await db.SaveChangesAsync();
            (atrasada, recente, antiga) = (a.Id, r.Id, v.Id);
        }

        await using var semTenant = fixture.CreateDbContext();
        var lista = await new ImpressaoPendenteRepository(semTenant)
            .ListarAtrasadasAsync(Agora.AddHours(-12), Agora.AddMinutes(-3), 50);

        lista.Should().Contain(x => x.ImpressaoId == atrasada && x.EmpresaId == empresa, "o job roda sem tenant");
        lista.Select(x => x.ImpressaoId).Should().NotContain(recente).And.NotContain(antiga);
    }

    [SkippableFact]
    public async Task Migration_SobeEDesceLimpaComRls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        (await ContarTabelaAsync(db)).Should().Be(1);
        (await ContarPolicyAsync(db)).Should().Be(1, "a tabela nova precisa da policy tenant_isolation (ADR-0010)");

        await migrator.MigrateAsync(MigrationAnterior);
        (await ContarTabelaAsync(db)).Should().Be(0, "Down remove a tabela");

        await migrator.MigrateAsync();
        (await ContarTabelaAsync(db)).Should().Be(1);
        (await ContarPolicyAsync(db)).Should().Be(1);
    }

    private static Task<int> ContarTabelaAsync(DbContext db) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'impressoes_pendentes'")
            .SingleAsync();

    private static Task<int> ContarPolicyAsync(DbContext db) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'impressoes_pendentes'")
            .SingleAsync();
}
