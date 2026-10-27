using System.Reflection;
using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.PrimeirosPassos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #2070: rotas da configuração inicial. Policy Admin e módulo Configurações; empresa do JWT (vazia dá 400); passo fora
/// do catálogo dá 400; adiar e concluir de quem não é dona nem suporte dá 403 HIERARQUIA.
/// </summary>
public class ConfiguracaoInicialControllerTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly IConfiguracaoInicialRepository _passos = Substitute.For<IConfiguracaoInicialRepository>();
    private readonly IRetratoDaConfiguracaoInicialQuery _retratos = Substitute.For<IRetratoDaConfiguracaoInicialQuery>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Empresa _empresa = Empresa.Criar("Casa da Baba", "11111111000191");

    public ConfiguracaoInicialControllerTests()
    {
        _empresa.Id = EmpresaId;
        _empresas.GetByIdAsync(EmpresaId).Returns(_empresa);
        _passos.ListarPassosAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(new List<PassoMarcado>());
        _retratos.ObterAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(new RetratoDaConfiguracaoInicial(
            false, 0, false, false, 0, 0, 0, false, new Dictionary<NivelAcesso, int>()));
    }

    private ConfiguracaoInicialController Controller(Guid empresaId, bool dona = false)
    {
        var usuario = Usuario.Criar("Administração de teste", $"adm-{Guid.NewGuid():N}@exemplo.test", "hash");
        usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = EmpresaId, Ativo = true, EhDona = dona });
        usuario.Perfis.Add(new UsuarioPerfil
        {
            UsuarioId = usuario.Id, EmpresaId = EmpresaId,
            Perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Administração", Nivel = NivelAcesso.Admin }
        });
        _usuarios.GetByIdAsync(usuario.Id).Returns(usuario);

        var sessao = Substitute.For<ICurrentUserAccessor>();
        sessao.UsuarioId.Returns(usuario.Id);
        sessao.EmpresaId.Returns(empresaId);
        sessao.Nivel.Returns(NivelAcesso.Admin);
        sessao.TemPermissao(Arg.Any<Permissao>()).Returns(true);

        var autoridade = new AutoridadeNaConfiguracaoInicial(_usuarios);
        var relogio = TimeProvider.System;
        return new ConfiguracaoInicialController(
            new ObterConfiguracaoInicialUseCase(_empresas, _passos, _retratos, autoridade, sessao),
            new MarcarPassoConfiguracaoInicialUseCase(_passos, sessao, relogio),
            new AdiarConfiguracaoInicialUseCase(_empresas, autoridade, sessao, _unitOfWork, relogio),
            new ConcluirConfiguracaoInicialUseCase(_empresas, autoridade, sessao, _unitOfWork, relogio),
            sessao);
    }

    [Fact]
    public void RotasSaoDoAdminNoModuloConfiguracoes()
    {
        typeof(ConfiguracaoInicialController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).Should().ContainSingle().Which.Should().Be("Admin");
        foreach (var acao in new[] { "Obter", "Marcar", "Adiar", "Concluir" })
            ModulosConvention.ModulosDe(typeof(ConfiguracaoInicialController), acao)!
                .Should().Equal([Modulo.Configuracoes], $"a ação {acao} fica no módulo Configurações");
    }

    [Fact]
    public async Task GetDevolveOEstadoNoEnvelope()
    {
        var resposta = await Controller(EmpresaId, dona: true).Obter(CancellationToken.None);

        var dados = resposta.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<ApiResponse<ConfiguracaoInicialResult>>().Which.Data;
        (dados.Pendente, dados.AbrirSozinho, dados.EhDona, dados.PodeConcluir).Should().Be((true, true, true, true));
        dados.Passos.Should().HaveCount(ConfiguracaoInicial.Passos.Count);
    }

    [Fact]
    public async Task EmpresaVaziaNoTokenDa400EmTodasAsRotas()
    {
        var controller = Controller(Guid.Empty, dona: true);

        foreach (var resposta in new[]
                 {
                     await controller.Obter(CancellationToken.None),
                     await controller.Marcar("frete", new MarcarPassoConfiguracaoInicialBody("conferido"), CancellationToken.None),
                     await controller.Adiar(CancellationToken.None),
                     await controller.Concluir(CancellationToken.None),
                 })
            resposta.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<ApiErrorResponse>()
                .Which.Error.Message.Should().Be("EmpresaId é obrigatório.");
        await _passos.DidNotReceiveWithAnyArgs().MarcarAsync(default!, default);
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task MarcarPassoConhecidoDa204()
    {
        var resposta = await Controller(EmpresaId).Marcar("frete", new MarcarPassoConfiguracaoInicialBody("depois"), CancellationToken.None);

        resposta.Should().BeOfType<NoContentResult>();
        await _passos.Received(1).MarcarAsync(Arg.Is<PassoConfiguracaoInicial>(p => p.Passo == "frete"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("caixa", "conferido", MarcarPassoConfiguracaoInicialUseCase.CodigoPassoDesconhecido)]
    [InlineData("frete", "pulado", MarcarPassoConfiguracaoInicialUseCase.CodigoSituacaoInvalida)]
    public async Task MarcarForaDoContratoDa400ComOCodigo(string passo, string situacao, string codigo)
    {
        var resposta = await Controller(EmpresaId).Marcar(passo, new MarcarPassoConfiguracaoInicialBody(situacao), CancellationToken.None);

        resposta.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<ApiErrorResponse>()
            .Which.Error.Code.Should().Be(codigo);
    }

    [Fact]
    public async Task AdministracaoRecebe403HierarquiaNoAdiarENoConcluir()
    {
        var controller = Controller(EmpresaId, dona: false);

        foreach (var resposta in new[] { await controller.Adiar(CancellationToken.None), await controller.Concluir(CancellationToken.None) })
        {
            var recusa = resposta.Should().BeOfType<ObjectResult>().Subject;
            recusa.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            recusa.Value.Should().BeOfType<ApiErrorResponse>().Which.Error.Code.Should().Be(AutoridadeNaConfiguracaoInicial.CodigoHierarquia);
        }
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DonaAdiaEConcluiCom204()
    {
        var controller = Controller(EmpresaId, dona: true);

        (await controller.Adiar(CancellationToken.None)).Should().BeOfType<NoContentResult>();
        (await controller.Concluir(CancellationToken.None)).Should().BeOfType<NoContentResult>();
        _empresa.ConfiguracaoInicialConcluidaEm.Should().NotBeNull();
        await _unitOfWork.Received(2).CommitAsync();
    }
}
