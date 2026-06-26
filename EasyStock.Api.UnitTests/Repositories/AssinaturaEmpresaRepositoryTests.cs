using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.UnitTests.Repositories;

/// <summary>
/// ADM-004 (#694): o CobrancaAssinaturaJob distingue inadimplencia de plano PAGO
/// (GetAtivasVencidasAsync -> Suspensa) de teste nao convertido
/// (GetTrialsExpiradosAsync -> Expirada). A correcao tambem evita suspender clientes
/// pagantes cujo TrialFim esta no passado (TrialFim nunca e limpo na conversao).
/// As queries usam IgnoreQueryFilters (job cross-tenant), entao o teste independe de contexto.
/// </summary>
public class AssinaturaEmpresaRepositoryTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly AssinaturaEmpresaRepository _repo;
    private static readonly DateTime Now = DateTime.UtcNow;

    public AssinaturaEmpresaRepositoryTests()
    {
        _db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"assinatura-repo-tests-{Guid.NewGuid()}")
            .Options);
        _repo = new AssinaturaEmpresaRepository(_db);
    }

    [Fact]
    public async Task GetAtivasVencidas_pega_plano_pago_lapso_ignora_pago_vigente_e_trial_only()
    {
        var pagoLapso     = Seed(trial: null, dataFim: Now.AddDays(-1));   // pago vencido -> pega
        var pagoVigente   = Seed(trial: Now.AddDays(-30), dataFim: Now.AddDays(20)); // pagante (trial passado) -> NAO
        var trialOnly     = Seed(trial: Now.AddDays(-1), dataFim: null);   // trial nao convertido -> NAO (vai p/ outra trilha)
        await _db.SaveChangesAsync();

        var ids = (await _repo.GetAtivasVencidasAsync()).Select(a => a.Id).ToList();

        ids.Should().Contain(pagoLapso.Id);
        ids.Should().NotContain(pagoVigente.Id);
        ids.Should().NotContain(trialOnly.Id);
    }

    [Fact]
    public async Task GetTrialsExpirados_pega_trial_only_vencido_ignora_pago_e_trial_vigente()
    {
        var trialExpirado = Seed(trial: Now.AddDays(-1), dataFim: null);   // -> pega
        var trialVigente  = Seed(trial: Now.AddDays(2), dataFim: null);    // trial ativo -> NAO
        var convertido    = Seed(trial: Now.AddDays(-1), dataFim: Now.AddDays(20)); // pago -> NAO
        await _db.SaveChangesAsync();

        var ids = (await _repo.GetTrialsExpiradosAsync()).Select(a => a.Id).ToList();

        ids.Should().Contain(trialExpirado.Id);
        ids.Should().NotContain(trialVigente.Id);
        ids.Should().NotContain(convertido.Id);
    }

    [Theory]
    [InlineData(StatusAssinatura.Ativa, StatusAssinatura.Expirada)]
    [InlineData(StatusAssinatura.Suspensa, StatusAssinatura.Suspensa)]   // idempotente: so age sobre Ativa
    [InlineData(StatusAssinatura.Cancelada, StatusAssinatura.Cancelada)]
    public void ExpirarPorTrial_so_transiciona_de_ativa(StatusAssinatura inicial, StatusAssinatura esperado)
    {
        var a = new AssinaturaEmpresa { Status = inicial };

        a.ExpirarPorTrial();

        a.Status.Should().Be(esperado);
    }

    private AssinaturaEmpresa Seed(DateTime? trial, DateTime? dataFim)
    {
        var a = new AssinaturaEmpresa
        {
            Id = Guid.NewGuid(),
            EmpresaId = Guid.NewGuid(),
            PlanoId = Guid.NewGuid(),
            DataInicio = Now.AddDays(-40),
            Status = StatusAssinatura.Ativa,
            TrialFim = trial,
            DataFim = dataFim
        };
        _db.AssinaturasEmpresa.Add(a);
        return a;
    }

    public void Dispose() => _db.Dispose();
}
