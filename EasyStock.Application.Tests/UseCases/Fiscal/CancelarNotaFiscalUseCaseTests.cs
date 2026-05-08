using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Fiscal.CancelarNotaFiscal;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.ValueObjects.Fiscal;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Fiscal;

public class CancelarNotaFiscalUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LojaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Usuario = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static NotaFiscal NotaAutorizada(DateTime? dhAuth = null)
    {
        var nota = NotaFiscal.CriarParaEmissao(
            EmpresaId, LojaId, Guid.NewGuid(),
            ModeloDocumentoFiscal.NFCe, 1, 1,
            ChaveAcessoNFe.Construir("35", DateTime.UtcNow, "12345678000190",
                ModeloDocumentoFiscal.NFCe, 1, 1, TipoEmissao.Normal, "00000001"),
            TipoEmissao.Normal, AmbienteSefaz.Homologacao, DateTime.UtcNow,
            Dinheiro.FromDecimal(10m), null, "k1", "test", Usuario);
        nota.MarcarAutorizada("PROT123", "<xml/>", dhAuth ?? DateTime.UtcNow);
        return nota;
    }

    [Fact]
    public async Task Justificativa_curta_lanca_validation()
    {
        var sut = Sut();
        var act = () => sut.ExecuteAsync(new CancelarNotaFiscalCommand(EmpresaId, Guid.NewGuid(), "curto", Usuario));
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*15*255*");
    }

    [Fact]
    public async Task Nota_inexistente_lanca_validation()
    {
        var deps = new Deps();
        deps.Repo.ObterPorIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((NotaFiscal?)null);

        var sut = deps.Build();
        var act = () => sut.ExecuteAsync(new CancelarNotaFiscalCommand(
            EmpresaId, Guid.NewGuid(), "Justificativa válida com mais de 15 chars", Usuario));
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*nao encontrada*");
    }

    [Fact]
    public async Task Nota_com_prazo_expirado_lanca_validation()
    {
        var nota = NotaAutorizada(DateTime.UtcNow.AddMinutes(-31));
        var deps = new Deps();
        deps.Repo.ObterPorIdAsync(EmpresaId, nota.Id, Arg.Any<CancellationToken>()).Returns(nota);

        var sut = deps.Build();
        var act = () => sut.ExecuteAsync(new CancelarNotaFiscalCommand(
            EmpresaId, nota.Id, "Justificativa válida do operador", Usuario));
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*30 minutos*");
    }

    [Fact]
    public async Task Nota_em_emEmissao_lanca_validation()
    {
        var nota = NotaFiscal.CriarParaEmissao(
            EmpresaId, LojaId, Guid.NewGuid(),
            ModeloDocumentoFiscal.NFCe, 1, 1,
            ChaveAcessoNFe.Construir("35", DateTime.UtcNow, "12345678000190",
                ModeloDocumentoFiscal.NFCe, 1, 1, TipoEmissao.Normal, "00000001"),
            TipoEmissao.Normal, AmbienteSefaz.Homologacao, DateTime.UtcNow,
            Dinheiro.FromDecimal(10m), null, "k", "test", Usuario);

        var deps = new Deps();
        deps.Repo.ObterPorIdAsync(EmpresaId, nota.Id, Arg.Any<CancellationToken>()).Returns(nota);

        var sut = deps.Build();
        var act = () => sut.ExecuteAsync(new CancelarNotaFiscalCommand(
            EmpresaId, nota.Id, "Justificativa válida do operador", Usuario));
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    private static CancelarNotaFiscalUseCase Sut() => new Deps().Build();

    private sealed class Deps
    {
        public INotaFiscalRepository Repo { get; } = Substitute.For<INotaFiscalRepository>();
        public IGatewayFiscal Gateway { get; } = Substitute.For<IGatewayFiscal>();
        public IConfigFiscalResolver ConfigResolver { get; } = Substitute.For<IConfigFiscalResolver>();
        public IPublicadorEventoIntegracao Eventos { get; } = Substitute.For<IPublicadorEventoIntegracao>();
        public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();

        public CancelarNotaFiscalUseCase Build() => new(
            Repo, Gateway, ConfigResolver, Eventos, Uow,
            NullLogger<CancelarNotaFiscalUseCase>.Instance);
    }
}
