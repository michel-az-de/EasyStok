using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.Data.Configurations;

/// <summary>
/// #2928: mapeamento do <see cref="OrigemAnuncio"/> como owned type em colunas da tabela do dono
/// (conversa e pedido), para o relatório filtrar e agrupar. Colunas anuláveis: sem anúncio, tudo nulo.
/// </summary>
internal static class OrigemAnuncioMapeamento
{
    public static void Mapear<TDono>(OwnedNavigationBuilder<TDono, OrigemAnuncio> o) where TDono : class
    {
        o.Property(x => x.Plataforma).HasMaxLength(OrigemAnuncio.TamanhoPlataforma);
        o.Property(x => x.Via).HasMaxLength(OrigemAnuncio.TamanhoVia);
        o.Property(x => x.Fonte).HasMaxLength(OrigemAnuncio.TamanhoTexto);
        o.Property(x => x.Meio).HasMaxLength(OrigemAnuncio.TamanhoTexto);
        o.Property(x => x.Campanha).HasMaxLength(OrigemAnuncio.TamanhoTexto);
        o.Property(x => x.Conteudo).HasMaxLength(OrigemAnuncio.TamanhoTexto);
        o.Property(x => x.Termo).HasMaxLength(OrigemAnuncio.TamanhoTexto);
        o.Property(x => x.IdAnuncio).HasMaxLength(OrigemAnuncio.TamanhoIdAnuncio);
        o.Property(x => x.TituloAnuncio).HasMaxLength(OrigemAnuncio.TamanhoTexto);
        o.Property(x => x.TipoIdClique).HasMaxLength(OrigemAnuncio.TamanhoTipoIdClique);
        o.Property(x => x.IdClique).HasMaxLength(OrigemAnuncio.TamanhoIdClique);
        o.Property(x => x.CapturadaEm).HasColumnType("timestamp with time zone");
    }
}
