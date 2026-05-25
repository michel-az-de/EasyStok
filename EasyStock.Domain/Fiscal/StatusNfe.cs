namespace EasyStock.Domain.Fiscal;

/// <summary>
/// Estados possiveis de um <see cref="NfeDocumento"/> ao longo do ciclo
/// emissao -> autorizacao -> cancelamento. Maquina de estados aplicada em
/// <see cref="NfeDocumento"/>: ver metodos MarcarEnviada, MarcarAutorizada,
/// MarcarRejeitada, Cancelar, MarcarInutilizada.
/// </summary>
public enum StatusNfe
{
    /// <summary>Criado mas ainda nao enviado a SEFAZ. Pode ser editado/descartado.</summary>
    Rascunho = 1,

    /// <summary>Enviado a SEFAZ, aguardando retorno (sincrono ou assincrono).</summary>
    EnviadaAguardandoRetorno = 2,

    /// <summary>SEFAZ autorizou. Possui ChaveAcesso e ProtocoloAutorizacao.</summary>
    Autorizada = 3,

    /// <summary>SEFAZ rejeitou. MotivoRejeicao preenchido. Pode ser corrigido e reenviado como novo documento.</summary>
    Rejeitada = 4,

    /// <summary>Cancelado apos autorizacao (dentro da janela legal de cancelamento — 30min para NFC-e).</summary>
    Cancelada = 5,

    /// <summary>Numero inutilizado por quebra de sequencia (sem que tenha sido emitido). Operacao formal SEFAZ.</summary>
    Inutilizada = 6,

    /// <summary>Falha transiente (timeout SEFAZ, erro de rede). Caller deve reagendar reenvio.</summary>
    FalhaTransiente = 7,
}
