namespace EasyStock.Application.Ports.Output.Integrations;

public sealed record FreteEndereco(string Nome, string Endereco, string Telefone, double Latitude, double Longitude);
public sealed record FretePedido(Guid PedidoId, FreteEndereco Destino, DateTime JanelaInicioUtc, DateTime JanelaFimUtc);
public sealed record FreteSolicitacao(Guid EmpresaId, FreteEndereco Origem, IReadOnlyList<FretePedido> Pedidos, string ChaveIdempotencia);
public sealed record FreteCotacao(string Provedor, string Referencia, int ValorCentavos, int DuracaoMinutos, DateTime ExpiraEmUtc, bool Simulado, string? Indisponibilidade);
public sealed record FreteContratacao(string Referencia, string? RastreioUrl, string Status, bool Simulado);

/// <summary>Falha recuperável do provedor, com orientação segura para apresentar ao operador.</summary>
public sealed class FreteProviderException(string mensagem) : Exception(mensagem);

public interface IFreteProvider
{
    string Provedor { get; }
    Task<FreteCotacao> CotarAsync(FreteSolicitacao solicitacao, CancellationToken ct);
    Task<FreteContratacao> ContratarAsync(FreteSolicitacao solicitacao, string referenciaCotacao, CancellationToken ct);
}
