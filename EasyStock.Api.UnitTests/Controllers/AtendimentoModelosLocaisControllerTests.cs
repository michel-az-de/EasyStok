using System.Reflection;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#3204: ler as mensagens padrão locais basta a policy Operador; criar, editar e arquivar exigem Gerente.</summary>
public class AtendimentoModelosLocaisControllerTests
{
    private readonly ICurrentUserAccessor _usuario = Substitute.For<ICurrentUserAccessor>();
    private readonly IModeloMensagemLocalRepository _modelos = Substitute.For<IModeloMensagemLocalRepository>();

    public AtendimentoModelosLocaisControllerTests()
    {
        _usuario.EmpresaId.Returns(Guid.NewGuid());
        _usuario.IsAuthenticated.Returns(true);
        _modelos.ListarAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ModeloMensagemLocal>>([]));
    }

    private AtendimentoModelosLocaisController Controller() => new(
        new ModelosMensagemLocaisUseCases(_modelos, Substitute.For<IUnitOfWork>(), TimeProvider.System), _usuario);

    private static string? Policy(MemberInfo membro) => membro.GetCustomAttribute<AuthorizeAttribute>()?.Policy;

    private static MethodInfo Acao(string nome) => typeof(AtendimentoModelosLocaisController).GetMethod(nome)!;

    [Fact]
    public void RotaEPoliciesSeguemAsRespostasProntas()
    {
        typeof(AtendimentoModelosLocaisController).GetCustomAttribute<RouteAttribute>()!.Template
            .Should().Be("api/atendimento/modelos-locais");
        Policy(typeof(AtendimentoModelosLocaisController)).Should().Be("Operador");
        Policy(Acao("Listar")).Should().BeNull("ler basta a policy Operador da classe");
        Policy(Acao("Criar")).Should().Be("Gerente");
        Policy(Acao("Editar")).Should().Be("Gerente");
        Policy(Acao("Arquivar")).Should().Be("Gerente");
    }

    [Fact]
    public async Task OperadorLe()
    {
        _usuario.Nivel.Returns(NivelAcesso.Operador);
        _usuario.TemPermissao(Arg.Any<Permissao>()).Returns(false);

        (await Controller().Listar(false, default)).Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task QuemNaoPodeAtenderNaoEscreve()
    {
        _usuario.Nivel.Returns(NivelAcesso.Gerente);
        _usuario.TemPermissao(Arg.Any<Permissao>()).Returns(false);
        var corpo = new ModeloMensagemLocalBody("a", "pt_BR", "UTILITY", null, "texto", null);

        (await Controller().Criar(corpo, default)).Should().BeOfType<ForbidResult>();
        (await Controller().Editar(Guid.NewGuid(), corpo, default)).Should().BeOfType<ForbidResult>();
        (await Controller().Arquivar(Guid.NewGuid(), true, default)).Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task NomeEIdiomaRepetidosViram409()
    {
        _usuario.TemPermissao(Arg.Any<Permissao>()).Returns(true);
        _modelos.ExisteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var resultado = await Controller().Criar(new ModeloMensagemLocalBody("a", "pt_BR", "UTILITY", null, "texto", null), default);

        resultado.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ModeloInexistenteViraNotFoundEInvalidoViraBadRequest()
    {
        _usuario.TemPermissao(Arg.Any<Permissao>()).Returns(true);

        (await Controller().Arquivar(Guid.NewGuid(), true, default)).Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(404);
        (await Controller().Criar(new ModeloMensagemLocalBody("a", "pt_BR", "MARKETING", null, "texto", null), default))
            .Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(400);
    }
}
