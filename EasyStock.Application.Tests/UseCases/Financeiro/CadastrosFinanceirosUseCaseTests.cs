using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Cadastros;
using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

public class CadastrosFinanceirosUseCaseTests
{
    private readonly ICadastrosFinanceirosRepository _repo = Substitute.For<ICadastrosFinanceirosRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _usuario = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _empresa = Guid.NewGuid();

    public CadastrosFinanceirosUseCaseTests()
    {
        _usuario.EmpresaId.Returns(_empresa);
        _usuario.UsuarioId.Returns(Guid.NewGuid());
        _usuario.IsAuthenticated.Returns(true);
        _usuario.Nivel.Returns(NivelAcesso.Admin);
        _usuario.TemPermissao(Arg.Any<Permissao>()).Returns(true);
    }

    private CadastrosFinanceirosUseCase Criar() => new(_repo, _uow, _usuario);
    private static SalvarContaBancariaInput Input(Guid? empresa = null) => new(Guid.NewGuid(), "Recebimentos", "pagamento", "Banco", "", "123-4", "Loja", true, EmpresaId: empresa);

    [Fact]
    public async Task Criacao_UsaTenantEAtorDaSessao_ERecuperaRespostaPerdida()
    {
        var input = Input();
        var caso = Criar();
        var conta = await caso.SalvarContaAsync(null, input, default);
        conta!.EmpresaId.Should().Be(_empresa);
        conta.AlteradoPor.Should().Be(_usuario.UsuarioId);
        _repo.ObterContaAsync(_empresa, input.RequestId, default).Returns(conta);
        var repetida = await caso.SalvarContaAsync(null, input, default);
        repetida!.Revisao.Should().Be(1);
        _repo.Received(1).Adicionar(Arg.Any<ContaBancaria>());
    }

    [Fact]
    public async Task EmpresaDivergente_RecusaAntesDeConsultarOuGravar()
    {
        var executar = () => Criar().SalvarContaAsync(null, Input(Guid.NewGuid()), default);
        await executar.Should().ThrowAsync<UseCaseValidationException>();
        _repo.ReceivedCalls().Should().BeEmpty();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task EntidadeDeOutraEmpresa_NaoEEditadaNemDevolvida()
    {
        var estrangeira = ContaBancaria.Criar(Guid.NewGuid(), Guid.NewGuid(), "Outra loja", "corrente", "Banco", "", "1", "Outro", true, Guid.NewGuid());
        _repo.ObterContaAsync(_empresa, estrangeira.Id, default).Returns(estrangeira);
        (await Criar().SalvarContaAsync(estrangeira.Id, Input(), default)).Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
        _repo.ListarContasAsync(_empresa, default).Returns(new[] { estrangeira });
        (await Criar().ListarContasAsync(default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Operador_NaoGravaMesmoComPermissoesFinanceiras()
    {
        _usuario.Nivel.Returns(NivelAcesso.Operador);
        var executar = () => Criar().SalvarContaAsync(null, Input(), default);
        await executar.Should().ThrowAsync<UnauthorizedAccessException>();
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task SemModuloFinanceiro_NaoLeContas()
    {
        _usuario.TemPermissao(Permissao.AcessarModuloCaixa).Returns(false);
        var executar = () => Criar().ListarContasAsync(default);
        await executar.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task FalhaDePersistencia_NaoRetornaSucesso()
    {
        _uow.CommitAsync().Returns(Task.FromException<int>(new IOException("indisponível")));
        var executar = () => Criar().SalvarContaAsync(null, Input(), default);
        await executar.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task Metodo_ListaCanonicaMantemLegadoEConfiguraSomenteEmpresaAtual()
    {
        _repo.ListarMetodosAsync(_empresa, default).Returns(Array.Empty<MetodoPagamentoEmpresa>());
        var metodos = await Criar().ListarMetodosAsync(default);
        metodos.Should().HaveCount(6).And.OnlyContain(m => m.Ativo && m.Revisao == 0);
        var entrada = new ConfigurarMetodoPagamentoInput(Guid.NewGuid(), false, 0);
        var resultado = await Criar().ConfigurarMetodoAsync("pix", entrada, default);
        resultado.Ativo.Should().BeFalse();
        _repo.Received(1).Adicionar(Arg.Is<MetodoPagamentoEmpresa>(m => m.EmpresaId == _empresa && m.Codigo == "pix"));
    }
}
