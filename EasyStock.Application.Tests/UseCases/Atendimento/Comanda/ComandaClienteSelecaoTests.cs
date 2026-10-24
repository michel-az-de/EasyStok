using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Comanda;

/// <summary>
/// #2002 comanda-02: o cliente altera o pedido depois que um prato que ele já pediu esgotou. Só quem aumenta a quantidade
/// ou traz prato novo precisa estar à venda; o que o pedido já reservou segue como está. Comanda-07: teto dos campos do rascunho.
/// </summary>
public class ComandaClienteSelecaoTests
{
    private static ComandaClienteUseCase Sut(CheckoutCoreServiceTests.Cenario c)
    {
        var construtor = typeof(ComandaClienteUseCase).GetConstructors().Single();
        var argumentos = construtor.GetParameters().Select(p => p.ParameterType switch
        {
            var t when t == typeof(CheckoutCoreService) => (object?)c.Servico(),
            var t when t == typeof(IStorefrontRepository) => c.StorefrontRepo,
            var t when t == typeof(ICardapioItemRepository) => c.CardapioRepo,
            var t when t == typeof(TimeProvider) => c.Relogio,
            var t when t.IsInterface => Substitute.For([t], []),
            _ => null,
        }).ToArray();
        return (ComandaClienteUseCase)construtor.Invoke(argumentos);
    }

    private static CardapioItem Coxinha(CheckoutCoreServiceTests.Cenario c)
    {
        var coxinha = CardapioItem.CriarAvulso(c.Storefront.Id, "Coxinha", 6m);
        coxinha.TornarVisivel();
        c.CardapioRepo.GetByIdAsync(c.Storefront.Id, coxinha.Id, Arg.Any<CancellationToken>()).Returns(coxinha);
        return coxinha;
    }

    private static Pedido PedidoCom(CheckoutCoreServiceTests.Cenario c, Guid cardapioItemId, int quantidade, decimal preco, Guid? variacaoId = null)
    {
        var pedido = new Pedido { Id = Guid.NewGuid(), EmpresaId = c.Storefront.EmpresaId };
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, CardapioItemId = cardapioItemId, CardapioItemVariacaoId = variacaoId,
            Nome = "Lasanha", Quantidade = quantidade, PrecoUnitario = preco, Subtotal = quantidade * preco,
        });
        return pedido;
    }

    [Fact]
    public async Task PratoEsgotadoQueOPedidoJaTem_NaoTravaAAlteracaoQueAcrescentaOutroPrato()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var coxinha = Coxinha(c);
        var pedido = PedidoCom(c, c.CardapioItemId, 2, 10m);
        c.CardapioItem.MarcarEsgotado();

        var selecao = await Sut(c).SelecionarAsync(c.Storefront.EmpresaId,
            [new(c.CardapioItemId, 2), new(coxinha.Id, 1)], pedido, CancellationToken.None);

        selecao.Select(i => (i.CardapioItemId, i.Qtd, i.Preco)).Should().Equal(
            (c.CardapioItemId, 2, 10m), (coxinha.Id, 1, 6m));
    }

    [Fact]
    public async Task PratoEsgotado_AumentarAQuantidadeSegueRecusado()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var pedido = PedidoCom(c, c.CardapioItemId, 2, 10m);
        c.CardapioItem.MarcarEsgotado();

        var agir = () => Sut(c).SelecionarAsync(c.Storefront.EmpresaId, [new(c.CardapioItemId, 3)], pedido, CancellationToken.None);

        await agir.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*indisponível*");
    }

    [Fact]
    public async Task PratoEsgotado_ReduzirAQuantidadeEPermitido()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var pedido = PedidoCom(c, c.CardapioItemId, 2, 10m);
        c.CardapioItem.MarcarEsgotado();

        var selecao = await Sut(c).SelecionarAsync(c.Storefront.EmpresaId, [new(c.CardapioItemId, 1)], pedido, CancellationToken.None);

        selecao.Should().ContainSingle().Which.Should().Match<ItemSelecionadoComanda>(i => i.Qtd == 1 && i.Preco == 10m);
    }

    [Fact]
    public async Task PratoEsgotadoSemPedido_SegueRecusado()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        c.CardapioItem.MarcarEsgotado();

        var agir = () => Sut(c).SelecionarAsync(c.Storefront.EmpresaId, [new(c.CardapioItemId, 1)], null, CancellationToken.None);

        await agir.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*indisponível*");
    }

    [Fact]
    public async Task PorcaoEsgotadaQueOPedidoJaTem_MantemOPrecoGravado_EOutraPorcaoNovaSegueRecusada()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var p300 = CardapioItemVariacao.Criar(c.CardapioItem.Id, "300 g", 28m, ordemExibicao: 0, ehPadrao: true);
        var p800 = CardapioItemVariacao.Criar(c.CardapioItem.Id, "800 g", 62m, ordemExibicao: 1);
        c.CardapioItem.AdicionarVariacao(p300);
        c.CardapioItem.AdicionarVariacao(p800);
        p300.MarcarEsgotado();
        var pedido = PedidoCom(c, c.CardapioItemId, 1, 28m, p300.Id);

        var selecao = await Sut(c).SelecionarAsync(c.Storefront.EmpresaId,
            [new(c.CardapioItemId, 1, null, p300.Id), new(c.CardapioItemId, 1, null, p800.Id)], pedido, CancellationToken.None);

        selecao.Select(i => (i.VariacaoId, i.Preco)).Should().Equal((p300.Id, 28m), (p800.Id, 62m));

        p800.MarcarEsgotado();
        var nova = () => Sut(c).SelecionarAsync(c.Storefront.EmpresaId,
            [new(c.CardapioItemId, 1, null, p300.Id), new(c.CardapioItemId, 1, null, p800.Id)], pedido, CancellationToken.None);
        await nova.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    // ── comanda-07: o rascunho valida o que o envio valida, sem deixar o JSON crescer até o limite do servidor ──

    private static RascunhoComandaCliente Rascunho(string? nome = null, string? telefone = null, EnderecoCheckout? endereco = null) =>
        new([new(Guid.NewGuid(), 1)], Nome: nome, Telefone: telefone, Endereco: endereco);

    private static EnderecoCheckout Endereco(string cep = "01310100", string logradouro = "Rua A", string numero = "10",
        string bairro = "Centro", string cidade = "São Paulo", string uf = "SP", string? complemento = null) =>
        new(cep, logradouro, numero, bairro, cidade, uf, complemento);

    [Fact]
    public void Rascunho_NoTeto_Passa()
    {
        var agir = () => ComandaClienteUseCase.Validar(Rascunho(new string('n', 150), new string('9', 32),
            Endereco(logradouro: new string('r', 200), numero: new string('1', 20), bairro: new string('b', 100),
                cidade: new string('c', 100), complemento: new string('x', 100))));

        agir.Should().NotThrow();
    }

    [Fact]
    public void Rascunho_ComEnderecoParcial_Passa()
    {
        // #1839: endereço incompleto fica no rascunho e a atendente confere; só o tamanho é limitado.
        var agir = () => ComandaClienteUseCase.Validar(Rascunho(endereco: Endereco(cep: "", logradouro: "Rua A", numero: "", bairro: "", cidade: "", uf: "")));

        agir.Should().NotThrow();
    }

    [Theory]
    [InlineData("nome", 151)]
    [InlineData("telefone", 33)]
    [InlineData("cep", 21)]
    [InlineData("logradouro", 201)]
    [InlineData("numero", 21)]
    [InlineData("bairro", 101)]
    [InlineData("cidade", 101)]
    [InlineData("uf", 3)]
    [InlineData("complemento", 101)]
    public void Rascunho_AcimaDoTeto_Recusa(string campo, int tamanho)
    {
        var texto = new string('a', tamanho);
        var dados = campo switch
        {
            "nome" => Rascunho(nome: texto),
            "telefone" => Rascunho(telefone: texto),
            "cep" => Rascunho(endereco: Endereco(cep: texto)),
            "logradouro" => Rascunho(endereco: Endereco(logradouro: texto)),
            "numero" => Rascunho(endereco: Endereco(numero: texto)),
            "bairro" => Rascunho(endereco: Endereco(bairro: texto)),
            "cidade" => Rascunho(endereco: Endereco(cidade: texto)),
            "uf" => Rascunho(endereco: Endereco(uf: texto)),
            _ => Rascunho(endereco: Endereco(complemento: texto)),
        };

        var agir = () => ComandaClienteUseCase.Validar(dados);

        agir.Should().Throw<UseCaseValidationException>();
    }
}
