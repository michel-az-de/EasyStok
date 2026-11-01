using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

public class WppConnectWebhookParserTests
{
    private static readonly Guid Empresa = Guid.Parse("c8ebd039-563e-4f34-a606-c5f7a788c156");
    private static string Sessao => CredencialWppConnect.NomeSessao(Empresa);

    [Theory]
    [InlineData("onmessage")]
    [InlineData("unreadmessages")]
    public void Texto_preserva_identidade_e_numero_sem_confundir_com_LID(string evento)
    {
        var raw = $$$"""{"event":"{{{evento}}}","session":"{{{Sessao}}}","id":"false_5511999999999@c.us_ABC","from":"5511999999999@c.us","type":"chat","body":"Olá","t":1793000000,"sender":{"pushname":"Cliente"}}""";
        var entrada = WppConnectWebhookParser.Parse(raw).Entradas.Single();
        entrada.Mensagens.Single().De.Should().Be("5511999999999");
        entrada.Mensagens.Single().TextoCorpo.Should().Be("Olá");
        entrada.Mensagens.Single().Wamid.Should().Be($"wpp:{Sessao}:false_5511999999999@c.us_ABC");
        entrada.Contatos.Single().Nome.Should().Be("Cliente");
    }

    [Theory]
    [InlineData("image", "image")]
    [InlineData("ptt", "audio")]
    [InlineData("document", "document")]
    public void Midia_usa_id_para_download_autenticado(string tipo, string esperado)
    {
        var raw = $$$"""{"event":"onmessage","session":"{{{Sessao}}}","id":"media-id","from":"5511999999999@c.us","type":"{{{tipo}}}","caption":"Anexo","mimetype":"image/jpeg","t":1793000000}""";
        var msg = WppConnectWebhookParser.Parse(raw).Entradas.Single().Mensagens.Single();
        msg.Tipo.Should().Be(esperado);
        msg.MidiaId.Should().Be("wpp:media-id");
    }

    [Theory]
    [InlineData("-25.4284", "-49.2733")]
    [InlineData("\"-25.4284\"", "\"-49.2733\"")]
    public void Localizacao_preserva_coordenadas_e_descricao(string lat, string lng)
    {
        // #2155: antes só o caption sobrava e a localização chegava vazia.
        var raw = $$$"""{"event":"onmessage","session":"{{{Sessao}}}","id":"loc-1","from":"5511999999999@c.us","type":"location","body":"/9j/miniatura","lat":{{{lat}}},"lng":{{{lng}}},"loc":"Rua XV de Novembro, Curitiba","t":1793000000}""";
        var msg = WppConnectWebhookParser.Parse(raw).Entradas.Single().Mensagens.Single();
        msg.Tipo.Should().Be("location");
        msg.Localizacao.Should().Be(new LocalizacaoWhatsApp(-25.4284, -49.2733, "Rua XV de Novembro, Curitiba", null));
        msg.TextoCorpo.Should().Be("Rua XV de Novembro, Curitiba; -25.4284,-49.2733");
    }

    [Fact]
    public void Eco_do_celular_nao_vira_entrada_do_cliente()
    {
        var raw = $$$"""{"event":"onselfmessage","session":"{{{Sessao}}}","id":{"_serialized":"true_5511999999999@c.us_ABC"},"fromMe":true,"to":"5511999999999@c.us","type":"chat","body":"Respondido pelo celular","t":1793000000}""";
        var entrada = WppConnectWebhookParser.Parse(raw).Entradas.Single();
        entrada.Mensagens.Should().BeEmpty();
        entrada.Ecos.Single().Para.Should().Be("5511999999999");
    }

    [Theory]
    [InlineData(1, "sent")]
    [InlineData(2, "delivered")]
    [InlineData(3, "read")]
    [InlineData(-1, "failed")]
    public void Ack_e_normalizado_sem_inventar_entrega(int ack, string esperado)
    {
        var raw = $$$"""{"event":"onack","session":"{{{Sessao}}}","id":{"_serialized":"msg-1","fromMe":true},"ack":{{{ack}}}}""";
        WppConnectWebhookParser.Parse(raw).Entradas.Single().Statuses.Single().Status.Should().Be(esperado);
    }

    [Fact]
    public void Ignora_grupos_e_nao_trata_identificador_LID_como_telefone()
    {
        var grupo = $$$"""{"event":"onmessage","session":"{{{Sessao}}}","id":"g1","from":"123@g.us","type":"chat","body":"grupo","t":1793000000}""";
        WppConnectWebhookParser.Parse(grupo).Entradas.Should().BeEmpty();
        var lid = $$$"""{"event":"onmessage","session":"{{{Sessao}}}","id":"l1","from":"123@lid","type":"chat","body":"cliente","t":1793000000}""";
        var act = () => WppConnectWebhookParser.Parse(lid);
        act.Should().Throw<FormatException>();
        WppConnectWebhookParser.Parse(lid, "5511999999999").Entradas.Single().Mensagens.Single().De.Should().Be("5511999999999");
    }

    // Risco conhecido #2071 (caracterização, não correção): o use case só resolve o LID de "to" no evento
    // onselfmessage, mas o parser trata como eco também o onmessage com fromMe. Nesse eco, um "to" LID cai no
    // sender (o próprio número da loja) ou, sem sender, vira FormatException e o webhook responde 400.
    // Quem corrigir o risco inverte estas asserções.
    [Fact]
    public void Risco_eco_fromMe_com_destino_LID_nao_resolvido_vira_400_ou_cai_no_numero_da_loja()
    {
        var semSender = $$$"""{"event":"onmessage","session":"{{{Sessao}}}","id":"e1","fromMe":true,"from":"5511888888888@c.us","to":"123@lid","type":"chat","body":"loja","t":1793000000}""";
        var act = () => WppConnectWebhookParser.Parse(semSender);
        act.Should().Throw<FormatException>();

        var comSender = $$$"""{"event":"onmessage","session":"{{{Sessao}}}","id":"e2","fromMe":true,"from":"5511888888888@c.us","to":"123@lid","type":"chat","body":"loja","t":1793000000,"sender":{"id":"5511888888888@c.us"}}""";
        WppConnectWebhookParser.Parse(comSender).Entradas.Single().Ecos.Single().Para.Should().Be("5511888888888");
    }

    [Fact]
    public void Sessao_tem_que_conter_empresa_valida()
    {
        CredencialWppConnect.EmpresaDaSessao(Sessao).Should().Be(Empresa);
        CredencialWppConnect.EmpresaDaSessao("outra-sessao").Should().BeNull();
    }

    [Theory]
    [InlineData("onmessage")]
    [InlineData("unreadmessages")]
    public void Historico_anterior_ao_piloto_nao_dispara_resposta(string evento)
    {
        var raw = $$$"""{"event":"{{{evento}}}","session":"{{{Sessao}}}","id":"antiga","from":"5511999999999@c.us","type":"chat","body":"Histórico","t":1793000000}""";
        WppConnectWebhookParser.Parse(raw, inicioDaCaptura: DateTimeOffset.FromUnixTimeSeconds(1793000001))
            .Entradas.Should().BeEmpty();
        WppConnectWebhookParser.Parse(raw, inicioDaCaptura: DateTimeOffset.FromUnixTimeSeconds(1793000000))
            .Entradas.Single().Mensagens.Should().ContainSingle();
    }
}
