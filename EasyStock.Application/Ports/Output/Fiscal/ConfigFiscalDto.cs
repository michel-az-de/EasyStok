using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Configuração fiscal resolvida no inicio do use case e passada ao
/// gateway. Contém dados do emitente (Empresa+Loja), credenciais
/// resolvidas e ambiente. Não trafega entre processos — vive só em
/// memória dentro do request.
/// </summary>
public sealed record ConfigFiscalDto(
    Guid EmpresaId,
    Guid LojaId,
    AmbienteSefaz Ambiente,
    int Serie,
    string CnpjEmitente,
    string InscricaoEstadualEmitente,
    string NomeEmitente,
    string UfEmitente,
    string UfCodigoIbge,
    string CepEmitente,
    string LogradouroEmitente,
    string NumeroEnderecoEmitente,
    string? ComplementoEnderecoEmitente,
    string BairroEmitente,
    string MunicipioEmitente,
    string MunicipioCodigoIbge,
    RegimeTributario RegimeTributario,
    string TokenFocus,
    string? CscId,
    string? Csc,
    string? WebhookSecret);
