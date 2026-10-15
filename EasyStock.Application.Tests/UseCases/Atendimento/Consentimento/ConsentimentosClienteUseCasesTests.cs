using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Consentimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Consentimento;

/// <summary>S38: a dona vê e registra, por canal, o consentimento que o cliente deu.</summary>
public class ConsentimentosClienteUseCasesTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IConsentimentoContatoRepository _repo = Substitute.For<IConsentimentoContatoRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero));

    public ConsentimentosClienteUseCasesTests()
    {
        _clientes.GetByIdAsync(_empresaId, _clienteId).Returns(new Cliente { Id = _clienteId, EmpresaId = _empresaId, Nome = "Fulana" });
        _repo.ListarDoClienteAsync(_empresaId, _clienteId, Arg.Any<CancellationToken>()).Returns(new List<ConsentimentoContato>());
    }

    [Fact]
    public async Task Listar_SemRegistro_MostraTodosOsCanaisComTransacionalLiberadoEMarketingNao()
    {
        var linhas = await new ListarConsentimentosClienteUseCase(_clientes, _repo).ExecuteAsync(_empresaId, _clienteId);

        linhas.Should().HaveCount(Enum.GetValues<CanalConversa>().Length * 2);
        linhas.Where(l => l.Finalidade == FinalidadeContato.Marketing).Should().OnlyContain(l => !l.PodeEnviar && l.Situacao == null);
        linhas.Where(l => l.Finalidade == FinalidadeContato.Transacional).Should().OnlyContain(l => l.PodeEnviar);
    }

    [Fact]
    public async Task Definir_ConcedeMarketingNoEmail_GravaComOrigemDoConsole()
    {
        var usuario = Guid.NewGuid();
        var useCase = new DefinirConsentimentosClienteUseCase(_clientes, _repo, _uow, _relogio);

        var linhas = await useCase.ExecuteAsync(new DefinirConsentimentosClienteCommand(_empresaId, _clienteId, usuario,
            [new ConsentimentoItem(CanalConversa.Email, FinalidadeContato.Marketing, SituacaoConsentimento.Concedido)]));

        linhas.Single(l => l.Canal == CanalConversa.Email && l.Finalidade == FinalidadeContato.Marketing).PodeEnviar.Should().BeTrue();
        linhas.Single(l => l.Canal == CanalConversa.Sms && l.Finalidade == FinalidadeContato.Marketing).PodeEnviar.Should().BeFalse();
        await _repo.Received(1).AddAsync(Arg.Is<ConsentimentoContato>(c => c.Origem == $"console:{usuario:N}"), Arg.Any<CancellationToken>());
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Definir_ClienteDeOutraEmpresa_LancaNaoEncontrado()
    {
        var useCase = new DefinirConsentimentosClienteUseCase(_clientes, _repo, _uow, _relogio);

        var act = () => useCase.ExecuteAsync(new DefinirConsentimentosClienteCommand(_empresaId, Guid.NewGuid(), Guid.NewGuid(),
            [new ConsentimentoItem(CanalConversa.Email, FinalidadeContato.Marketing, SituacaoConsentimento.Concedido)]));

        await act.Should().ThrowAsync<ClienteNaoEncontradoParaConsentimentoException>();
        _uow.CommitCount.Should().Be(0);
    }
}
