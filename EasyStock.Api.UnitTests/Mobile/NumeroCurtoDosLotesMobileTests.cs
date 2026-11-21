using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>#2209: número curto dos lotes no sync do app. Teto por chamada, falha de um lote não derruba os outros.</summary>
public sealed class NumeroCurtoDosLotesMobileTests : IDisposable
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly EasyStockDbContext _db;
    private readonly ILoteNumeroCurtoRepository _repo = Substitute.For<ILoteNumeroCurtoRepository>();

    public NumeroCurtoDosLotesMobileTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresa);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase($"numero-curto-{Guid.NewGuid()}").Options,
            currentUser);
        _repo.NumerosDadosAsync(default, default!, default).ReturnsForAnyArgs(new Dictionary<Guid, string>());
    }

    public void Dispose() => _db.Dispose();

    private NumeroCurtoDosLotesMobile Servico() => new(_db, _repo, NullLogger<NumeroCurtoDosLotesMobile>.Instance);

    [Fact]
    public async Task LoteAntigoQueFalha_NaoDerrubaOsJaNumeradosNemOsOutros()
    {
        Guid comNumero = Guid.NewGuid(), falha = Guid.NewGuid(), antigo = Guid.NewGuid();
        _repo.NumerosDadosAsync(_empresa, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [comNumero] = "00000001" });
        _repo.GarantirAsync(_empresa, falha, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("deadlock"));
        _repo.GarantirAsync(_empresa, antigo, Arg.Any<CancellationToken>()).Returns("00000003");

        var numeros = await Servico().DosLotesAsync(_empresa, [comNumero, falha, antigo], CancellationToken.None);

        numeros.Should().BeEquivalentTo(new Dictionary<Guid, string> { [comNumero] = "00000001", [antigo] = "00000003" });
        await _repo.DidNotReceive().GarantirAsync(_empresa, comNumero, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LotesAntigos_NumeraNoMaximo200PorChamada()
    {
        var ids = Enumerable.Range(0, 250).Select(_ => Guid.NewGuid()).ToList();
        _repo.GarantirAsync(_empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => c.ArgAt<Guid>(1).ToString("N")[..8]);

        var numeros = await Servico().DosLotesAsync(_empresa, ids, CancellationToken.None);

        numeros.Should().HaveCount(NumeroCurtoDosLotesMobile.MaximoNovosPorChamada);
        await _repo.Received(NumeroCurtoDosLotesMobile.MaximoNovosPorChamada)
            .GarantirAsync(_empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoAparelho_SoLotesDaLojaDoAparelhoOuSemLoja()
    {
        Guid lojaA1 = Guid.NewGuid(), lojaA2 = Guid.NewGuid();
        Guid loteA1 = Guid.NewGuid(), loteA2 = Guid.NewGuid(), loteSemLoja = Guid.NewGuid();
        _db.Set<Batch>().AddRange(
            new Batch { Id = "b-a1", Code = "L1", EmpresaId = _empresa, LojaId = lojaA1, ErpLoteId = loteA1 },
            new Batch { Id = "b-a2", Code = "L2", EmpresaId = _empresa, LojaId = lojaA2, ErpLoteId = loteA2 },
            new Batch { Id = "b-sem", Code = "L3", EmpresaId = _empresa, LojaId = null, ErpLoteId = loteSemLoja });
        await _db.SaveChangesAsync();
        _repo.NumerosDadosAsync(_empresa, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.ArgAt<IReadOnlyCollection<Guid>>(1).ToDictionary(id => id, id => id.ToString("N")[..8]));

        var numeros = await Servico().DosLotesDoAparelhoAsync(_empresa, lojaA2, ["b-a1", "b-a2", "b-sem"], CancellationToken.None);

        numeros.Keys.Should().BeEquivalentTo(["b-a2", "b-sem"], "o id vem do payload: o lote da A1 não sai nem é numerado");
        await _repo.Received(1).NumerosDadosAsync(_empresa,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => !ids.Contains(loteA1)), Arg.Any<CancellationToken>());
    }
}
