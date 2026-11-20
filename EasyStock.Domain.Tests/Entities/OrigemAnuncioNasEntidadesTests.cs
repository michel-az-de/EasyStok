using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>#2928: a conversa guarda o último anúncio; o pedido guarda a origem de quando nasceu.</summary>
public class OrigemAnuncioNasEntidadesTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static OrigemAnuncio Anuncio(string id) => OrigemAnuncio.DoAnuncioWhatsApp(id, "Promo", null, Agora);

    [Fact]
    public void Conversa_nasce_sem_origem_de_anuncio()
    {
        var conversa = Conversa.Abrir(Guid.NewGuid(), "5511999999999", DateTime.UtcNow);

        Assert.Null(conversa.OrigemAnuncio);
    }

    [Fact]
    public void Conversa_ultimo_anuncio_substitui_o_anterior()
    {
        var conversa = Conversa.Abrir(Guid.NewGuid(), "5511999999999", DateTime.UtcNow);

        conversa.RegistrarOrigemAnuncio(Anuncio("111"));
        conversa.RegistrarOrigemAnuncio(Anuncio("222"));

        Assert.Equal("222", conversa.OrigemAnuncio!.IdAnuncio);
    }

    [Fact]
    public void Conversa_origem_nula_nao_apaga_a_existente()
    {
        var conversa = Conversa.Abrir(Guid.NewGuid(), "5511999999999", DateTime.UtcNow);
        conversa.RegistrarOrigemAnuncio(Anuncio("111"));

        conversa.RegistrarOrigemAnuncio(null);

        Assert.Equal("111", conversa.OrigemAnuncio!.IdAnuncio);
    }

    [Fact]
    public void Pedido_guarda_a_origem_de_quando_nasceu_e_nao_muda_depois()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());

        pedido.DefinirOrigemAnuncio(Anuncio("111"));
        pedido.DefinirOrigemAnuncio(Anuncio("222"));

        Assert.Equal("111", pedido.OrigemAnuncio!.IdAnuncio);
    }

    [Fact]
    public void Pedido_sem_anuncio_fica_sem_origem()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());

        pedido.DefinirOrigemAnuncio(null);

        Assert.Null(pedido.OrigemAnuncio);
    }

    [Fact]
    public void CapturadaEm_e_gravada_em_UTC()
    {
        var local = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(-3));

        var origem = OrigemAnuncio.DoAnuncioWhatsApp("1", null, null, local);

        Assert.Equal(TimeSpan.Zero, origem.CapturadaEm.Offset);
        Assert.Equal(local, origem.CapturadaEm);
    }

    [Fact]
    public void Corte_no_tamanho_maximo_nao_parte_emoji_ao_meio()
    {
        // Emoji (par substituto) cruzando a posição 120: o corte não pode deixar meio par, que o Npgsql recusa.
        var titulo = new string('a', OrigemAnuncio.TamanhoTexto - 1) + "🎁" + "fim";

        var origem = OrigemAnuncio.DoAnuncioWhatsApp("1", titulo, null, Agora);

        Assert.False(char.IsHighSurrogate(origem.TituloAnuncio![^1]));
        Assert.True(origem.TituloAnuncio.Length <= OrigemAnuncio.TamanhoTexto);
    }
}
