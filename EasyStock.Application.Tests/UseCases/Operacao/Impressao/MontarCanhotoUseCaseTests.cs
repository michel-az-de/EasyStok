using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Operacao.Impressao;

/// <summary>
/// S20 (#1156): o canhoto agrupa por linha e traz porção, molho e observação por item (US-035, US-036,
/// RN-27 a RN-29). Item sem linha e sem catálogo (frete) não entra: não é produção.
/// </summary>
public class MontarCanhotoUseCaseTests
{
    internal sealed class Cenario
    {
        public Guid EmpresaId { get; } = Guid.NewGuid();
        public Pedido Pedido { get; }
        public Cliente Cliente { get; }
        public StorefrontEntity Storefront { get; }
        public List<CardapioItem> Cardapio { get; } = new();
        public IPedidoRepository Pedidos { get; } = Substitute.For<IPedidoRepository>();
        public IClienteRepository Clientes { get; } = Substitute.For<IClienteRepository>();
        public IStorefrontRepository Storefronts { get; } = Substitute.For<IStorefrontRepository>();
        public ICardapioItemRepository CardapioRepo { get; } = Substitute.For<ICardapioItemRepository>();

        public Cenario()
        {
            Storefront = StorefrontEntity.Criar(EmpresaId, "casa-da-baba", "Casa da Babá", 0m);
            Cliente = new Cliente
            {
                Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Ana",
                Endereco = "Rua das Flores, 10", Complemento = "ap 32", Bairro = "Centro", Cidade = "São Paulo", Cep = "01001000",
            };
            Pedido = Pedido.Criar(EmpresaId, Cliente, origem: "whatsapp");
            Pedido.ClienteTelefone = "+5511999990000";
            Pedido.Observacoes = "Portão azul";
            Pedido.AgendadoParaEm = new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);
            Pedido.Pagamentos.Add(new PedidoPagamento
            {
                Id = Guid.NewGuid(), PedidoId = Pedido.Id, Metodo = "pix", Valor = 50m,
                PagoEm = new DateTime(2026, 9, 30, 12, 5, 0, DateTimeKind.Utc),
            });

            Pedidos.GetByIdWithDetailsAsync(EmpresaId, Pedido.Id).Returns(Pedido);
            Clientes.GetByIdAsync(EmpresaId, Cliente.Id).Returns(Cliente);
            Storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(Storefront);
            CardapioRepo.GetTodosDoStorefrontAsync(Storefront.Id, Arg.Any<CancellationToken>()).Returns(_ => Cardapio);
        }

        public CardapioItem ItemCardapio(string nome, string? molho = null)
        {
            var c = CardapioItem.CriarAvulso(Storefront.Id, nome, 10m);
            if (molho is not null) c.AtualizarMetadata(sugestaoMolho: molho);
            Cardapio.Add(c);
            return c;
        }

        public void Item(string nome, decimal qtd, string? linha, Guid? cardapioItemId = null, string? porcao = null, string? obs = null) =>
            Pedido.Itens.Add(new PedidoItem
            {
                Id = Guid.NewGuid(), PedidoId = Pedido.Id, Nome = nome, Quantidade = qtd, PrecoUnitario = 10m,
                LinhaSnapshot = linha, CardapioItemId = cardapioItemId, VariacaoRotuloSnapshot = porcao,
                Observacao = obs, CriadoEm = new DateTime(2026, 9, 30, 11, 0, Pedido.Itens.Count, DateTimeKind.Utc),
            });

        public MontarCanhotoUseCase UseCase() => new(Pedidos, Clientes, Storefronts, CardapioRepo);
    }

    [Fact]
    public async Task AgrupaPorLinhaComObservacao()
    {
        var c = new Cenario();
        var coxinha = c.ItemCardapio("Coxinha", molho: "Molho rosé");
        var lasanha = c.ItemCardapio("Lasanha");
        c.Item("Coxinha", 2, "paraServir", coxinha.Id, porcao: "Cento", obs: "sem cebola");
        c.Item("Lasanha", 1, "prepararEmCasa", lasanha.Id, porcao: "800g");
        c.Item("Kibe", 1, "paraServir", obs: "bem assado");
        c.Item("Entrega — Centro", 1, linha: null);

        var canhoto = await c.UseCase().ExecuteAsync(new MontarCanhotoInput(c.EmpresaId, c.Pedido.Id));

        canhoto.Should().NotBeNull();
        canhoto!.Grupos.Select(g => g.Linha).Should().Equal("paraServir", "prepararEmCasa");
        var servir = canhoto.Grupos[0];
        servir.Titulo.Should().Be("Para servir");
        servir.Itens.Should().Equal(
            new CanhotoItemDto("Coxinha", "Cento", 2, "Molho rosé", "sem cebola"),
            new CanhotoItemDto("Kibe", null, 1, null, "bem assado"));
        canhoto.Grupos[1].Titulo.Should().Be("Preparar em casa");
        canhoto.Grupos[1].Itens.Should().Equal(new CanhotoItemDto("Lasanha", "800g", 1, null, null));
        canhoto.Grupos.SelectMany(g => g.Itens).Should().NotContain(i => i.Nome.StartsWith("Entrega"), "frete não é produção");

        canhoto.Observacoes.Should().Be("Portão azul");
        canhoto.Rodape.Should().Be("imprima este canhoto: ele basta para produzir");
        var cab = canhoto.Cabecalho;
        cab.NomeCasa.Should().Be("Casa da Babá");
        cab.Numero.Should().Be(c.Pedido.Id.ToString("N")[..8].ToUpperInvariant());
        cab.Cliente.Should().Be("Ana");
        cab.Telefone.Should().Be("+5511999990000");
        cab.Endereco.Should().Be("Rua das Flores, 10, ap 32 - Centro, São Paulo - CEP 01001-000");
        cab.AgendadoPara.Should().Be(new DateTime(2026, 9, 30, 12, 0, 0));
        cab.PagoEm.Should().Be(new DateTime(2026, 9, 30, 9, 5, 0));
    }

    [Fact]
    public async Task PedidoDeOutraEmpresaDevolveNull()
    {
        var c = new Cenario();

        var canhoto = await c.UseCase().ExecuteAsync(new MontarCanhotoInput(Guid.NewGuid(), c.Pedido.Id));

        canhoto.Should().BeNull();
    }
}
