using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Lembretes;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Lembretes;

/// <summary>S43, console: criar, concluir e marcar vistos.</summary>
public class LembretesUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly ILembreteRepository _repo = Substitute.For<ILembreteRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));
    private CriarLembreteUseCase Criador() => new(_repo, _uow, _relogio, _conversas, _pedidos, _usuarios);

    [Fact]
    public async Task Criar_SemTexto_RecusaSemGravar()
    {
        var act = () => Criador().ExecuteAsync(
            new CriarLembreteCommand(_empresaId, _usuarioId, " ", null, null, null, null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Criar_GravaManualComQuemCriou()
    {
        var resultado = await Criador().ExecuteAsync(
            new CriarLembreteCommand(_empresaId, _usuarioId, "Ligar para a Fulana", Agora.AddHours(2), null, null, null));

        resultado.VenceEm.Should().Be(Agora.AddHours(2));
        await _repo.Received(1).AddAsync(Arg.Is<Lembrete>(l => l.CriadoPorUsuarioId == _usuarioId && l.EmpresaId == _empresaId), Arg.Any<CancellationToken>());
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Concluir_Inexistente_LancaNaoEncontrado()
    {
        var act = () => new ConcluirLembreteUseCase(_repo, _uow, _relogio).ExecuteAsync(_empresaId, _usuarioId, Guid.NewGuid());

        await act.Should().ThrowAsync<LembreteNaoEncontradoException>();
    }

    [Theory]
    [InlineData("conversa")]
    [InlineData("pedido")]
    [InlineData("destinatario")]
    public async Task Criar_RecusaVinculoDeOutraEmpresa(string vinculo)
    {
        var outraEmpresa = Guid.NewGuid();
        var conversa = Conversa.Abrir(outraEmpresa, "5511999990001", Agora);
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        var pedido = new Pedido { Id = Guid.NewGuid(), EmpresaId = outraEmpresa };
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        var usuario = Usuario.Criar("Outro", "outro@teste.local", "hash");
        usuario.Empresas.Add(new UsuarioEmpresa { EmpresaId = outraEmpresa, Ativo = true });
        _usuarios.GetByIdAsync(usuario.Id).Returns(usuario);

        var act = () => Criador().ExecuteAsync(new CriarLembreteCommand(_empresaId, _usuarioId, "Lembrar", Agora,
            vinculo == "destinatario" ? usuario.Id : null, vinculo == "conversa" ? conversa.Id : null,
            vinculo == "pedido" ? pedido.Id : null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _uow.CommitCount.Should().Be(0);
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concluir_RecusaOutraEmpresaOuOutroDestinatario(bool outraEmpresa)
    {
        var lembrete = Lembrete.Manual(outraEmpresa ? Guid.NewGuid() : _empresaId, "Privado", Agora, _usuarioId, Agora,
            paraUsuarioId: outraEmpresa ? _usuarioId : Guid.NewGuid());
        _repo.ObterAsync(_empresaId, lembrete.Id, Arg.Any<CancellationToken>()).Returns(lembrete);

        var act = () => new ConcluirLembreteUseCase(_repo, _uow, _relogio).ExecuteAsync(_empresaId, _usuarioId, lembrete.Id);

        await act.Should().ThrowAsync<LembreteNaoEncontradoException>();
        lembrete.EstaAberto.Should().BeTrue();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task ListarEVistos_DefendemEmpresaEDestinatarioMesmoSeRepositorioFalhar()
    {
        var proprio = Lembrete.Manual(_empresaId, "Meu", Agora, _usuarioId, Agora, paraUsuarioId: _usuarioId);
        var equipe = Lembrete.Manual(_empresaId, "Equipe", Agora, _usuarioId, Agora);
        var alheio = Lembrete.Manual(_empresaId, "Outro", Agora, _usuarioId, Agora, paraUsuarioId: Guid.NewGuid());
        var externo = Lembrete.Manual(Guid.NewGuid(), "Externo", Agora, _usuarioId, Agora);
        _repo.ListarAsync(_empresaId, _usuarioId, false, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([proprio, equipe, alheio, externo]);

        var lista = await new ListarLembretesUseCase(_repo).ExecuteAsync(_empresaId, _usuarioId, false, false);
        var vistos = await new MarcarLembretesVistosUseCase(_repo, _uow, _relogio).ExecuteAsync(_empresaId, _usuarioId);

        lista.Select(l => l.Id).Should().BeEquivalentTo([proprio.Id, equipe.Id]);
        vistos.Should().Be(2);
        alheio.VistoEm.Should().BeNull();
        externo.VistoEm.Should().BeNull();
    }

    [Fact]
    public async Task MarcarVistos_SoOsVencidosAindaNaoVistos()
    {
        var vencido = Lembrete.Manual(_empresaId, "vencido", Agora.AddMinutes(-5), _usuarioId, Agora.AddMinutes(-10));
        var futuro = Lembrete.Manual(_empresaId, "futuro", Agora.AddHours(1), _usuarioId, Agora);
        var jaVisto = Lembrete.Manual(_empresaId, "visto", Agora.AddMinutes(-5), _usuarioId, Agora.AddMinutes(-10));
        jaVisto.MarcarVisto(Agora.AddMinutes(-1));
        _repo.ListarAsync(_empresaId, _usuarioId, false, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([vencido, futuro, jaVisto]);

        var marcados = await new MarcarLembretesVistosUseCase(_repo, _uow, _relogio).ExecuteAsync(_empresaId, _usuarioId);

        marcados.Should().Be(1);
        vencido.VistoEm.Should().Be(Agora);
        futuro.VistoEm.Should().BeNull("ainda não apareceu no sininho");
        jaVisto.VistoEm.Should().Be(Agora.AddMinutes(-1));
    }
}
