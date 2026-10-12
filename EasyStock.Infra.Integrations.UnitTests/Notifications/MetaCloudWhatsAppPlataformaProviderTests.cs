using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.WhatsApp.Plataforma;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.Notifications;

/// <summary>N6: o provider de plataforma só manda template, nunca toca a Conversa e nunca reenvia.</summary>
public class MetaCloudWhatsAppPlataformaProviderTests
{
    private readonly IClienteWhatsAppPlataforma _cliente = Substitute.For<IClienteWhatsAppPlataforma>();
    private readonly ITemplateMetaEstadoRepository _estados = Substitute.For<ITemplateMetaEstadoRepository>();
    private readonly IRemetenteWhatsApp _remetente = Substitute.For<IRemetenteWhatsApp>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();

    private MetaCloudWhatsAppPlataformaProvider Provider() =>
        new(_cliente, _estados, NullLogger<MetaCloudWhatsAppPlataformaProvider>.Instance);

    private static MensagemPronta Mensagem(
        CategoriaConteudoNotificacao categoria = CategoriaConteudoNotificacao.Seguranca,
        Dictionary<string, string>? metadados = null) => new(
            Guid.NewGuid(), Guid.NewGuid(), "+5511999990001", "", "corpo", CanalNotificacao.WhatsApp, categoria,
            MensagemPronta.ProviderOverridePlataforma)
        {
            Metadados = metadados ?? new()
            {
                ["template"] = "codigo_redefinir_senha", ["idioma"] = "pt_BR", ["param1"] = "482913", ["botaoUrl0"] = "482913"
            }
        };

    [Fact]
    public async Task SemTemplateFalhaPermanente()
    {
        var r = await Provider().EnviarAsync(Mensagem(metadados: new()));

        r.FalhaPermanente.Should().BeTrue();
        r.ErroDetalhado.Should().Be(MetaCloudWhatsAppPlataformaProvider.ErroTemplateObrigatorio);
        await _cliente.DidNotReceiveWithAnyArgs().EnviarTemplatePlataformaAsync(default!, default);
    }

    [Fact]
    public async Task MarketingNaoSaiPelaPlataforma()
    {
        var r = await Provider().EnviarAsync(Mensagem(CategoriaConteudoNotificacao.Marketing));

        r.FalhaPermanente.Should().BeTrue();
        r.ErroDetalhado.Should().Be(MetaCloudWhatsAppPlataformaProvider.ErroMarketing);
        await _cliente.DidNotReceiveWithAnyArgs().EnviarTemplatePlataformaAsync(default!, default);
    }

    [Fact]
    public async Task TemplateRecategorizadoNaoChamaAMeta()
    {
        _estados.ObterAsync("codigo_redefinir_senha", "pt_BR", Arg.Any<CancellationToken>())
            .Returns(TemplateMetaEstado.Criar("codigo_redefinir_senha", "pt-BR", "MARKETING"));

        var r = await Provider().EnviarAsync(Mensagem());

        r.FalhaPermanente.Should().BeTrue();
        r.ErroDetalhado.Should().Be(MetaCloudWhatsAppPlataformaProvider.ErroRecategorizado);
        await _cliente.DidNotReceiveWithAnyArgs().EnviarTemplatePlataformaAsync(default!, default);
    }

    [Fact]
    public async Task AceitoDevolveIdExternoEProviderMetaPlataforma()
    {
        EnvioTemplatePlataforma? enviado = null;
        _cliente.EnviarTemplatePlataformaAsync(Arg.Do<EnvioTemplatePlataforma>(e => enviado = e), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvioPlataforma(DesfechoEnvio.Enviado, "wamid.OK"));
        var mensagem = Mensagem();

        var r = await Provider().EnviarAsync(mensagem);

        r.Sucesso.Should().BeTrue();
        r.ProviderUsado.Should().Be("meta-plataforma");
        r.IdExterno.Should().Be("wamid.OK");
        enviado!.Nome.Should().Be("codigo_redefinir_senha");
        enviado.ParametrosCorpo.Should().Equal("482913");
        enviado.BotaoUrl0.Should().Be("482913");
        enviado.CopyCode.Should().BeTrue("código de verificação é categoria Seguranca");
        enviado.OpacoCallback.Should().Be($"{mensagem.EmpresaId:N}.{mensagem.OutboxId:N}");
    }

    [Theory]
    [InlineData(DesfechoEnvio.FalhaPermanente, true, DesfechoEnvio.FalhaPermanente)]
    [InlineData(DesfechoEnvio.FalhaTransitoria, false, DesfechoEnvio.FalhaTransitoria)]
    [InlineData(DesfechoEnvio.Indeterminado, false, DesfechoEnvio.Indeterminado)]
    public async Task DesfechoDoClienteViraDesfechoDoCanal(DesfechoEnvio doCliente, bool permanente, DesfechoEnvio esperado)
    {
        _cliente.EnviarTemplatePlataformaAsync(Arg.Any<EnvioTemplatePlataforma>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvioPlataforma(doCliente, CodigoMeta: 131042, Classe: ClasseErroMeta.PermanenteComAlerta, Erro: "falha"));

        var r = await Provider().EnviarAsync(Mensagem());

        r.Desfecho.Should().Be(esperado);
        r.FalhaPermanente.Should().Be(permanente);
        r.Sucesso.Should().BeFalse();
    }

    [Fact]
    public async Task NaoTocaConversaNemTenant()
    {
        _cliente.EnviarTemplatePlataformaAsync(Arg.Any<EnvioTemplatePlataforma>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvioPlataforma(DesfechoEnvio.Enviado, "wamid.OK"));

        await Provider().EnviarAsync(Mensagem());

        // O provider nem recebe essas dependências; os doubles provam que nada as alcança.
        await _remetente.DidNotReceiveWithAnyArgs().ObterPhoneNumberIdAsync(default);
        await _conversas.DidNotReceiveWithAnyArgs().ObterAbertaPorContatoAsync(default, default, default!, default);
        typeof(MetaCloudWhatsAppPlataformaProvider).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType)
            .Should().NotContain(t => t == typeof(IRemetenteWhatsApp) || t == typeof(IConversaRepository));
    }
}
