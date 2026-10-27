using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Services;

/// <summary>#2070: a prontidão dos passos essenciais é função pura do retrato de contagens.</summary>
public class ProntidaoDaConfiguracaoInicialTests
{
    private static readonly RetratoDaConfiguracaoInicial Vazio = new(
        HorariosGravados: false, DiasAbertos: 0, TemVitrine: false, TemLogo: false, ItensVisiveisComPreco: 0,
        JanelasAtivas: 0, ZonasAtivas: 0, RaioConfigurado: false, EquipeAtivaPorNivel: new Dictionary<NivelAcesso, int>());

    private static readonly RetratoDaConfiguracaoInicial Completo = new(
        HorariosGravados: true, DiasAbertos: 6, TemVitrine: true, TemLogo: true, ItensVisiveisComPreco: 12,
        JanelasAtivas: 3, ZonasAtivas: 2, RaioConfigurado: false,
        EquipeAtivaPorNivel: new Dictionary<NivelAcesso, int> { [NivelAcesso.Admin] = 1, [NivelAcesso.Operador] = 2 });

    [Theory]
    [InlineData("horarios", ProntidaoDaConfiguracaoInicial.FaltaSalvarHorarios)]
    [InlineData("site-e-impressos", ProntidaoDaConfiguracaoInicial.FaltaVitrine, ProntidaoDaConfiguracaoInicial.FaltaLogo)]
    [InlineData("cardapio", ProntidaoDaConfiguracaoInicial.FaltaItemComPreco)]
    [InlineData("janelas", ProntidaoDaConfiguracaoInicial.FaltaJanela)]
    [InlineData("frete", ProntidaoDaConfiguracaoInicial.FaltaFrete)]
    [InlineData("equipe", ProntidaoDaConfiguracaoInicial.FaltaEquipe)]
    public void LojaVaziaDizOQueFaltaEmCadaPassoEssencial(string passo, params string[] falta)
    {
        var prontidao = ProntidaoDaConfiguracaoInicial.Avaliar(passo, Vazio)!;

        prontidao.Ok.Should().BeFalse();
        prontidao.Falta.Should().Equal(falta);
    }

    [Theory]
    [InlineData("horarios")]
    [InlineData("site-e-impressos")]
    [InlineData("cardapio")]
    [InlineData("janelas")]
    [InlineData("frete")]
    [InlineData("equipe")]
    public void LojaCompletaFicaProntaSemFalta(string passo)
    {
        var prontidao = ProntidaoDaConfiguracaoInicial.Avaliar(passo, Completo)!;

        prontidao.Ok.Should().BeTrue();
        prontidao.Falta.Should().BeEmpty();
    }

    [Theory]
    [InlineData("boas-vindas")]
    [InlineData("dados-da-loja")]
    [InlineData("atendimento")]
    [InlineData("respostas")]
    [InlineData("agente")]
    [InlineData("producao")]
    [InlineData("estoque")]
    [InlineData("entregadores")]
    [InlineData("impressora")]
    [InlineData("avisos")]
    [InlineData("pronto")]
    public void PassoSemProntidaoDevolveNulo(string passo) =>
        ProntidaoDaConfiguracaoInicial.Avaliar(passo, Completo).Should().BeNull();

    [Fact]
    public void HorariosSalvosSemNenhumDiaAbertoPedemUmDia()
    {
        var prontidao = ProntidaoDaConfiguracaoInicial.Avaliar("horarios", Vazio with { HorariosGravados = true })!;

        prontidao.Ok.Should().BeFalse();
        prontidao.Falta.Should().Equal(ProntidaoDaConfiguracaoInicial.FaltaDiaAberto);
    }

    [Fact]
    public void VitrineSemLogoPedeSoOLogo() =>
        ProntidaoDaConfiguracaoInicial.Avaliar("site-e-impressos", Vazio with { TemVitrine = true })!
            .Falta.Should().Equal(ProntidaoDaConfiguracaoInicial.FaltaLogo);

    [Fact]
    public void FretePorRaioSemZonaEstaPronto() =>
        ProntidaoDaConfiguracaoInicial.Avaliar("frete", Vazio with { RaioConfigurado = true })!.Ok.Should().BeTrue();

    [Theory]
    [InlineData(NivelAcesso.Admin, false)]
    [InlineData(NivelAcesso.SuperAdmin, false)]
    [InlineData(NivelAcesso.Visualizador, false)]
    [InlineData(NivelAcesso.Gerente, true)]
    [InlineData(NivelAcesso.Operador, true)]
    public void EquipeProntaPedeAlguemQueAtendaOuCozinhe(NivelAcesso nivel, bool pronta) =>
        ProntidaoDaConfiguracaoInicial.Avaliar("equipe",
                Vazio with { EquipeAtivaPorNivel = new Dictionary<NivelAcesso, int> { [nivel] = 1 } })!
            .Ok.Should().Be(pronta);
}
