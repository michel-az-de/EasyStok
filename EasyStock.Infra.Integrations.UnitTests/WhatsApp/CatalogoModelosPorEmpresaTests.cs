using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

/// <summary>#3205 (S80): o catálogo decide por empresa com a regra do roteador (sessão web manda e lista os locais).</summary>
public class CatalogoModelosPorEmpresaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private readonly ICatalogoModelosAtendimento _meta = Substitute.For<ICatalogoModelosAtendimento>();
    private readonly IRemetenteWhatsApp _remetente = Substitute.For<IRemetenteWhatsApp>();
    private readonly IModeloMensagemLocalRepository _locais = Substitute.For<IModeloMensagemLocalRepository>();
    private CatalogoModelosPorEmpresa Catalogo => new(_meta, _remetente, _locais);

    public CatalogoModelosPorEmpresaTests()
    {
        _locais.ListarAsync(Empresa, false, Arg.Any<CancellationToken>()).Returns([
            ModeloMensagemLocal.Criar(Empresa, "retomar", "pt_BR", "UTILITY", "Casa", "Olá {{1}}, seu pedido {{2}} saiu.", "Até logo", DateTime.UtcNow)]);
        _meta.ListarAsync(Empresa, Arg.Any<CancellationToken>()).Returns([
            new ModeloAtendimento("aviso_meta", "pt_BR", "UTILITY", "Oi {{1}}", 1)]);
    }

    private void ComSessaoWeb(bool tem) =>
        _remetente.ObterSessaoWebAsync(Arg.Any<CancellationToken>())
            .Returns(tem ? new CredencialWppConnect("ez-c8ebd039563e4f34a606c5f7a788c156", "token") : null);

    [Fact]
    public async Task SoSessaoWeb_ListaOsLocaisNaoArquivados_ComOrigemLocal()
    {
        ComSessaoWeb(true);
        _meta.ListarAsync(Empresa, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ModeloAtendimento>>(_ => throw CatalogoModelosException.SemConexao("sem Meta"));

        var lista = await Catalogo.ListarAsync(Empresa);

        var modelo = lista.Should().ContainSingle().Subject;
        modelo.Should().BeEquivalentTo(new ModeloAtendimento("retomar", "pt_BR", "UTILITY", "Olá {{1}}, seu pedido {{2}} saiu.", 2,
            "Casa", "Até logo", null, OrigemModelo.Local));
        await _locais.Received(1).ListarAsync(Empresa, false, Arg.Any<CancellationToken>());
        (await Catalogo.UsaModelosLocaisAsync(Empresa)).Should().BeTrue();
    }

    [Fact]
    public async Task SoMeta_ListaSoOsAprovadosDaMeta()
    {
        ComSessaoWeb(false);

        var lista = await Catalogo.ListarAsync(Empresa);

        lista.Should().ContainSingle().Which.Should().Match<ModeloAtendimento>(m => m.Nome == "aviso_meta" && m.Origem == OrigemModelo.Meta);
        await _locais.DidNotReceiveWithAnyArgs().ListarAsync(default, default, default);
        (await Catalogo.UsaModelosLocaisAsync(Empresa)).Should().BeFalse();
    }

    [Fact]
    public async Task SemSessaoESemMeta_SegueRecusandoComAFraseDeConexao()
    {
        ComSessaoWeb(false);
        _meta.ListarAsync(Empresa, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ModeloAtendimento>>(_ => throw CatalogoModelosException.SemConexao("sem Meta"));

        var act = () => Catalogo.ListarAsync(Empresa);

        (await act.Should().ThrowAsync<CatalogoModelosException>()).Which.FaltaConexao.Should().BeTrue();
    }

    [Fact]
    public async Task AsDuas_SessaoWebMandaEListaOsLocais_SemConsultarAMeta()
    {
        ComSessaoWeb(true);

        var lista = await Catalogo.ListarAsync(Empresa);

        lista.Should().ContainSingle().Which.Origem.Should().Be(OrigemModelo.Local);
        await _meta.DidNotReceiveWithAnyArgs().ListarAsync(default, default);
    }
}
