using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Storefront.Auth;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Auth;

public sealed class VincularComandaAposOtpUseCaseTests
{
    private const string Token = "token-comanda-verificacao";
    private const string Telefone = "+5511997573992";
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly IStorefrontRepository _lojas = Substitute.For<IStorefrontRepository>();
    private readonly IClienteSessionRepository _sessoes = Substitute.For<IClienteSessionRepository>();
    private readonly ILinkCardapioConversaRepository _links = Substitute.For<ILinkCardapioConversaRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly FakeUnitOfWork _uow = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Posse_verificada_vincula_sem_token_chat_e_preserva_rascunho(bool jaVinculada)
    {
        var (sessao, conversa, link) = Preparar();
        if (jaVinculada) conversa.VincularCliente(sessao.ClienteId);
        var json = link.RascunhoJson;
        var revisao = link.Revisao;
        await UseCase().ExecuteAsync("casa-da-baba", sessao.Id, Token);
        conversa.ClienteId.Should().Be(sessao.ClienteId);
        link.RascunhoJson.Should().Be(json);
        link.Revisao.Should().Be(revisao);
        link.UsadoEm.Should().BeNull();
        _uow.CommitCount.Should().Be(1);
        await _clientes.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Theory]
    [InlineData("sessao-outro-tenant")]
    [InlineData("link-outro-tenant")]
    [InlineData("cliente-outro-tenant")]
    [InlineData("conversa-outro-tenant")]
    [InlineData("terceiro")]
    [InlineData("numero-divergente")]
    [InlineData("numero-invalido")]
    [InlineData("vencido")]
    [InlineData("consumido")]
    [InlineData("pedido-registrado")]
    [InlineData("conversa-encerrada")]
    [InlineData("outro-canal")]
    [InlineData("sessao-revogada")]
    [InlineData("token-invalido")]
    public async Task Nega_vinculo_sem_modificar_cadastro_ou_conversa(string cenario)
    {
        var (sessao, conversa, link) = Preparar();
        if (cenario == "sessao-outro-tenant")
            _sessoes.GetByIdAsync(sessao.Id, Arg.Any<CancellationToken>()).Returns(ClienteSession.Criar(sessao.ClienteId, Guid.NewGuid(), TimeProvider.System));
        if (cenario == "link-outro-tenant")
            _links.ObterPorTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(LinkCardapioConversa.Gerar(Guid.NewGuid(), conversa.Id, link.TokenHash, DateTime.UtcNow));
        if (cenario == "cliente-outro-tenant")
            _clientes.GetByIdWithDetailsAsync(_empresa, sessao.ClienteId).Returns(Cliente.Criar(Guid.NewGuid(), "Terceiro"));
        if (cenario == "conversa-outro-tenant")
            _conversas.ObterPorIdComLockAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(Conversa.Abrir(Guid.NewGuid(), "outro", DateTime.UtcNow, canal: CanalConversa.ChatSite));
        if (cenario == "terceiro") conversa.VincularCliente(Guid.NewGuid());
        if (cenario.StartsWith("numero-", StringComparison.Ordinal))
            link.SalvarRascunho(link.Revisao, JsonSerializer.Serialize(new RascunhoComandaCliente([], Telefone: cenario == "numero-divergente" ? "+5511982254398" : "x"), ComandaClienteUseCase.Json), DateTime.UtcNow);
        if (cenario == "vencido") link.Expirar(DateTime.UtcNow.AddSeconds(-1));
        if (cenario == "consumido") link.Consumir(DateTime.UtcNow);
        if (cenario == "pedido-registrado") link.RegistrarResultado(Guid.NewGuid(), "{}");
        if (cenario == "conversa-encerrada") conversa.Encerrar(DateTime.UtcNow);
        if (cenario == "outro-canal")
            _conversas.ObterPorIdComLockAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(Conversa.Abrir(_empresa, "5511997573992", DateTime.UtcNow));
        if (cenario == "sessao-revogada") sessao.Revogar("teste");
        var donoAnterior = conversa.ClienteId;
        await UseCase().Invoking(s => s.ExecuteAsync("casa-da-baba", sessao.Id, cenario == "token-invalido" ? "" : Token))
            .Should().ThrowAsync<VinculoComandaInvalidoException>();
        conversa.ClienteId.Should().Be(donoAnterior);
        _uow.CommitCount.Should().Be(0);
        await _clientes.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    private VincularComandaAposOtpUseCase UseCase() => new(_lojas, _sessoes, _links, _conversas, _clientes, _uow, TimeProvider.System);

    private (ClienteSession Sessao, Conversa Conversa, LinkCardapioConversa Link) Preparar()
    {
        var loja = StorefrontEntity.Criar(_empresa, "casa-da-baba", "Casa", 0m);
        loja.Ativar();
        var cliente = Cliente.Criar(_empresa, "Maria");
        cliente.TelefoneHash = ClienteOtp.CalcularTelefoneHash(Telefone);
        var sessao = ClienteSession.Criar(cliente.Id, _empresa, TimeProvider.System);
        var conversa = Conversa.Abrir(_empresa, "visitante", DateTime.UtcNow, canal: CanalConversa.ChatSite);
        var link = LinkCardapioConversa.Gerar(_empresa, conversa.Id, AcessoChatSite.HashDoToken(Token), DateTime.UtcNow);
        link.SalvarRascunho(0, JsonSerializer.Serialize(new RascunhoComandaCliente([], Nome: "Maria", Telefone: "(11) 99757-3992"), ComandaClienteUseCase.Json), DateTime.UtcNow);
        _lojas.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>()).Returns(loja);
        _sessoes.GetByIdAsync(sessao.Id, Arg.Any<CancellationToken>()).Returns(sessao);
        _links.ObterPorTokenHashAsync(link.TokenHash, Arg.Any<CancellationToken>()).Returns(link);
        _links.ObterComLockAsync(_empresa, link.Id, Arg.Any<CancellationToken>()).Returns(link);
        _conversas.ObterPorIdComLockAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        _clientes.GetByIdWithDetailsAsync(_empresa, cliente.Id).Returns(cliente);
        return (sessao, conversa, link);
    }
}
