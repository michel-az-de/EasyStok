namespace EasyStock.Application.Ports.Output.Integrations;

/// <summary>Um ponto da corrida: coordenada e o texto do endereço que o motorista lê.</summary>
public sealed record PontoCotacaoFrete(double Lat, double Lng, string Endereco);

/// <summary>Preço de uma corrida cotada no provedor, já em centavos, sem arredondamento da loja.</summary>
public sealed record CotacaoFreteExterna(string Provedor, int ValorCentavos, int? DistanciaMetros);

/// <summary>
/// Cotação do frete num provedor de entrega com a chave da própria loja (#1882, fatia de cotação da F17).
/// Só cota: não chama motorista. Devolve <see langword="null"/> quando a loja não tem chave de produção ou o
/// provedor não cotou (fora da área, erro, timeout, sem saldo): o frete volta à tabela da loja e o pedido
/// nunca trava por causa do provedor.
/// </summary>
public interface ICotacaoFreteExterna
{
    Task<CotacaoFreteExterna?> CotarAsync(Guid empresaId, PontoCotacaoFrete coleta, PontoCotacaoFrete entrega, CancellationToken ct = default);
}
