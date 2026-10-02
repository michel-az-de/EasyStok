using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N13: o payload de exemplo é a fonte única do disparo de teste e do teste de renderização do catálogo.</summary>
public class ExemplosDeEventoTests
{
    private static readonly TipoEventoNotificacao[] TiposDoCatalogo =
    [
        TipoEventoNotificacao.ResetSenha, TipoEventoNotificacao.ConviteAcesso, TipoEventoNotificacao.IncidenteSistema,
        TipoEventoNotificacao.PrazoEstourado, TipoEventoNotificacao.ResumoDiario, TipoEventoNotificacao.ContatoAlterado
    ];

    [Fact]
    public void TodoTipoDoCatalogoTemExemplo()
    {
        ExemplosDeEvento.Tipos.Should().BeEquivalentTo(TiposDoCatalogo);

        foreach (var tipo in TiposDoCatalogo)
            ExemplosDeEvento.Obter(tipo).Should().NotBeEmpty($"{tipo} precisa de exemplo");
    }

    [Theory]
    [InlineData(TipoEventoNotificacao.ResetSenha, "nome,email,usuarioId,link_redefinicao,codigo,expira_em_minutos")]
    [InlineData(TipoEventoNotificacao.ConviteAcesso, "nome,email,usuarioId,empresa,link_convite,expira_em_dias")]
    [InlineData(TipoEventoNotificacao.IncidenteSistema, "componente,estado_texto,gravidade,desde,duracao")]
    [InlineData(TipoEventoNotificacao.PrazoEstourado, "tipo_legivel,referencia,prazo_texto,atraso_texto")]
    [InlineData(TipoEventoNotificacao.ContatoAlterado, "nome,email,usuarioId,contato,novo_mascarado,quando")]
    [InlineData(TipoEventoNotificacao.ResumoDiario, "data,entregues,faturamento,ticket_medio,pendentes,valor_pendentes,caixa_texto,pix_texto")]
    public void ExemploTemExatamenteAsChavesDoContrato(TipoEventoNotificacao tipo, string chaves) =>
        ExemplosDeEvento.Obter(tipo).Keys.Should().BeEquivalentTo(chaves.Split(','));

    [Fact]
    public void ExemploNaoTemChaveDeClienteNemSegredoReal()
    {
        string[] proibidas = ["telefone", "celular", "whatsapp", "cpf", "cnpj", "cliente", "cliente_nome", "senha", "token", "token_convite_whatsapp"];

        foreach (var tipo in ExemplosDeEvento.Tipos)
        {
            var exemplo = ExemplosDeEvento.Obter(tipo);
            exemplo.Keys.Should().NotContain(proibidas, $"{tipo} não leva dado de cliente nem token");

            foreach (var (chave, valor) in exemplo.Where(p => p.Key.StartsWith("link_")))
                valor!.ToString().Should().EndWith("#teste", $"{tipo}.{chave} nunca leva token real");
        }

        ExemplosDeEvento.Obter(TipoEventoNotificacao.ResetSenha)["codigo"]!.ToString().Should().Be("000000");
    }

    [Fact]
    public void TipoForaDoCatalogoNaoTemExemplo()
    {
        ExemplosDeEvento.TryObter(TipoEventoNotificacao.PedidoEntregue, out _).Should().BeFalse();
        var acao = () => ExemplosDeEvento.Obter(TipoEventoNotificacao.PedidoEntregue);
        acao.Should().Throw<ArgumentOutOfRangeException>();
    }
}
