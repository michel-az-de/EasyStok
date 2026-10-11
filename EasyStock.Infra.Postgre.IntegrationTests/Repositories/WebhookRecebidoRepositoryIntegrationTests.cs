using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1562: o processamento do webhook falhou e a transação voltou atrás, mas o que ele alterou segue rastreado
/// no contexto da requisição. Marcar a falha não pode gravar esse resto junto.
/// </summary>
public class WebhookRecebidoRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task MarcarFalhaDepoisDeRollback_NaoGravaOQueOProcessamentoDeixouRastreado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var evento = Guid.NewGuid().ToString("N");
        var conversa = Conversa.Abrir(Guid.NewGuid(), "5511999991562", DateTime.UtcNow);

        await using (var db = fixture.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            var repo = new WebhookRecebidoRepository(db);
            var registro = await repo.TryRegistrarAsync("teste-1562", evento, "hash");

            // O processador alterou o contexto dentro da transação e estourou antes de gravar.
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                db.AtendimentoConversas.Add(conversa);
                await tx.RollbackAsync();
            }

            await repo.MarcarProcessadoAsync(registro!.Id, sucesso: false, erro: "falha no processamento");
        }

        await using var conferir = fixture.CreateDbContext();
        using var bypassConferir = conferir.UseRowLevelSecurityBypass();
        (await conferir.AtendimentoConversas.IgnoreQueryFilters().AnyAsync(c => c.Id == conversa.Id))
            .Should().BeFalse("a transação do processamento foi revertida");
        var marcado = await new WebhookRecebidoRepository(conferir).ObterAsync("teste-1562", evento);
        marcado!.ProcessadoEm.Should().NotBeNull();
        marcado.Sucesso.Should().BeFalse();
        marcado.Erro.Should().Be("falha no processamento");
    }

    // O caminho feliz não muda: marcar sucesso não descarta nada do contexto.
    [SkippableFact]
    public async Task MarcarSucesso_GravaORegistro()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var evento = Guid.NewGuid().ToString("N");
        await using (var db = fixture.CreateDbContext())
        {
            var repo = new WebhookRecebidoRepository(db);
            var registro = await repo.TryRegistrarAsync("teste-1562", evento, "hash");
            await repo.MarcarProcessadoAsync(registro!.Id, sucesso: true);
        }

        await using var conferir = fixture.CreateDbContext();
        var marcado = await new WebhookRecebidoRepository(conferir).ObterAsync("teste-1562", evento);
        marcado!.Sucesso.Should().BeTrue();
        marcado.ProcessadoEm.Should().NotBeNull();
    }
}
