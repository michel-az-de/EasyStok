using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects.Pix;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Pagamentos;

/// <summary>
/// #2233: cobrança Pix direto pela chave da loja. Manual como a da entrega (sem link, sem prazo, sem provedor), com
/// o copia e cola do BR Code estático. Só uma pessoa a quita.
/// </summary>
public class CobrancaPedidoPixDiretoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Pedido = Guid.Parse("ab12cd34-1111-2222-3333-444455556666");
    private static readonly DateTime Agora = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly RecebedorPix Recebedor =
        RecebedorPix.Criar(ChavePix.Criar(TipoChavePix.Email, "loja@exemplo.test"), "Loja Exemplo", "Sao Paulo");

    private static CobrancaPedido PixDireto(Guid? conversa = null) =>
        CobrancaPedido.CriarPixDireto(Empresa, Pedido, 57.9m, Recebedor, Agora, conversa);

    [Fact]
    public void Nasce_pendente_manual_sem_link_nem_prazo_com_o_copia_e_cola()
    {
        var c = PixDireto();

        c.Provedor.Should().Be(CobrancaPedido.ProvedorPixDireto);
        c.Status.Should().Be(StatusCobrancaPedido.Pendente);
        c.EhOnline.Should().BeFalse();
        c.EhPixDireto.Should().BeTrue();
        c.LinkPagamento.Should().BeNull();
        c.ExpiraEm.Should().BeNull();
        c.ReferenciaExterna.Should().BeNull();
        c.PixCopiaCola.Should().Be(BrCodePix.Estatico(Recebedor, 57.9m, "PEDAB12CD34"));
    }

    [Fact]
    public void Encomenda_cobra_o_saldo_e_o_copia_e_cola_pede_so_o_sinal()
    {
        var c = CobrancaPedido.CriarPixDireto(Empresa, Pedido, 57.9m, Recebedor, Agora, valorDoPix: 28.95m);

        c.Valor.Should().Be(57.9m);
        c.PixCopiaCola.Should().Be(BrCodePix.Estatico(Recebedor, 28.95m, "PEDAB12CD34"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    public void Valor_do_pix_fora_do_saldo_e_recusado(decimal valorDoPix)
    {
        var act = () => CobrancaPedido.CriarPixDireto(Empresa, Pedido, 57.9m, Recebedor, Agora, valorDoPix: valorDoPix);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Nunca_vence_nem_entra_no_aviso_de_vencimento()
    {
        var c = PixDireto();

        c.Venceu(Agora.AddYears(1)).Should().BeFalse();
        c.VenceEmBreve(Agora.AddYears(1), TimeSpan.FromDays(400)).Should().BeFalse();
    }

    [Fact]
    public void Quitacao_manual_e_reabertura_valem_como_na_entrega()
    {
        var c = PixDireto();

        c.MarcarPaga("manual-1", 57.9m, "pix", Agora.AddHours(1));
        c.Status.Should().Be(StatusCobrancaPedido.Paga);
        c.PagaEm.Should().Be(Agora.AddHours(1));

        c.ReabrirPagamentoManual(Agora.AddHours(2));
        c.Status.Should().Be(StatusCobrancaPedido.Pendente);
        c.PixCopiaCola.Should().NotBeNull();
    }

    [Fact]
    public void Conversa_vazia_e_valor_zero_sao_recusados()
    {
        ((Action)(() => PixDireto(Guid.Empty))).Should().Throw<RegraDeDominioVioladaException>();
        ((Action)(() => CobrancaPedido.CriarPixDireto(Empresa, Pedido, 0m, Recebedor, Agora)))
            .Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Loja_guarda_e_devolve_o_recebedor_pix()
    {
        var loja = Loja.Criar(Empresa, "Loja Centro");
        loja.ObterRecebedorPix().Should().BeNull();

        loja.DefinirRecebedorPix(Recebedor);

        loja.ObterRecebedorPix().Should().Be(Recebedor);
        loja.PixTipo.Should().Be(TipoChavePix.Email);

        loja.DefinirRecebedorPix(null);
        loja.ObterRecebedorPix().Should().BeNull();
        loja.PixChave.Should().BeNull();
    }
}
