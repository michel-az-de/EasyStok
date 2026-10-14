namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Marca um adapter que simula um provedor externo (I3.1, #1667; invariantes 1 e 2 do plano de
/// integrações). Simulador nunca é construído em Production e devolve desfecho <c>Simulado</c>, nunca
/// o de sucesso do provedor real.
/// </summary>
public interface ISimulador;