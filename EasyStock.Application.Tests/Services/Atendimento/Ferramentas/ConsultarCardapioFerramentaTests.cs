using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using Microsoft.Extensions.Logging;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>#1314: a ficha (alérgenos e ingredientes) chega ao agente para responder pergunta de alergia.</summary>
public class ConsultarCardapioFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Cenario
    {
        private readonly List<CardapioItem> _itens = [];
        private readonly StorefrontEntity _storefront = StorefrontEntity.Criar(Guid.NewGuid(), "casa-da-baba", "Casa da Babá", 0m);
        private readonly ConsultarCardapioFerramenta _ferramenta;
        private readonly Conversa _conversa;

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

            _ferramenta = new ConsultarCardapioFerramenta(storefrontRepo, listar, cardapioRepo,
                Substitute.For<IConfiguracaoAtendimentoRepository>());
            _conversa = Conversa.Abrir(_storefront.EmpresaId, "5511999998888", Agora, "Maria", Guid.NewGuid());
        }

        public void Item(string nome, string? alergenos = null, string? ingredientes = null)
        {
            var item = CardapioItem.CriarAvulso(_storefront.Id, nome, 30m);
            item.AtualizarMetadata(alergenos: alergenos, ingredientes: ingredientes);
            _itens.Add(item);
        }

        public async Task<JsonElement> ItemDoResultadoAsync(string nome)
        {
            var resultado = await _ferramenta.ExecutarAsync(
                new ContextoTurnoAgente(_storefront.EmpresaId, _conversa, Agora), JsonSerializer.SerializeToElement(new { }));
            return JsonDocument.Parse(resultado).RootElement.GetProperty("itens").EnumerateArray()
                .Single(i => i.GetProperty("nome").GetString() == nome).Clone();
        }
    }

    [Fact]
    public async Task ItemComFicha_LevaAlergenosEIngredientes()
    {
        var c = new Cenario();
        c.Item("molho pesto", alergenos: "castanhas, lactose", ingredientes: "manjericão, castanha-de-caju, parmesão");

        var item = await c.ItemDoResultadoAsync("Molho Pesto");

        item.GetProperty("alergenos").GetString().Should().Be("castanhas, lactose");
        item.GetProperty("ingredientes").GetString().Should().Be("manjericão, castanha-de-caju, parmesão");
    }

    [Fact]
    public async Task ItemSemFicha_OmiteOsCampos()
    {
        var c = new Cenario();
        c.Item("lasanha");
        c.Item("nhoque", alergenos: "  ", ingredientes: "");

        foreach (var nome in new[] { "Lasanha", "Nhoque" })
        {
            var item = await c.ItemDoResultadoAsync(nome);
            item.TryGetProperty("alergenos", out _).Should().BeFalse(nome);
            item.TryGetProperty("ingredientes", out _).Should().BeFalse(nome);
        }
    }
}
