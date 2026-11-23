using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>#3204: mensagem padrão local, já no formato que a Meta aprova (nome, idioma, categoria e {{n}}).</summary>
public class ModeloMensagemLocalTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    private static ModeloMensagemLocal Criar(
        string nome = "pedido_pronto", string idioma = "pt_BR", string categoria = "UTILITY",
        string? cabecalho = null, string corpo = "Olá {{1}}, seu pedido {{2}} está pronto para retirada.", string? rodape = null) =>
        ModeloMensagemLocal.Criar(Empresa, nome, idioma, categoria, cabecalho, corpo, rodape, Agora);

    [Fact]
    public void CriarNormalizaNomeEGuardaOsCampos()
    {
        var modelo = Criar(nome: "  Pedido_Pronto ", cabecalho: "  Casa da Baba ", rodape: " Obrigada! ");

        modelo.Nome.Should().Be("pedido_pronto");
        modelo.Idioma.Should().Be("pt_BR");
        modelo.Categoria.Should().Be("UTILITY");
        modelo.Cabecalho.Should().Be("Casa da Baba");
        modelo.Rodape.Should().Be("Obrigada!");
        modelo.Arquivado.Should().BeFalse();
        modelo.EmpresaId.Should().Be(Empresa);
        modelo.CriadoEm.Should().Be(Agora);
    }

    [Fact]
    public void CabecalhoERodapeVaziosViramNulo()
    {
        var modelo = Criar(cabecalho: "   ", rodape: "");

        modelo.Cabecalho.Should().BeNull();
        modelo.Rodape.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pedido pronto")]
    [InlineData("pedido-pronto")]
    [InlineData("pedido_prontíssimo")]
    public void NomeInvalidoRecusa(string nome)
    {
        var act = () => Criar(nome: nome);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Nome*");
    }

    [Fact]
    public void NomeAcimaDoLimiteRecusa()
    {
        var act = () => Criar(nome: new string('a', ModeloMensagemLocal.NomeTamanhoMaximo + 1));
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Nome*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("pt-BR")]
    [InlineData("PT_BR")]
    [InlineData("pt_br")]
    [InlineData("portugues")]
    public void IdiomaInvalidoOuAusenteRecusa(string idioma)
    {
        var act = () => Criar(idioma: idioma);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Idioma*");
    }

    [Theory]
    [InlineData("pt_BR")]
    [InlineData("en")]
    [InlineData("es_AR")]
    public void IdiomaValidoPassa(string idioma) => Criar(idioma: idioma).Idioma.Should().Be(idioma);

    [Theory]
    [InlineData("MARKETING")]
    [InlineData("AUTHENTICATION")]
    [InlineData("utility")]
    [InlineData("")]
    public void CategoriaDiferenteDeUtilityRecusa(string categoria)
    {
        var act = () => Criar(categoria: categoria);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*promoção*");
    }

    [Fact]
    public void CorpoObrigatorio()
    {
        var act = () => Criar(corpo: "  ");
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Corpo*");
    }

    [Fact]
    public void CorpoAcimaDe1024Recusa()
    {
        var act = () => Criar(corpo: new string('a', ModeloMensagemLocal.CorpoTamanhoMaximo + 1));
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Corpo*");
    }

    [Fact]
    public void CorpoNoLimiteDe1024Passa() =>
        Criar(corpo: new string('a', ModeloMensagemLocal.CorpoTamanhoMaximo)).Corpo.Length.Should().Be(1024);

    [Theory]
    [InlineData("Oi {{2}}, tudo bem?")]
    [InlineData("Oi {{1}}, seu pedido {{3}} saiu.")]
    [InlineData("Oi {{1}} e {{3}}, tudo bem?")]
    public void ParametroForaDeSequenciaRecusa(string corpo)
    {
        var act = () => Criar(corpo: corpo);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*sequência*");
    }

    [Theory]
    [InlineData("{{1}} seu pedido saiu.")]
    [InlineData("Seu pedido saiu, {{1}}")]
    [InlineData("  {{1}} seu pedido saiu.")]
    public void ParametroNoInicioOuNoFimRecusa(string corpo)
    {
        var act = () => Criar(corpo: corpo);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*começar nem terminar*");
    }

    [Theory]
    [InlineData("Oi {{nome}}, tudo bem?")]
    [InlineData("Oi {{ 1 }}, tudo bem?")]
    [InlineData("Oi {{01}}, tudo bem?")]
    [InlineData("Oi {{0}}, tudo bem?")]
    [InlineData("Oi {{1}, tudo bem?")]
    [InlineData("Oi {{, tudo bem?")]
    public void ChavesDuplasForaDoFormatoRecusa(string corpo)
    {
        var act = () => Criar(corpo: corpo);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*{{*");
    }

    [Fact]
    public void ParametroRepetidoPodeAparecerMaisDeUmaVez()
    {
        var modelo = Criar(corpo: "Oi {{1}}, o pedido {{2}} de {{1}} está pronto.");

        modelo.QuantidadeParametros.Should().Be(2);
    }

    [Theory]
    [InlineData("Mensagem sem parâmetro.", 0)]
    [InlineData("Oi {{1}}, tudo bem?", 1)]
    [InlineData("Oi {{1}}, pedido {{2}} e entrega {{3}} ok.", 3)]
    public void QuantidadeParametrosContaOMaiorNumero(string corpo, int esperado) =>
        Criar(corpo: corpo).QuantidadeParametros.Should().Be(esperado);

    [Fact]
    public void CabecalhoComQuebraDeLinhaRecusa()
    {
        var act = () => Criar(cabecalho: "linha 1\nlinha 2");
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Cabeçalho*linha*");
    }

    [Fact]
    public void RodapeComQuebraDeLinhaRecusa()
    {
        var act = () => Criar(rodape: "a\r\nb");
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Rodapé*linha*");
    }

    [Fact]
    public void CabecalhoERodapeComChavesDuplasRecusam()
    {
        ((Action)(() => Criar(cabecalho: "Oi {{1}}"))).Should().Throw<RegraDeDominioVioladaException>();
        ((Action)(() => Criar(rodape: "Oi {{1}}"))).Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void CabecalhoERodapeAcimaDe60Recusam()
    {
        var longo = new string('a', ModeloMensagemLocal.CabecalhoTamanhoMaximo + 1);
        ((Action)(() => Criar(cabecalho: longo))).Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Cabeçalho*");
        ((Action)(() => Criar(rodape: longo))).Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Rodapé*");
    }

    [Fact]
    public void EmpresaVaziaRecusa()
    {
        var act = () => ModeloMensagemLocal.Criar(Guid.Empty, "a", "pt_BR", "UTILITY", null, "texto", null, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void EditarAtualizaEPreservaCriacao()
    {
        var modelo = Criar();

        modelo.Editar("Novo_Nome", "en", "UTILITY", "Topo", "Hi {{1}}, order ready.", "Rodapé", Agora.AddMinutes(5));

        modelo.Nome.Should().Be("novo_nome");
        modelo.Idioma.Should().Be("en");
        modelo.Cabecalho.Should().Be("Topo");
        modelo.QuantidadeParametros.Should().Be(1);
        modelo.CriadoEm.Should().Be(Agora);
        modelo.AlteradoEm.Should().Be(Agora.AddMinutes(5));
    }

    [Fact]
    public void EditarInvalidoNaoAlteraOModelo()
    {
        var modelo = Criar();

        var act = () => modelo.Editar("outro", "pt_BR", "MARKETING", null, "texto", null, Agora.AddMinutes(1));

        act.Should().Throw<RegraDeDominioVioladaException>();
        modelo.Nome.Should().Be("pedido_pronto");
    }

    [Fact]
    public void ArquivarEDesarquivar()
    {
        var modelo = Criar();

        modelo.Arquivar(Agora.AddMinutes(1));
        modelo.Arquivado.Should().BeTrue();
        modelo.AlteradoEm.Should().Be(Agora.AddMinutes(1));

        modelo.Desarquivar(Agora.AddMinutes(2));
        modelo.Arquivado.Should().BeFalse();
        modelo.AlteradoEm.Should().Be(Agora.AddMinutes(2));
    }
}
