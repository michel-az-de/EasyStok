using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Caderno;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Caderno;

/// <summary>S54: cadastro do caderno da loja, com teto do núcleo que vai sempre no prompt do agente.</summary>
public class CadernoUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICadernoRepository _repo = Substitute.For<ICadernoRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));
    private readonly List<TrechoCaderno> _existentes = [];

    public CadernoUseCasesTests()
    {
        _repo.ListarAsync(_empresaId, false, Arg.Any<CancellationToken>()).Returns(_ => _existentes);
    }

    private CadernoUseCases UseCases() => new(_repo, _uow, _relogio);

    /// <summary>Enche o núcleo com trechos de até 4.000 caracteres somando <paramref name="caracteres"/>; devolve o último.</summary>
    private TrechoCaderno Existente(int caracteres, bool nucleo = true)
    {
        TrechoCaderno? ultimo = null;
        for (var resto = caracteres; resto > 0; resto -= TrechoCaderno.TextoTamanhoMaximo)
        {
            var trecho = TrechoCaderno.Criar(_empresaId, "Existente",
                new string('a', Math.Min(resto, TrechoCaderno.TextoTamanhoMaximo)), null, nucleo, Agora);
            _existentes.Add(trecho);
            _repo.ObterAsync(_empresaId, trecho.Id, Arg.Any<CancellationToken>()).Returns(trecho);
            ultimo = trecho;
        }
        return ultimo!;
    }

    [Fact]
    public async Task CriarGravaERetornaComCodigo()
    {
        var resultado = await UseCases().CriarAsync(
            new SalvarTrechoCadernoCommand(_empresaId, "Troca", "Trocamos em até 24 h.", "troca, devolução", Nucleo: false));

        resultado.Titulo.Should().Be("Troca");
        resultado.Codigo.Should().HaveLength(8);
        resultado.PalavrasChave.Should().Be("troca, devolução");
        await _repo.Received(1).AddAsync(Arg.Is<TrechoCaderno>(t => t.EmpresaId == _empresaId), Arg.Any<CancellationToken>());
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task CriarNucleoAcimaDoTetoRecusa()
    {
        Existente(TrechoCaderno.NucleoTamanhoMaximoTotal - 10);

        var act = () => UseCases().CriarAsync(
            new SalvarTrechoCadernoCommand(_empresaId, "Horário", new string('b', 11), null, Nucleo: true));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*núcleo*");
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task CriarForaDoNucleoIgnoraOTeto()
    {
        Existente(TrechoCaderno.NucleoTamanhoMaximoTotal);

        var resultado = await UseCases().CriarAsync(
            new SalvarTrechoCadernoCommand(_empresaId, "Congelar", "Dura 3 meses no freezer.", null, Nucleo: false));

        resultado.Nucleo.Should().BeFalse();
    }

    [Fact]
    public async Task EditarNaoContaOProprioTextoDuasVezes()
    {
        // 4.000 + 1.995 no núcleo; o segundo vira 2.000: soma 6.000 sem contar o texto antigo dele.
        var trecho = Existente(TrechoCaderno.NucleoTamanhoMaximoTotal - 5);

        var resultado = await UseCases().EditarAsync(trecho.Id,
            new SalvarTrechoCadernoCommand(_empresaId, "Existente", new string('c', 2000), null, Nucleo: true));

        resultado.Texto.Should().HaveLength(2000);
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task DesarquivarNucleoAcimaDoTetoRecusa()
    {
        Existente(TrechoCaderno.NucleoTamanhoMaximoTotal - 3);
        var arquivado = TrechoCaderno.Criar(_empresaId, "Antigo", "texto longo", null, nucleo: true, Agora);
        arquivado.Arquivar(true, Agora);
        _repo.ObterAsync(_empresaId, arquivado.Id, Arg.Any<CancellationToken>()).Returns(arquivado);

        var act = () => UseCases().ArquivarAsync(_empresaId, arquivado.Id, arquivado: false);

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task EditarInexistenteDevolveNaoEncontrado()
    {
        var act = () => UseCases().EditarAsync(Guid.NewGuid(),
            new SalvarTrechoCadernoCommand(_empresaId, "x", "y", null, false));

        await act.Should().ThrowAsync<TrechoCadernoNaoEncontradoException>();
    }

    [Fact]
    public async Task TituloVazioViraValidacao()
    {
        var act = () => UseCases().CriarAsync(new SalvarTrechoCadernoCommand(_empresaId, " ", "y", null, false));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
