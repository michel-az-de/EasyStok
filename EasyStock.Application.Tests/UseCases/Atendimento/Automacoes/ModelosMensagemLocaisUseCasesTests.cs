using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Automacoes;

/// <summary>#3204: mensagens padrão locais (nome + idioma único por empresa, contando as arquivadas).</summary>
public class ModelosMensagemLocaisUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 11, 22, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IModeloMensagemLocalRepository _repo = Substitute.For<IModeloMensagemLocalRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));

    private ModelosMensagemLocaisUseCases UseCases() => new(_repo, _uow, _relogio);

    private SalvarModeloMensagemLocalCommand Comando(string nome = "pedido_pronto", string corpo = "Oi {{1}}, seu pedido está pronto.") =>
        new(_empresaId, nome, "pt_BR", "UTILITY", null, corpo, null);

    [Fact]
    public async Task CriarGravaERetornaOResultado()
    {
        var resultado = await UseCases().CriarAsync(Comando("Pedido_Pronto"));

        resultado.Nome.Should().Be("pedido_pronto");
        resultado.Idioma.Should().Be("pt_BR");
        resultado.Categoria.Should().Be("UTILITY");
        resultado.QuantidadeParametros.Should().Be(1);
        resultado.Arquivado.Should().BeFalse();
        resultado.AlteradoEm.Should().Be(Agora);
        await _repo.Received(1).AddAsync(Arg.Any<ModeloMensagemLocal>(), Arg.Any<CancellationToken>());
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task CriarComNomeEIdiomaJaUsadosRecusa()
    {
        _repo.ExisteAsync(_empresaId, "pedido_pronto", "pt_BR", null, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => UseCases().CriarAsync(Comando("Pedido_Pronto"));

        await act.Should().ThrowAsync<ModeloMensagemLocalDuplicadoException>();
        await _repo.DidNotReceive().AddAsync(Arg.Any<ModeloMensagemLocal>(), Arg.Any<CancellationToken>());
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task CriarInvalidoViraErroDeValidacao()
    {
        var act = () => UseCases().CriarAsync(Comando(corpo: "{{1}} no início"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task EditarAtualizaEGrava()
    {
        var modelo = ModeloMensagemLocal.Criar(_empresaId, "a", "pt_BR", "UTILITY", null, "texto", null, Agora.AddDays(-1));
        _repo.ObterAsync(_empresaId, modelo.Id, Arg.Any<CancellationToken>()).Returns(modelo);

        var resultado = await UseCases().EditarAsync(modelo.Id, Comando("Novo_Nome"));

        resultado.Nome.Should().Be("novo_nome");
        resultado.AlteradoEm.Should().Be(Agora);
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task EditarParaNomeEIdiomaDeOutroRecusa()
    {
        var modelo = ModeloMensagemLocal.Criar(_empresaId, "a", "pt_BR", "UTILITY", null, "texto", null, Agora);
        _repo.ObterAsync(_empresaId, modelo.Id, Arg.Any<CancellationToken>()).Returns(modelo);
        _repo.ExisteAsync(_empresaId, "b", "pt_BR", modelo.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => UseCases().EditarAsync(modelo.Id, Comando("b"));

        await act.Should().ThrowAsync<ModeloMensagemLocalDuplicadoException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task EditarInexistenteFalha()
    {
        var act = () => UseCases().EditarAsync(Guid.NewGuid(), Comando());

        await act.Should().ThrowAsync<ModeloMensagemLocalNaoEncontradoException>();
    }

    [Fact]
    public async Task ArquivarMarcaEGrava()
    {
        var modelo = ModeloMensagemLocal.Criar(_empresaId, "a", "pt_BR", "UTILITY", null, "texto", null, Agora.AddDays(-1));
        _repo.ObterAsync(_empresaId, modelo.Id, Arg.Any<CancellationToken>()).Returns(modelo);

        var resultado = await UseCases().ArquivarAsync(_empresaId, modelo.Id, true);

        resultado.Arquivado.Should().BeTrue();
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task ArquivarInexistenteFalha()
    {
        var act = () => UseCases().ArquivarAsync(_empresaId, Guid.NewGuid(), true);

        await act.Should().ThrowAsync<ModeloMensagemLocalNaoEncontradoException>();
    }

    [Fact]
    public async Task ListarDevolveOsResultados()
    {
        var modelo = ModeloMensagemLocal.Criar(_empresaId, "a", "pt_BR", "UTILITY", "Topo", "Oi {{1}}, ok.", "Rodapé", Agora);
        _repo.ListarAsync(_empresaId, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ModeloMensagemLocal>>([modelo]));

        var lista = await UseCases().ListarAsync(_empresaId, true);

        lista.Should().ContainSingle().Which.Should().Match<ModeloMensagemLocalResult>(r =>
            r.Id == modelo.Id && r.Cabecalho == "Topo" && r.Rodape == "Rodapé" && r.Corpo == "Oi {{1}}, ok." && r.QuantidadeParametros == 1);
    }
}
