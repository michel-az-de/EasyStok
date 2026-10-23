using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions.Storefront;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// S11: a cobrança do pedido é uma preferência do Mercado Pago com <c>external_reference</c> do pedido,
/// itens e frete, que expira em 30 minutos. Pedir de novo, com a pendente ainda válida, devolve a mesma.
/// </summary>
public class GerarCobrancaPedidoUseCaseTests
{
    [Fact]
    public async Task EncomendaFutura_CobraSinalArredondadoEReutilizaLink()
    {
        var f = new CobrancaPedidoFixture();
        f.Pedido.CriadoEm = CobrancaPedidoFixture.Agora;
        f.Pedido.AgendadoParaEm = CobrancaPedidoFixture.Agora.AddDays(2);
        f.Pedido.Total = CobrancaPedidoFixture.Reais(25.01m);

        var uc = f.Gerar();
        var input = new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id);
        var primeira = await uc.ExecuteAsync(input);
        var segunda = await uc.ExecuteAsync(input);

        primeira.Valor.Should().Be(12.51m);
        segunda.CobrancaId.Should().Be(primeira.CobrancaId);
        segunda.Reutilizada.Should().BeTrue();
        var preferencia = f.Preferencias.Should().ContainSingle().Subject;
        preferencia.ValorTotal.Should().Be(12.51m);
        preferencia.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new PreferenceItemCommand("Sinal da encomenda (50%)", 1, 12.51m));

        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);
        var reemitida = await uc.ExecuteAsync(input);
        reemitida.Valor.Should().Be(12.51m);
        reemitida.CobrancaId.Should().NotBe(primeira.CobrancaId);
        f.Preferencias.Should().HaveCount(2);
    }

    [Fact]
    public async Task PreferenciaComExternalReferenceEExpiracao()
    {
        var f = new CobrancaPedidoFixture();
        var conversaId = Guid.NewGuid();

        var r = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id, conversaId));

        var pref = f.Preferencias.Should().ContainSingle().Subject;
        pref.PedidoId.Should().Be(f.Pedido.Id, "external_reference = PedidoId");
        pref.Items.Select(i => i.Titulo).Should().Equal("Brigadeiro", "Frete SP Centro");
        pref.ValorTotal.Should().Be(25m);
        pref.ExpiraEm.Should().Be(CobrancaPedidoFixture.Agora.AddMinutes(30));
        pref.IdempotencyKey.Should().NotBeNullOrWhiteSpace();

        var cobranca = f.Cobrancas.Should().ContainSingle().Subject;
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorMercadoPago);
        cobranca.ReferenciaExterna.Should().Be("pref-1");
        cobranca.LinkPagamento.Should().Be("https://mp.test/pref-1");
        cobranca.Valor.Should().Be(25m);
        cobranca.ExpiraEm.Should().Be(CobrancaPedidoFixture.Agora.AddMinutes(30));
        cobranca.Tentativa.Should().Be(1);
        cobranca.ConversaId.Should().Be(conversaId);

        r.LinkPagamento.Should().Be("https://mp.test/pref-1");
        r.Reutilizada.Should().BeFalse();
        await f.Uow.Received().CommitAsync();
    }

    [Fact]
    public async Task IdempotentePorTentativa()
    {
        var f = new CobrancaPedidoFixture();
        var uc = f.Gerar();
        var input = new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id);

        var primeira = await uc.ExecuteAsync(input);
        var segunda = await uc.ExecuteAsync(input);

        segunda.CobrancaId.Should().Be(primeira.CobrancaId);
        segunda.LinkPagamento.Should().Be(primeira.LinkPagamento);
        segunda.Reutilizada.Should().BeTrue();
        f.Preferencias.Should().ContainSingle("a pendente ainda válida é devolvida sem nova preferência");
        f.Cobrancas.Should().ContainSingle();
    }

    [Fact]
    public async Task PendenteVencida_ExpiraEGeraOutraComChaveNova()
    {
        var f = new CobrancaPedidoFixture();
        var vencida = f.AdicionarOnline();
        f.Relogio.Agora = CobrancaPedidoFixture.Agora.AddMinutes(31);

        await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        vencida.Status.Should().Be(StatusCobrancaPedido.Expirada);
        f.Cobrancas.Should().HaveCount(2);
        f.Preferencias.Single().IdempotencyKey.Should().EndWith("-2", "a chave muda a cada cobrança do pedido");
    }

    [Fact]
    public async Task PedidoJaPago_Recusa()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline().MarcarPaga("pay-1", 25m, "pix", CobrancaPedidoFixture.Agora);

        var act = () => f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        (await act.Should().ThrowAsync<CobrancaPedidoConflitoException>()).Which.Codigo.Should().Be("pedido_ja_pago");
    }

    // #1865: a atendente reemite o link de uma encomenda com o sinal pago; antes recebia "Pedido já pago" com saldo em aberto.
    [Fact]
    public async Task SinalPago_ReemissaoCobraORestante()
    {
        var f = new CobrancaPedidoFixture();
        f.Pedido.CriadoEm = CobrancaPedidoFixture.Agora;
        f.Pedido.AgendadoParaEm = CobrancaPedidoFixture.Agora.AddDays(2);
        f.AdicionarOnline(valor: 12.5m).MarcarPaga("pay-1", 12.5m, "pix", CobrancaPedidoFixture.Agora);
        f.AdicionarPagamento("pay-1", 12.5m);

        var r = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        r.Valor.Should().Be(12.5m);
        f.Preferencias.Should().ContainSingle().Which.Items.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new PreferenceItemCommand("Restante do pedido", 1, 12.5m));
    }

    // #1540: depois do estorno total a cobrança fica Estornada e o pagamento permanece. A confirmação
    // (#1289) trata o próximo pagamento como duplicado e devolve sozinha, então o link não pode sair.
    [Fact]
    public async Task PagamentosJaCobremOTotal_SemCobrancaPaga_Recusa()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarPagamento(referencia: null);

        var act = () => f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        (await act.Should().ThrowAsync<CobrancaPedidoConflitoException>()).Which.Codigo.Should().Be("pedido_ja_pago");
        f.Preferencias.Should().BeEmpty();
    }

    [Fact]
    public async Task MercadoPagoFora_LancaIndisponivelSemGravar()
    {
        var f = new CobrancaPedidoFixture();
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("500"));

        var act = () => f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        await act.Should().ThrowAsync<MercadoPagoIndisponivelException>();
        f.Cobrancas.Should().BeEmpty();
    }

    [Fact]
    public async Task QuantidadeFracionaria_VaiComoUmComOSubtotalESomaFechaComOTotal()
    {
        // #1291: (int)1,5 cobrava 1 kg e 0,5 virava 0, que o Mercado Pago recusa.
        var f = new CobrancaPedidoFixture();
        AdicionarItem(f, "Queijo meia cura (kg)", 1.5m, 30m);
        AdicionarItem(f, "Doce de leite (kg)", 0.5m, 12m);

        await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        var pref = f.Preferencias.Should().ContainSingle().Subject;
        pref.Items.Should().ContainEquivalentOf(new PreferenceItemCommand("Queijo meia cura (kg)", 1, 45m));
        pref.Items.Should().ContainEquivalentOf(new PreferenceItemCommand("Doce de leite (kg)", 1, 6m));
        pref.Items.Should().ContainEquivalentOf(new PreferenceItemCommand("Brigadeiro", 2, 10m), "item inteiro não muda");
        pref.Items.Sum(i => i.Quantidade * i.PrecoUnitario).Should().Be(f.Pedido.Total.Valor);
        pref.ValorTotal.Should().Be(76m);
    }

    // #1991-5: pedido feito antes da correção guardou o nome do prato em minúsculas ("1x talharim 500 g" no Mercado
    // Pago). A descrição sai como no cardápio; o que já tem maiúscula não muda.
    [Fact]
    public async Task NomeDoItemEmMinusculas_SaiComoNoCardapio()
    {
        var f = new CobrancaPedidoFixture();
        AdicionarItem(f, "talharim 500 g", 1m, 30m);

        await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        var pref = f.Preferencias.Should().ContainSingle().Subject;
        pref.Items.Should().ContainEquivalentOf(new PreferenceItemCommand("Talharim 500 g", 1, 30m));
        pref.Items.Should().ContainEquivalentOf(new PreferenceItemCommand("Brigadeiro", 2, 10m));
    }

    [Fact]
    public async Task PendenteComValorDiferenteDoTotal_CancelaEGeraOutra()
    {
        // #1291: item editado em AguardandoPagamento; o link antigo cobraria o valor velho.
        var f = new CobrancaPedidoFixture();
        var antiga = f.AdicionarOnline();
        AdicionarItem(f, "Bolo de fubá", 1m, 6m);

        var r = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        antiga.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        r.Reutilizada.Should().BeFalse();
        r.Valor.Should().Be(31m);
        var nova = f.Cobrancas.Should().ContainSingle(c => c.Status == StatusCobrancaPedido.Pendente).Subject;
        nova.Valor.Should().Be(31m);
        f.Preferencias.Should().ContainSingle().Which.IdempotencyKey.Should().EndWith("-2");
    }

    private static void AdicionarItem(CobrancaPedidoFixture f, string nome, decimal qtd, decimal preco)
    {
        var item = new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = f.Pedido.Id,
            Nome = nome,
            Quantidade = qtd,
            PrecoUnitario = preco,
        };
        item.RecalcularSubtotal();
        f.Pedido.Itens.Add(item);
        f.Pedido.RecalcularTotal();
    }
}
