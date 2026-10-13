using EasyStock.Application.Ports.Output.Integrations;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

public sealed record ConfiguracaoLogistica(Guid EmpresaDemonstracaoId, FreteEndereco Origem);
public sealed record CotarLogisticaInput(IReadOnlyList<Guid> PedidoIds, bool Agrupar, int? ValorManualCentavos = null, bool IncluirManual = false);
public sealed record ContratarLogisticaInput(Guid CotacaoId, string ChaveIdempotencia);
public sealed record PagamentoLogisticaInput(string ChaveIdempotencia, string Meio, string Referencia);
public sealed record EventoLogisticaInput(string Tipo, string? Motivo, string ChaveIdempotencia);
public sealed record PoliticaLogisticaInput(bool Automatico, int TetoCentavos, bool PermitirAgrupar);
public sealed record LogisticaPedido(Guid Id, string Numero, string Cliente, string Endereco, string Bairro,
    DateTime JanelaInicio, DateTime JanelaFim, string Status, int FreteCentavos,
    string Telefone, double? Latitude, double? Longitude, bool ClienteBloqueado);
public sealed record LogisticaEvento(string Tipo, string? Motivo, DateTime Em, string ChaveIdempotencia);
public sealed record CotacaoLogisticaResult(Guid Id, string Provedor, int ValorCentavos, int DuracaoMinutos,
    DateTime ExpiraEm, bool Simulado, string? Indisponibilidade, IReadOnlyList<Guid> PedidoIds);
public sealed record DespachoLogisticaResult(Guid Id, string Provedor, string Status, int ValorCentavos,
    bool Pago, bool Simulado, string? RastreioUrl, IReadOnlyList<Guid> PedidoIds, DateTime CriadoEm,
    IReadOnlyList<LogisticaEvento> Eventos, Guid ViagemId);
public sealed record ContextoLogisticaResult(bool Demonstracao, PoliticaLogisticaInput Politica,
    IReadOnlyList<LogisticaPedido> Pedidos, IReadOnlyList<DespachoLogisticaResult> Despachos);
