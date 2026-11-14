using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>#2544 (S72): o Assistente mostra o produto. Pergunta se o cliente quer ver e, com o sim, manda a foto do cardápio.</summary>
public class EnviarFotoProdutoFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Cenario
    {
        private readonly List<CardapioItem> _itens = [];
        private readonly StorefrontEntity _storefront = StorefrontEntity.Criar(Guid.NewGuid(), "loja", "Loja", 0m);
        private readonly EnviarFotoProdutoFerramenta _ferramenta;

        public IWhatsAppCloudClient Cloud { get; } = Substitute.For<IWhatsAppCloudClient>();
        public List<Mensagem> Mensagens { get; } = [];
        public Conversa Conversa { get; }

        public Cenario()
        {
            _storefront.Ativar();
            var storefrontRepo = Substitute.For<IStorefrontRepository>();
            storefrontRepo.GetByEmpresaAsync(_storefront.EmpresaId, Arg.Any<CancellationToken>()).Returns(_storefront);
            storefrontRepo.GetBySlugAsync(_storefront.Slug, Arg.Any<CancellationToken>()).Returns(_storefront);
            var cardapioRepo = Substitute.For<ICardapioItemRepository>();
            cardapioRepo.GetVisiveisDoStorefrontAsync(_storefront.Id, Arg.Any<CancellationToken>())
                .Returns(_ => (IReadOnlyList<CardapioItem>)_itens.ToList());
            var listar = new ListarCardapioPublicoUseCase(storefrontRepo, cardapioRepo,
                Substitute.For<IItemEstoqueRepository>(), Substitute.For<ILogger<ListarCardapioPublicoUseCase>>());
            var conversas = Substitute.For<IConversaRepository>();
            conversas.AddMensagemAsync(Arg.Do<Mensagem>(Mensagens.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Cloud.EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new EnvioWhatsAppResult("wamid.foto"));
            var configuracao = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://loja.exemplo/" }).Build();

            _ferramenta = new EnviarFotoProdutoFerramenta(storefrontRepo, listar, Cloud, conversas, configuracao);
            Conversa = Conversa.Abrir(_storefront.EmpresaId, "5511999998888", Agora, "Maria", Guid.NewGuid());
        }

        public CardapioItem Item(string nome, string? foto)
        {
            var item = CardapioItem.CriarAvulso(_storefront.Id, nome, 30m);
            item.TornarVisivel();
            if (foto is not null) item.AdicionarFotoGaleria(foto);
            _itens.Add(item);
            return item;
        }

        public ContextoTurnoAgente NovoContexto() => new(_storefront.EmpresaId, Conversa, Agora);

        public async Task<JsonElement> ExecutarAsync(Guid itemId, ContextoTurnoAgente? contexto = null)
        {
            var resultado = await _ferramenta.ExecutarAsync(contexto ?? NovoContexto(),
                JsonSerializer.SerializeToElement(new { itemId }));
            return JsonDocument.Parse(resultado).RootElement.Clone();
        }
    }

    [Fact]
    public async Task ItemComFoto_MandaAFotoComNomeEPreco_EGravaAMensagem()
    {
        var c = new Cenario();
        var item = c.Item("lasanha bolonhesa", "https://cdn.exemplo/files/cardapios/lasanha.jpg");
        var contexto = c.NovoContexto();

        var resultado = await c.ExecutarAsync(item.Id, contexto);

        resultado.GetProperty("enviado").GetBoolean().Should().BeTrue();
        await c.Cloud.Received(1).EnviarImagemAsync("5511999998888", "https://cdn.exemplo/files/cardapios/lasanha.jpg",
            Arg.Is<string?>(l => l!.Contains("Lasanha Bolonhesa") && l.Contains("R$ 30,00")), Arg.Any<CancellationToken>());
        c.Mensagens.Should().ContainSingle(m => m.Direcao == DirecaoMensagem.Saida && m.Autor == AutorMensagem.Agente
            && m.TipoConteudo == TipoConteudoMensagem.Imagem && m.ExternoId == "wamid.foto");
        // A foto não é a resposta: o texto do agente sai depois dela.
        contexto.HouveEnvio.Should().BeTrue();
        contexto.EnvioVerificado.Should().BeFalse();
    }

    [Fact]
    public async Task ItemSemFoto_NaoManda()
    {
        var c = new Cenario();
        var item = c.Item("nhoque", foto: null);

        var resultado = await c.ExecutarAsync(item.Id);

        resultado.GetProperty("enviado").GetBoolean().Should().BeFalse();
        resultado.GetProperty("motivo").GetString().Should().Be("sem_foto");
        await c.Cloud.DidNotReceiveWithAnyArgs().EnviarImagemAsync(default!, default!, default, default);
        c.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task ItemQueNaoEhDaLoja_NaoEncontrado()
    {
        var c = new Cenario();
        c.Item("lasanha", "https://cdn.exemplo/files/cardapios/lasanha.jpg");

        var resultado = await c.ExecutarAsync(Guid.NewGuid());

        resultado.GetProperty("motivo").GetString().Should().Be("item_nao_encontrado");
        await c.Cloud.DidNotReceiveWithAnyArgs().EnviarImagemAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task FotoComCaminhoRelativo_SaiComOEnderecoPublicoEmHttps()
    {
        var c = new Cenario();
        var item = c.Item("ravioli", "/files/cardapios/ravioli.jpg");

        await c.ExecutarAsync(item.Id);

        await c.Cloud.Received(1).EnviarImagemAsync(Arg.Any<string>(), "https://loja.exemplo/files/cardapios/ravioli.jpg",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuartaFotoNoTurno_NaoSai()
    {
        var c = new Cenario();
        var contexto = c.NovoContexto();
        var itens = Enumerable.Range(1, 4).Select(i => c.Item($"item {i}", $"https://cdn.exemplo/files/cardapios/{i}.jpg")).ToList();

        foreach (var item in itens.Take(3)) (await c.ExecutarAsync(item.Id, contexto)).GetProperty("enviado").GetBoolean().Should().BeTrue();
        var quarta = await c.ExecutarAsync(itens[3].Id, contexto);

        quarta.GetProperty("motivo").GetString().Should().Be("limite_do_turno");
        await c.Cloud.ReceivedWithAnyArgs(3).EnviarImagemAsync(default!, default!, default, default);
    }
}
