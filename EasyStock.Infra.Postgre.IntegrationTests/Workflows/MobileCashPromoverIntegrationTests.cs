using EasyStock.Api.Mobile.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #1615: "desvincular" e "promover" de novo criava um segundo movimento ativo para o mesmo lançamento do PWA, e o
/// valor entrava duas vezes no caixa. A promoção manual usa a mesma referência do vínculo automático. Em Postgres
/// desde o #2629: a promoção toma a trava do caixa numa transação, o que o banco em memória não tem.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class MobileCashPromoverIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    // A chave de mobile_cash_entries é global: um lançamento por teste.
    private readonly string Lancamento = $"cash-{Guid.NewGuid():N}";

    [SkippableFact]
    public async Task Promover_DepoisDeDesvincular_ReligaOMesmoMovimento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await SemearAsync();
        await using var db = Contexto(empresaId);
        var controller = Controller(db, empresaId);

        await controller.Promover(Lancamento, null, default);
        var primeiro = (await MovimentosAsync(empresaId)).Should().ContainSingle().Subject;
        primeiro.Referencia.Should().Be($"mobile:{Lancamento}");

        await controller.Unlink(Lancamento, null, default);
        var resposta = await controller.Promover(Lancamento, null, default);

        resposta.Should().BeOfType<OkObjectResult>();
        (await MovimentosAsync(empresaId)).Should().ContainSingle("o lançamento já tem movimento no caixa");
        (await LancamentoAsync(empresaId)).ErpMovimentoCaixaId.Should().Be(primeiro.Id);
    }

    [SkippableFact]
    public async Task Promover_ComMovimentoEstornado_RecusaEmVezDeReligar()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await SemearAsync();
        await using var db = Contexto(empresaId);
        var controller = Controller(db, empresaId);
        await controller.Promover(Lancamento, null, default);
        var movimento = await db.Set<MovimentoCaixa>().SingleAsync(m => m.EmpresaId == empresaId);
        movimento.Estornar(null, "Gerente", "lançado errado");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await controller.Unlink(Lancamento, null, default);

        var resposta = await controller.Promover(Lancamento, null, default);

        resposta.Should().BeOfType<BadRequestObjectResult>();
        (await MovimentosAsync(empresaId)).Should().ContainSingle();
        (await LancamentoAsync(empresaId)).ErpMovimentoCaixaId.Should().BeNull();
    }

    [SkippableFact]
    public async Task Promover_LancamentoExcluidoNoPwa_Recusa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await SemearAsync(excluido: true);
        await using var db = Contexto(empresaId);

        var resposta = await Controller(db, empresaId).Promover(Lancamento, null, default);

        resposta.Should().BeOfType<BadRequestObjectResult>();
        (await MovimentosAsync(empresaId)).Should().BeEmpty("lançamento excluído não entra no caixa do ERP");
    }

    private EasyStockDbContext Contexto(Guid empresaId)
    {
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return db;
    }

    private static MobileCashController Controller(EasyStockDbContext db, Guid empresaId)
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(empresaId);
        currentUser.Nivel.Returns(NivelAcesso.Gerente);
        var repo = new CaixaRepository(db);
        var registrar = new RegistrarMovimentoCaixaUseCase(repo, db,
            new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(repo)),
            NullLogger<RegistrarMovimentoCaixaUseCase>.Instance);
        return new MobileCashController(db, registrar, currentUser, NullLogger<MobileCashController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private async Task<Guid> SemearAsync(bool excluido = false)
    {
        var empresaId = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.Empresas.Add(new Empresa { Id = empresaId, Nome = "Promover mobile", Documento = Guid.NewGuid().ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        seed.SetMobileTenantContext(empresaId);
        seed.Add(new CashEntry
        {
            Id = Lancamento, Type = "income", Amount = 30m, Description = "Troco",
            CreatedAt = DateTime.UtcNow.AddHours(-1), EmpresaId = empresaId, Metodo = "dinheiro",
            DeletedAt = excluido ? DateTime.UtcNow : null
        });
        await seed.SaveChangesAsync();
        return empresaId;
    }

    private async Task<List<MovimentoCaixa>> MovimentosAsync(Guid empresaId)
    {
        await using var db = Contexto(empresaId);
        return await db.Set<MovimentoCaixa>().AsNoTracking().Where(m => m.EmpresaId == empresaId).ToListAsync();
    }

    private async Task<CashEntry> LancamentoAsync(Guid empresaId)
    {
        await using var db = Contexto(empresaId);
        return await db.Set<CashEntry>().AsNoTracking().SingleAsync(c => c.EmpresaId == empresaId);
    }
}
