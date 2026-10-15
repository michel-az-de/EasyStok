using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Application.UseCases.Atendimento.Programadas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #1781: quem não tem <see cref="Permissao.AtenderConversas"/> (ex.: perfil Cozinha) recebe 403 ao enviar
/// mensagem na conversa; agendar mensagem e mexer nas respostas prontas tem o mesmo efeito e exige o mesmo.
/// Ler continua bastando a policy Operador (decisão do S41).
/// </summary>
public class AtendimentoEscritaPermissaoTests
{
    private readonly ICurrentUserAccessor _usuario = Substitute.For<ICurrentUserAccessor>();
    private readonly IMensagemProgramadaRepository _programadas = Substitute.For<IMensagemProgramadaRepository>();
    private readonly IRespostaProntaRepository _respostas = Substitute.For<IRespostaProntaRepository>();

    public AtendimentoEscritaPermissaoTests()
    {
        _usuario.EmpresaId.Returns(Guid.NewGuid());
        _usuario.IsAuthenticated.Returns(true);
        _programadas.ListarAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<MensagemProgramada>>([]));
        _respostas.ListarAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RespostaPronta>>([]));
    }

    private void ComoPerfil(string nome)
    {
        var permissoes = PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == nome).Permissoes.ToHashSet();
        _usuario.Nivel.Returns(NivelAcesso.Operador);
        _usuario.TemPermissao(Arg.Any<Permissao>()).Returns(c => permissoes.Contains(c.Arg<Permissao>()));
    }

    private AtendimentoMensagensProgramadasController Programadas() => new(
        new AgendarMensagemProgramadaUseCase(Substitute.For<IClienteRepository>(), Substitute.For<IConversaRepository>(),
            _programadas, null!, Substitute.For<IUnitOfWork>(), TimeProvider.System),
        new ListarMensagensProgramadasUseCase(_programadas),
        new CancelarMensagemProgramadaUseCase(_programadas, Substitute.For<IUnitOfWork>(), TimeProvider.System),
        _usuario);

    private AtendimentoRespostasProntasController Respostas() => new(
        new RespostasProntasUseCases(_respostas, Substitute.For<IConversaRepository>(), null!,
            Substitute.For<IUnitOfWork>(), TimeProvider.System),
        _usuario);

    private static AgendarMensagemProgramadaBody Agendamento() => new(
        Guid.NewGuid(), null, CanalConversa.WhatsApp, FinalidadeContato.Transacional, "oi", null, DateTime.UtcNow.AddHours(1));

    [Fact]
    public async Task CozinhaNaoAgendaNemCancelaMensagemProgramada()
    {
        ComoPerfil("Cozinha");

        (await Programadas().Agendar(Agendamento(), default)).Should().BeOfType<ForbidResult>();
        (await Programadas().Cancelar(Guid.NewGuid(), default)).Should().BeOfType<ForbidResult>();
        await _programadas.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task CozinhaNaoCriaEditaNemArquivaRespostaPronta()
    {
        ComoPerfil("Cozinha");
        var corpo = new RespostaProntaBody("Oi", "oi", "Olá!");

        (await Respostas().Criar(corpo, default)).Should().BeOfType<ForbidResult>();
        (await Respostas().Editar(Guid.NewGuid(), corpo, default)).Should().BeOfType<ForbidResult>();
        (await Respostas().Arquivar(Guid.NewGuid(), true, default)).Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task CozinhaContinuaLendo()
    {
        ComoPerfil("Cozinha");

        (await Programadas().Listar(null, null, default)).Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(200);
        (await Respostas().Listar(false, default)).Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task AtendimentoPassaPelaGuarda()
    {
        ComoPerfil("Atendimento");

        (await Respostas().Arquivar(Guid.NewGuid(), true, default)).Should().NotBeOfType<ForbidResult>();
        (await Programadas().Cancelar(Guid.NewGuid(), default)).Should().NotBeOfType<ForbidResult>();
    }
}
