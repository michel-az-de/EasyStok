using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Fiscal;

public class NfeDocumentoTests
{
    private const string ChaveValida = "35260514200166000187650010000000011000000017";
    private const string ProtocoloValido = "135260000123456";

    private static DadosEmissor EmitenteValido() =>
        new(Nome: "Casa da Baba LTDA",
            Documento: "14.200.166/0001-87",
            RazaoSocial: "Casa da Baba LTDA",
            InscricaoEstadual: "123456789",
            RegimeTributario: "Simples",
            Endereco: new Endereco("Av. Paulista", "1000", null, "Bela Vista", "Sao Paulo", "SP", "01310-100"));

    private static DadosFaturado DestinatarioValido() =>
        new(Nome: "Joao da Silva", Documento: "123.456.789-00");

    private static NfeDocumento CriarValida() =>
        NfeDocumento.Criar(
            empresaId: Guid.NewGuid(),
            pedidoId: Guid.NewGuid(),
            serie: 1,
            numero: 42,
            dadosEmitente: EmitenteValido(),
            dadosDestinatario: DestinatarioValido(),
            totalNota: Dinheiro.FromDecimal(120.50m));

    private static NfeDocumento CriarComItem()
    {
        var doc = CriarValida();
        doc.AdicionarItem(
            nomeSnapshot: "Coxinha de frango",
            quantidade: 2,
            precoUnitario: Dinheiro.FromDecimal(60.25m),
            unidade: "UN",
            ncm: "19059090",
            cfop: "5102");
        return doc;
    }

    [Fact]
    public void Criar_inicializa_em_rascunho_com_evento_criado()
    {
        var doc = CriarValida();

        doc.Id.Should().NotBe(Guid.Empty);
        doc.Modelo.Should().Be("65");
        doc.Status.Should().Be(StatusNfe.Rascunho);
        doc.ChaveAcesso.Should().BeNull();
        doc.Eventos.Should().HaveCount(1);
        doc.Eventos.Single().Tipo.Should().Be("criado");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Criar_serie_invalida_lanca(short serie)
    {
        Action act = () => NfeDocumento.Criar(
            Guid.NewGuid(), Guid.NewGuid(), serie, 1, EmitenteValido(), null, Dinheiro.FromDecimal(10m));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_total_zero_lanca()
    {
        Action act = () => NfeDocumento.Criar(
            Guid.NewGuid(), Guid.NewGuid(), 1, 1, EmitenteValido(), null, Dinheiro.Zero);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void AdicionarItem_calcula_subtotal()
    {
        var doc = CriarValida();
        var item = doc.AdicionarItem("Pao de queijo", 3, Dinheiro.FromDecimal(5m), "UN");

        item.Subtotal.Valor.Should().Be(15m);
        item.Ordem.Should().Be(1);
    }

    [Fact]
    public void AdicionarItem_apos_envio_lanca()
    {
        var doc = CriarComItem();
        doc.MarcarEnviada();

        Action act = () => doc.AdicionarItem("X", 1, Dinheiro.FromDecimal(1m), "UN");
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void MarcarEnviada_sem_itens_lanca()
    {
        var doc = CriarValida();
        Action act = () => doc.MarcarEnviada();
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*sem itens*");
    }

    [Fact]
    public void Fluxo_feliz_rascunho_enviada_autorizada()
    {
        var doc = CriarComItem();

        doc.MarcarEnviada();
        doc.Status.Should().Be(StatusNfe.EnviadaAguardandoRetorno);
        doc.Eventos.Last().Tipo.Should().Be("enviado");

        doc.MarcarAutorizada(ChaveValida, ProtocoloValido, "blob/nfe.xml", "https://danfe");
        doc.Status.Should().Be(StatusNfe.Autorizada);
        doc.ChaveAcesso.Should().Be(ChaveValida);
        doc.ProtocoloAutorizacao.Should().Be(ProtocoloValido);
        doc.DataAutorizacao.Should().NotBeNull();
        doc.Eventos.Last().Tipo.Should().Be("autorizado");
    }

    [Fact]
    public void MarcarEnviada_idempotente()
    {
        var doc = CriarComItem();
        doc.MarcarEnviada();
        var eventosAntes = doc.Eventos.Count;

        doc.MarcarEnviada();

        doc.Eventos.Count.Should().Be(eventosAntes);
    }

    [Fact]
    public void MarcarAutorizada_chave_invalida_lanca()
    {
        var doc = CriarComItem();
        doc.MarcarEnviada();

        Action act = () => doc.MarcarAutorizada("123", ProtocoloValido);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarcarAutorizada_a_partir_de_rascunho_lanca()
    {
        var doc = CriarComItem();
        Action act = () => doc.MarcarAutorizada(ChaveValida, ProtocoloValido);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void MarcarAutorizada_dupla_com_chave_diferente_lanca()
    {
        var doc = CriarComItem();
        doc.MarcarEnviada();
        doc.MarcarAutorizada(ChaveValida, ProtocoloValido);

        var outraChave = "35260514200166000187650010000000020000000020";
        Action act = () => doc.MarcarAutorizada(outraChave, ProtocoloValido);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void MarcarRejeitada_a_partir_de_enviada_grava_motivo()
    {
        var doc = CriarComItem();
        doc.MarcarEnviada();

        doc.MarcarRejeitada("Codigo 539: NCM invalido para item 1");

        doc.Status.Should().Be(StatusNfe.Rejeitada);
        doc.MotivoRejeicao.Should().Contain("539");
        doc.Eventos.Last().Tipo.Should().Be("rejeitado");
    }

    [Fact]
    public void Cancelar_so_em_autorizada()
    {
        var doc = CriarComItem();
        doc.MarcarEnviada();

        Action act = () => doc.Cancelar("desistencia cliente");
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Cancelar_autorizada_funciona()
    {
        var doc = CriarComItem();
        doc.MarcarEnviada();
        doc.MarcarAutorizada(ChaveValida, ProtocoloValido);

        doc.Cancelar("desistencia cliente");

        doc.Status.Should().Be(StatusNfe.Cancelada);
        doc.Eventos.Last().Tipo.Should().Be("cancelado");
    }

    [Fact]
    public void Inutilizar_a_partir_de_rascunho_funciona()
    {
        var doc = CriarComItem();

        doc.MarcarInutilizada();

        doc.Status.Should().Be(StatusNfe.Inutilizada);
        doc.Eventos.Last().Tipo.Should().Be("inutilizado");
    }

    [Fact]
    public void Inutilizar_a_partir_de_autorizada_lanca()
    {
        var doc = CriarComItem();
        doc.MarcarEnviada();
        doc.MarcarAutorizada(ChaveValida, ProtocoloValido);

        Action act = () => doc.MarcarInutilizada();
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void FalhaTransiente_e_reenvio_funcionam()
    {
        var doc = CriarComItem();
        doc.MarcarEnviada();
        doc.MarcarFalhaTransiente("timeout SEFAZ-SP");

        doc.Status.Should().Be(StatusNfe.FalhaTransiente);

        doc.MarcarEnviada();

        doc.Status.Should().Be(StatusNfe.EnviadaAguardandoRetorno);
    }
}
