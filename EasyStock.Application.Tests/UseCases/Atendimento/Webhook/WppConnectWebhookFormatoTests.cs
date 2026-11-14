using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

public class WppConnectWebhookFormatoTests
{
    private static string Sessao => CredencialWppConnect.NomeSessao(Guid.Parse("c8ebd039-563e-4f34-a606-c5f7a788c156"));

    [Fact]
    public void Descreve_campos_e_tipos_sem_expor_valor_de_cliente()
    {
        var raw = $$$"""{"event":"unreadmessages","session":"{{{Sessao}}}","id":{"fromMe":false,"remote":{"server":"c.us","user":"5511999999999","_serialized":"5511999999999@c.us"},"id":"3EB0AAAA"},"from":"5511999999999@c.us","type":"chat","body":"Quero um bolo de cenoura","t":1793000000,"sender":{"pushname":"Maria Teste"}}""";

        var formato = WppConnectWebhookFormato.Descrever(raw);

        formato.Should().Be("evento=unreadmessages tipo=chat campos{event:string(14),session:string(35),"
            + "id:{fromMe:false,remote:{server:string(4),user:string(13),_serialized:string(18)},id:string(8)},"
            + "from:string(18),type:string(4),body:string(24),t:number,sender:{pushname:string(11)}}");
        formato.Should().NotContainAny("5511999999999", "bolo", "Maria", "3EB0AAAA");
    }

    [Fact]
    public void Nome_de_campo_que_pode_ser_dado_so_e_contado()
    {
        var raw = $$$"""{"event":"unreadmessages","session":"{{{Sessao}}}","0":"f","1":"a","5511999999999@c.us":{"x":1},"t1793000000":true}""";

        WppConnectWebhookFormato.Descrever(raw).Should()
            .Be("evento=unreadmessages tipo=ausente campos{event:string(14),session:string(35),+2 índices,+2 fora do padrão}");
    }

    [Fact]
    public void Evento_e_tipo_fora_do_padrao_de_rotulo_nao_sao_ecoados()
    {
        var formato = WppConnectWebhookFormato.Descrever("""{"event":"5511999999999","type":"Maria Teste"}""");

        formato.Should().Be("evento=? tipo=? campos{event:string(13),type:string(11)}");
    }

    [Theory]
    [InlineData(null, "corpo vazio")]
    [InlineData("  ", "corpo vazio")]
    [InlineData("{\"event\":", "json inválido (9 caracteres)")]
    [InlineData("[1,2]", "raiz=array(2)")]
    [InlineData("\"5511999999999\"", "raiz=string(13)")]
    public void Corpo_que_nao_e_objeto_nao_derruba_o_registro(string? raw, string esperado) =>
        WppConnectWebhookFormato.Descrever(raw).Should().Be(esperado);

    [Fact]
    public void Corta_objeto_largo_ou_fundo_demais()
    {
        var largo = "{" + string.Join(",", Enumerable.Range(0, 60).Select(i => $"\"campo_{(char)('a' + i / 26)}{(char)('a' + i % 26)}\":null")) + "}";
        WppConnectWebhookFormato.Descrever(largo).Should().EndWith(",+20 campos}");

        WppConnectWebhookFormato.Descrever("""{"a":{"b":{"c":{"d":1}}}}""").Should().EndWith("campos{a:{b:{c:object}}}");

        var longo = "{" + string.Join(",", Enumerable.Range(0, 40).Select(i => $"\"campo_bem_comprido_para_estourar_{(char)('a' + i / 26)}{(char)('a' + i % 26)}\":{{\"x\":null}}")) + "}";
        WppConnectWebhookFormato.Descrever(longo).Should().HaveLength(1203).And.EndWith("...");
    }
}
