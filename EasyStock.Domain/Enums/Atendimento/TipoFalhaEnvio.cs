namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>S57 (#1355): o que fazer depois de uma falha de envio ao cliente.</summary>
public enum TipoFalhaEnvio
{
    /// <summary>Canal fora, rede, limite de taxa: reenviar sozinho mais tarde.</summary>
    Temporaria = 1,

    /// <summary>Número inválido ou fora da lista, janela de 24 h vencida: insistir não adianta.</summary>
    Permanente = 2,

    /// <summary>Sem resposta do canal (timeout): pode ter saído; reenviar sozinho duplicaria (#1292).</summary>
    Incerta = 3
}
