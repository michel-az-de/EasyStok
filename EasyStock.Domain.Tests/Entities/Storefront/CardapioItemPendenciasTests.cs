using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// #1808: o que falta na apresentação do prato (foto, categoria, descrição). A lista de Itens mostra
/// a dica sem IA e o assistente usa as mesmas pendências para escrever a dica por IA.
/// </summary>
public class CardapioItemPendenciasTests
{
    private static CardapioItem Prato(string? categoria = null) => CardapioItem.CriarAvulso(Guid.NewGuid(), "Lasanha", 30m, categoria);

    [Fact]
    public void Prato_sem_foto_categoria_e_descricao_tem_as_tres_pendencias()
    {
        Prato().PendenciasDeApresentacao().Should().Equal(
            CardapioItem.PendenciaSemFoto, CardapioItem.PendenciaSemCategoria, CardapioItem.PendenciaSemDescricao);
    }

    [Fact]
    public void Descricao_curta_e_pendencia_diferente_de_sem_descricao()
    {
        var prato = Prato("massas");
        prato.AtualizarMetadata(descricaoPublica: "Gostosa.", fotoUrl: "/files/lasanha.webp");

        prato.PendenciasDeApresentacao().Should().Equal(CardapioItem.PendenciaDescricaoCurta);
    }

    [Fact]
    public void Prato_completo_nao_tem_pendencia()
    {
        var prato = Prato("massas");
        prato.AtualizarMetadata(descricaoPublica: "Massa fresca em camadas com ragu de carne e molho branco.", fotoUrl: "/files/lasanha.webp");

        prato.PendenciasDeApresentacao().Should().BeEmpty();
    }
}
