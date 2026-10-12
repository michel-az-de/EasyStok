using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using EasyStock.Application.UseCases.Atendimento.Programadas;
using EasyStock.Application.UseCases.Common;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Mensagem programada (S39) em Postgres real: a reserva com <c>FOR UPDATE SKIP LOCKED</c> não deixa
/// dois disparadores pegarem a mesma mensagem, e a tabela nova tem RLS.
/// </summary>
public class MensagemProgramadaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task CancelamentoPreservaTenantETiraMensagemDaFila()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Programadas canceladas", null);
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Teste" };
        var mensagem = MensagemProgramada.Agendar(empresa.Id, cliente.Id, null, CanalConversa.Email,
            FinalidadeContato.Transacional, "Aviso cancelado", null, agora.AddSeconds(1), Guid.NewGuid(), agora);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        using var bypass = db.UseRowLevelSecurityBypass();
        db.Empresas.Add(empresa);
        db.Clientes.Add(cliente);
        db.MensagensProgramadas.Add(mensagem);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repo = new MensagemProgramadaRepository(db);
        var cancelar = new CancelarMensagemProgramadaUseCase(repo, db, TimeProvider.System);
        var alheio = () => cancelar.ExecuteAsync(Guid.NewGuid(), mensagem.Id);
        await alheio.Should().ThrowAsync<MensagemProgramadaNaoEncontradaException>();
        (await cancelar.ExecuteAsync(empresa.Id, mensagem.Id)).Situacao.Should().Be(SituacaoMensagemProgramada.Cancelada);
        await using var tx = await db.Database.BeginTransactionAsync();
        (await repo.ListarVencidasComLockAsync(agora.AddSeconds(2), 100)).Should().NotContain(m => m.Id == mensagem.Id);
    }

    [SkippableFact]
    public async Task CancelamentoConcorrenteNaoSobrescreveReservaDoDisparador()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Programadas concorrentes", null);
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Teste" };
        var mensagem = MensagemProgramada.Agendar(empresa.Id, cliente.Id, null, CanalConversa.Email,
            FinalidadeContato.Transacional, "Aviso", null, agora.AddSeconds(1), Guid.NewGuid(), agora);
        await using var reservaDb = fixture.CreateDbContext();
        reservaDb.SetMobileTenantContext(empresa.Id);
        using var bypass = reservaDb.UseRowLevelSecurityBypass();
        reservaDb.Empresas.Add(empresa);
        reservaDb.Clientes.Add(cliente);
        reservaDb.MensagensProgramadas.Add(mensagem);
        await reservaDb.SaveChangesAsync();

        await using var tx = await reservaDb.Database.BeginTransactionAsync();
        mensagem.Reservar(agora.AddSeconds(2));
        await reservaDb.SaveChangesAsync();

        await using var cancelarDb = fixture.CreateDbContext();
        cancelarDb.SetMobileTenantContext(empresa.Id);
        using var bypassCancelar = cancelarDb.UseRowLevelSecurityBypass();
        await cancelarDb.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)cancelarDb.Database.GetDbConnection()).ProcessID;
        var cancelar = new CancelarMensagemProgramadaUseCase(new MensagemProgramadaRepository(cancelarDb), cancelarDb, TimeProvider.System);
        var tentativa = cancelar.ExecuteAsync(empresa.Id, mensagem.Id);
        var esperando = false;
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (!esperando && !tentativa.IsCompleted && DateTime.UtcNow < limite)
        {
            esperando = await reservaDb.Database.SqlQuery<bool>($"SELECT cardinality(pg_blocking_pids({pid})) > 0 AS \"Value\"").SingleAsync();
            if (!esperando) await Task.Delay(20);
        }
        esperando.Should().BeTrue("o cancelamento deve concorrer com a reserva ainda sem commit");
        await tx.CommitAsync();

        Func<Task> finalizar = async () => await tentativa.WaitAsync(TimeSpan.FromSeconds(10));
        await finalizar.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Enviando*");
        await reservaDb.Entry(mensagem).ReloadAsync();
        mensagem.Situacao.Should().Be(SituacaoMensagemProgramada.Enviando);
    }

    [SkippableFact]
    public async Task DoisDisparadoresConcorrentesNaoPegamAMesmaMensagem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Casa da Baba Programada", "11111111000191");
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Fulana", Email = "f@x.com" };
        var ids = new List<Guid>();

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.Add(empresa);
            db.Clientes.Add(cliente);
            for (var i = 0; i < 3; i++)
            {
                var m = MensagemProgramada.Agendar(empresa.Id, cliente.Id, null, CanalConversa.Email,
                    FinalidadeContato.Transacional, $"lembrete {i}", null, agora.AddSeconds(1 + i), Guid.NewGuid(), agora);
                db.MensagensProgramadas.Add(m);
                ids.Add(m.Id);
            }
            await db.SaveChangesAsync();
        }

        var vence = agora.AddMinutes(1);
        await using var dbA = fixture.CreateDbContext();
        await using var dbB = fixture.CreateDbContext();
        using var bypassA = dbA.UseRowLevelSecurityBypass();
        using var bypassB = dbB.UseRowLevelSecurityBypass();

        await using var txA = await dbA.Database.BeginTransactionAsync();
        var pegasA = await new MensagemProgramadaRepository(dbA).ListarVencidasComLockAsync(vence, 2);

        // A ainda segura o lock das duas primeiras: B só enxerga a terceira.
        await using var txB = await dbB.Database.BeginTransactionAsync();
        var pegasB = await new MensagemProgramadaRepository(dbB).ListarVencidasComLockAsync(vence, 10);

        foreach (var m in pegasA) m.Reservar(vence);
        foreach (var m in pegasB) m.Reservar(vence);
        await dbA.SaveChangesAsync();
        await txA.CommitAsync();
        await dbB.SaveChangesAsync();
        await txB.CommitAsync();

        var todas = pegasA.Select(m => m.Id).Concat(pegasB.Select(m => m.Id)).ToList();
        todas.Should().OnlyHaveUniqueItems("nenhuma mensagem pode ser pega pelos dois disparadores");
        todas.Should().BeEquivalentTo(ids);

        await using var dbC = fixture.CreateDbContext();
        using var bypassC = dbC.UseRowLevelSecurityBypass();
        (await new MensagemProgramadaRepository(dbC).ListarVencidasComLockAsync(vence, 10))
            .Should().BeEmpty("depois de reservadas, saem da fila das agendadas");
        (await dbC.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'mensagens_programadas'")
            .SingleAsync())
            .Should().Be(1);
    }
}
