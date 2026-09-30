using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// S48: link do cardápio ligado à conversa. Vale 24 h, serve para um pedido só e a URL não carrega
/// dado do cliente nem id interno.
/// </summary>
public class LinkCardapioConversaServiceTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid EmpresaId = Guid.NewGuid();

    /// <summary>Repositório em memória com a mesma semântica atômica do Postgres.</summary>
    internal sealed class RepositorioEmMemoria : ILinkCardapioConversaRepository
    {
        public List<LinkCardapioConversa> Links { get; } = new();

        public Task AddAsync(LinkCardapioConversa link, CancellationToken ct = default)
        {
            Links.Add(link);
            return Task.CompletedTask;
        }

        public Task<LinkCardapioConversa?> ObterPorTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(Links.FirstOrDefault(l => l.TokenHash == tokenHash));

        public Task<bool> TentarConsumirAsync(Guid empresaId, Guid linkId, DateTime agora, CancellationToken ct = default)
        {
            var link = Links.FirstOrDefault(l => l.EmpresaId == empresaId && l.Id == linkId);
            if (link is null || !link.EstaValido(agora)) return Task.FromResult(false);
            link.Consumir(agora);
            return Task.FromResult(true);
        }

        public Task LiberarAsync(Guid empresaId, Guid linkId, CancellationToken ct = default)
        {
            Links.FirstOrDefault(l => l.EmpresaId == empresaId && l.Id == linkId)?.Liberar();
            return Task.CompletedTask;
        }
    }

    private readonly RepositorioEmMemoria _repo = new();
    private readonly LinkCardapioConversaService _servico;

    public LinkCardapioConversaServiceTests()
    {
        _servico = new LinkCardapioConversaService(_repo,
            new SaudacaoAtendimento(Substitute.For<IStorefrontRepository>(), new ConfigurationBuilder().Build()));
    }

    [Fact]
    public async Task TokenVencidoRecusa()
    {
        var gerado = await _servico.GerarAsync(EmpresaId, Guid.NewGuid(), Agora);

        (await _servico.ValidarAsync(gerado.Token, Agora.AddHours(23))).Should().NotBeNull();

        var vencido = () => _servico.ValidarAsync(gerado.Token, Agora.Add(LinkCardapioConversa.Validade));
        await vencido.Should().ThrowAsync<LinkCardapioConversaIndisponivelException>();
    }

    [Fact]
    public async Task UsoUnico()
    {
        var gerado = await _servico.GerarAsync(EmpresaId, Guid.NewGuid(), Agora);
        var link = await _servico.ValidarAsync(gerado.Token, Agora);

        await _servico.ConsumirAsync(link, Agora.AddMinutes(5));

        var denovo = () => _servico.ValidarAsync(gerado.Token, Agora.AddMinutes(6));
        await denovo.Should().ThrowAsync<LinkCardapioConversaIndisponivelException>();
        var concorrente = () => _servico.ConsumirAsync(link, Agora.AddMinutes(6));
        await concorrente.Should().ThrowAsync<LinkCardapioConversaIndisponivelException>();
    }

    [Fact]
    public async Task PedidoRecusado_LiberaParaReenviar()
    {
        var gerado = await _servico.GerarAsync(EmpresaId, Guid.NewGuid(), Agora);
        var link = await _servico.ValidarAsync(gerado.Token, Agora);
        await _servico.ConsumirAsync(link, Agora);

        await _servico.LiberarAsync(link);

        (await _servico.ValidarAsync(gerado.Token, Agora.AddMinutes(1))).Id.Should().Be(link.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("token-inventado")]
    public async Task TokenInexistenteRecusa(string? token)
    {
        var act = () => _servico.ValidarAsync(token, Agora);
        await act.Should().ThrowAsync<LinkCardapioConversaIndisponivelException>();
    }

    [Fact]
    public async Task UrlSemDadosDoClienteNemIdInterno()
    {
        var conversa = Conversa.Abrir(EmpresaId, "5511999998888", Agora, "Maria Souza", Guid.NewGuid());

        var gerado = await _servico.GerarAsync(EmpresaId, conversa.Id, Agora);

        gerado.Url.Should().StartWith($"{SaudacaoAtendimento.BaseUrlPadrao}{SaudacaoAtendimento.CaminhoCardapio}?c=");
        gerado.Url.Should().EndWith(gerado.Token);
        gerado.ExpiraEm.Should().Be(Agora.Add(LinkCardapioConversa.Validade));
        foreach (var proibido in new[]
                 {
                     "5511999998888", "999998888", "Maria", conversa.Id.ToString(), conversa.Id.ToString("N"),
                     EmpresaId.ToString(), EmpresaId.ToString("N"), conversa.ClienteId!.Value.ToString("N"),
                 })
            gerado.Url.Should().NotContainEquivalentOf(proibido);

        var gravado = _repo.Links.Should().ContainSingle().Subject;
        gravado.ConversaId.Should().Be(conversa.Id);
        gravado.TokenHash.Should().NotBe(gerado.Token).And.HaveLength(LinkCardapioConversa.TokenHashTamanho);
        gerado.Url.Should().NotContain(gravado.Id.ToString("N"));
    }
}
