using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>S42: resposta pronta com atalho e variáveis renderizadas no servidor.</summary>
public class RespostaProntaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CriarNormalizaAtalhoETexto()
    {
        var resposta = RespostaPronta.Criar(Empresa, "  Boas-vindas ", " /Oi ", "  Olá, {nome}!  ", Agora);

        resposta.Titulo.Should().Be("Boas-vindas");
        resposta.Atalho.Should().Be("/oi");
        resposta.Texto.Should().Be("Olá, {nome}!");
        resposta.Arquivada.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("com espaco")]
    public void AtalhoInvalidoRecusa(string atalho)
    {
        var act = () => RespostaPronta.Criar(Empresa, "T", atalho, "texto", Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RenderSubstituiVariaveis()
    {
        var resposta = RespostaPronta.Criar(Empresa, "T", "/p", "Oi {nome}, pedido {pedido} às {faixa}.", Agora);

        var texto = resposta.Renderizar(new Dictionary<string, string?>
        {
            ["nome"] = "Ana", ["pedido"] = "AB12CD34", ["faixa"] = "30/09 às 18:00",
        });

        texto.Should().Be("Oi Ana, pedido AB12CD34 às 30/09 às 18:00.");
    }

    [Fact]
    public void RenderSemVariavelFalha()
    {
        var resposta = RespostaPronta.Criar(Empresa, "T", "/p", "Seu pedido {pedido} saiu.", Agora);

        var act = () => resposta.Renderizar(new Dictionary<string, string?> { ["nome"] = "Ana", ["pedido"] = null });

        act.Should().Throw<VariavelSemValorException>().Which.Variavel.Should().Be("pedido");
    }

    [Fact]
    public void RenderIgnoraChaveQueNaoEVariavel()
    {
        var texto = ModeloTextoAtendimento.Renderizar("Use {cupom} hoje", new Dictionary<string, string?>());
        texto.Should().Be("Use {cupom} hoje");
    }

    [Fact]
    public void ArquivarEEditar()
    {
        var resposta = RespostaPronta.Criar(Empresa, "T", "/p", "x", Agora);

        resposta.Editar("Novo", "/novo", "y", Agora.AddMinutes(1));
        resposta.Arquivar(Agora.AddMinutes(2));

        resposta.Atalho.Should().Be("/novo");
        resposta.Texto.Should().Be("y");
        resposta.Arquivada.Should().BeTrue();
    }

    [Fact]
    public void CategoriaAntigaOuOmitidaFicaNula()
    {
        RespostaPronta.Criar(Empresa, "T", "/p", "x", Agora).Categoria.Should().BeNull();
    }

    [Theory]
    [InlineData("  Entrega ", "Entrega")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void CategoriaApararEVazioViraNulo(string? entrada, string? esperado)
    {
        var resposta = RespostaPronta.Criar(Empresa, "T", "/p", "x", Agora, entrada);

        resposta.Categoria.Should().Be(esperado);
    }

    [Fact]
    public void EditarTrocaELimpaACategoria()
    {
        var resposta = RespostaPronta.Criar(Empresa, "T", "/p", "x", Agora, "Entrega");

        resposta.Editar("T", "/p", "x", Agora.AddMinutes(1), "Pagamento");
        resposta.Categoria.Should().Be("Pagamento");

        resposta.Editar("T", "/p", "x", Agora.AddMinutes(2), null);
        resposta.Categoria.Should().BeNull();
    }

    [Fact]
    public void CategoriaNoLimiteAceitaEAcimaRecusa()
    {
        RespostaPronta.Criar(Empresa, "T", "/p", "x", Agora, new string('a', RespostaPronta.CategoriaTamanhoMaximo))
            .Categoria.Should().HaveLength(RespostaPronta.CategoriaTamanhoMaximo);

        var act = () => RespostaPronta.Criar(Empresa, "T", "/p", "x", Agora, new string('a', RespostaPronta.CategoriaTamanhoMaximo + 1));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("Entrega\nrápida")]
    [InlineData("Entrega\r\nrápida")]
    public void CategoriaComQuebraDeLinhaRecusa(string categoria)
    {
        var act = () => RespostaPronta.Criar(Empresa, "T", "/p", "x", Agora, categoria);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}

/// <summary>S42: regra automática por gatilho e a escolha do gatilho na entrada do cliente.</summary>
public class RegraAutomaticaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CriarExigeTextoEGatilhoValido()
    {
        var regra = RegraAutomatica.Criar(Empresa, GatilhoAutomacao.PosEntrega, "Chegou tudo certo, {nome}?", true, Agora);
        regra.Ligada.Should().BeTrue();

        ((Action)(() => RegraAutomatica.Criar(Empresa, (GatilhoAutomacao)99, "x", true, Agora)))
            .Should().Throw<RegraDeDominioVioladaException>();
        ((Action)(() => RegraAutomatica.Criar(Empresa, GatilhoAutomacao.PosEntrega, " ", true, Agora)))
            .Should().Throw<RegraDeDominioVioladaException>();
    }

    // #2781: regra antiga com chave que o servidor não preenche; desligar (o rollback) manda o texto já gravado.
    [Fact]
    public void DesligarRegraComTextoAntigoSalva()
    {
        var regra = RegraAutomatica.Criar(Empresa, GatilhoAutomacao.PosEntrega, "Oi, {nome}", true, Agora);
        typeof(RegraAutomatica).GetProperty(nameof(RegraAutomatica.Texto))!.SetValue(regra, "Oi {loja}");

        regra.Alterar("Oi {loja}", false, Agora);

        regra.Ligada.Should().BeFalse();
        ((Action)(() => regra.Alterar("Oi {outra}", false, Agora))).Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void EditarRespostaComTextoAntigoMantemSalva()
    {
        var r = RespostaPronta.Criar(Empresa, "Titulo", "atalho", "Oi, {nome}", Agora);
        typeof(RespostaPronta).GetProperty(nameof(RespostaPronta.Texto))!.SetValue(r, "Oi {loja}");

        r.Editar("Novo titulo", "atalho", "Oi {loja}", Agora);

        r.Titulo.Should().Be("Novo titulo");
    }

    /// <summary>
    /// #1610: o lembrete do link nasce ligado com texto padrão, sem regra gravada. Os gatilhos antigos continuam
    /// desligados até a dona salvar.
    /// </summary>
    [Fact]
    public void PadraoSoParaOLembreteDoLink()
    {
        var padrao = RegraAutomatica.Padrao(Empresa, GatilhoAutomacao.CobrancaVencendo, Agora);
        padrao.Should().NotBeNull();
        padrao!.Ligada.Should().BeTrue();
        padrao.Gatilho.Should().Be(GatilhoAutomacao.CobrancaVencendo);
        padrao.Texto.Should().Contain("{pedido}").And.NotContain("{nome}", "nome pode faltar, e variável sem valor não sai");

        // "Pagamento confirmado" também nasce ligada (#1765-4); os demais seguem desligados.
        foreach (var antigo in Enum.GetValues<GatilhoAutomacao>()
                     .Where(g => g is not (GatilhoAutomacao.CobrancaVencendo or GatilhoAutomacao.PagamentoConfirmado
                         or GatilhoAutomacao.SaudacaoSolta)))
            RegraAutomatica.Padrao(Empresa, antigo, Agora).Should().BeNull();
    }

    /// <summary>#2885 (S75, D3): a saudação solta nasce desligada, com o texto sugerido.</summary>
    [Fact]
    public void SaudacaoSoltaNasceDesligadaComTextoSugerido()
    {
        var padrao = RegraAutomatica.Padrao(Empresa, GatilhoAutomacao.SaudacaoSolta, Agora);

        padrao.Should().NotBeNull();
        padrao!.Ligada.Should().BeFalse();
        padrao.Texto.Should().Be("Oi! Como a Casa pode ajudar?");
    }

    [Theory]
    [InlineData(true, false, GatilhoAutomacao.PrimeiroContato)]
    [InlineData(false, false, GatilhoAutomacao.ForaDoHorario)]
    [InlineData(false, true, GatilhoAutomacao.LojaFechada)]
    public void GatilhoDaEntradaEscolheSoUm(bool aberta, bool fechadaNaMao, GatilhoAutomacao esperado) =>
        RegraAutomatica.GatilhoDaPrimeiraEntrada(aberta, fechadaNaMao).Should().Be(esperado);

    // #1991-2 (#1765-4, decisão do Felipe de 23/10): "Pagamento confirmado" nasce ligada, com nome e entrega.
    [Fact]
    public void PagamentoConfirmadoNasceLigado()
    {
        var padrao = RegraAutomatica.Padrao(Empresa, GatilhoAutomacao.PagamentoConfirmado, Agora);

        padrao.Should().NotBeNull();
        padrao!.Ligada.Should().BeTrue();
        padrao.Texto.Should().Be(RegraAutomatica.TextoPadraoPagamentoConfirmado);
        padrao.Renderizar(new Dictionary<string, string?> { ["nome"] = "Ana", ["faixa"] = "01/10 às 14:00" })
            .Should().Be("Oi, Ana! Pagamento recebido. Seu pedido está confirmado para 01/10 às 14:00.");
    }

    [Theory]
    [InlineData(GatilhoAutomacao.PrimeiroContato)]
    [InlineData(GatilhoAutomacao.ForaDoHorario)]
    [InlineData(GatilhoAutomacao.LojaFechada)]
    [InlineData(GatilhoAutomacao.PosEntrega)]
    [InlineData(GatilhoAutomacao.Encerramento)]
    public void DemaisGatilhosNascemDesligados(GatilhoAutomacao gatilho) =>
        RegraAutomatica.Padrao(Empresa, gatilho, Agora).Should().BeNull();
}
