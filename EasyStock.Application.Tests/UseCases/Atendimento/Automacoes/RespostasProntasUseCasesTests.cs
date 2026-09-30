using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Automacoes;

/// <summary>S42: biblioteca de respostas prontas (atalho único, render no servidor) e cadastro das regras.</summary>
public class RespostasProntasUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IRespostaProntaRepository _repo = Substitute.For<IRespostaProntaRepository>();
    private readonly IRegraAutomaticaRepository _regras = Substitute.For<IRegraAutomaticaRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));

    private RespostasProntasUseCases UseCases() =>
        new(_repo, _conversas, new VariaveisAtendimento(_clientes, _pedidos), _uow, _relogio);

    [Fact]
    public async Task CriarComAtalhoDuplicadoRecusa()
    {
        _repo.ExisteAtalhoAsync(_empresaId, "/oi", null, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => UseCases().CriarAsync(new SalvarRespostaProntaCommand(_empresaId, "Oi", "/OI", "Olá"));

        await act.Should().ThrowAsync<AtalhoDuplicadoException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task CriarGravaERetorna()
    {
        var resultado = await UseCases().CriarAsync(new SalvarRespostaProntaCommand(_empresaId, "Oi", "/oi", "Olá {nome}"));

        resultado.Atalho.Should().Be("/oi");
        await _repo.Received(1).AddAsync(Arg.Any<RespostaPronta>(), Arg.Any<CancellationToken>());
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task EditarParaAtalhoDeOutraRecusa()
    {
        var resposta = RespostaPronta.Criar(_empresaId, "A", "/a", "x", Agora);
        _repo.ObterAsync(_empresaId, resposta.Id, Arg.Any<CancellationToken>()).Returns(resposta);
        _repo.ExisteAtalhoAsync(_empresaId, "/b", resposta.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => UseCases().EditarAsync(resposta.Id, new SalvarRespostaProntaCommand(_empresaId, "A", "/b", "x"));

        await act.Should().ThrowAsync<AtalhoDuplicadoException>();
    }

    [Fact]
    public async Task RenderComPedidoNumaConversaSemPedidoFalhaComValidacao()
    {
        var resposta = RespostaPronta.Criar(_empresaId, "Pedido", "/pedido", "Seu pedido {pedido} saiu.", Agora);
        _repo.ObterAsync(_empresaId, resposta.Id, Arg.Any<CancellationToken>()).Returns(resposta);
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora);
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var act = () => UseCases().RenderizarAsync(_empresaId, resposta.Id, conversa.Id);

        (await act.Should().ThrowAsync<UseCaseValidationException>()).WithMessage("*{pedido}*");
    }

    [Fact]
    public async Task RenderUsaPrimeiroNomeDoClienteEPedidoDaConversa()
    {
        var resposta = RespostaPronta.Criar(_empresaId, "Pedido", "/pedido", "{nome}, pedido {pedido}.", Agora);
        _repo.ObterAsync(_empresaId, resposta.Id, Arg.Any<CancellationToken>()).Returns(resposta);
        var cliente = new ClienteEntity { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Bia Lima" };
        _clientes.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora);
        conversa.VincularCliente(cliente.Id);
        conversa.DefinirPedidoEmAndamento(Guid.Parse("0a1b2c3d-0000-0000-0000-000000000000"));
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var texto = await UseCases().RenderizarAsync(_empresaId, resposta.Id, conversa.Id);

        texto.Texto.Should().Be("Bia, pedido 0A1B2C3D.");
    }

    [Fact]
    public async Task SalvarAutomacaoCriaQuandoNaoExisteEAlteraQuandoExiste()
    {
        var useCase = new AutomacoesUseCases(_regras, _uow, _relogio);

        var criada = await useCase.SalvarAsync(new SalvarAutomacaoCommand(_empresaId, GatilhoAutomacao.PosEntrega, "Chegou?", true));
        criada.Ligada.Should().BeTrue();
        await _regras.Received(1).AddAsync(Arg.Any<RegraAutomatica>(), Arg.Any<CancellationToken>());

        var existente = RegraAutomatica.Criar(_empresaId, GatilhoAutomacao.PosEntrega, "Chegou?", true, Agora);
        _regras.ObterPorGatilhoAsync(_empresaId, GatilhoAutomacao.PosEntrega, Arg.Any<CancellationToken>()).Returns(existente);

        var desligada = await useCase.SalvarAsync(new SalvarAutomacaoCommand(_empresaId, GatilhoAutomacao.PosEntrega, "Chegou?", false));
        desligada.Ligada.Should().BeFalse();
        existente.Ligada.Should().BeFalse();
        _uow.CommitCount.Should().Be(2);
    }
}
