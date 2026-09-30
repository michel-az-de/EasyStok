namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// O provedor do canal recusou ou não completou o envio (S37). <see cref="FalhaPermanente"/> =
/// true não deve ser reenviado automaticamente (ex.: número inválido).
/// </summary>
public sealed class EnvioCanalFalhouException(string mensagem, bool falhaPermanente)
    : Exception(mensagem)
{
    public bool FalhaPermanente { get; } = falhaPermanente;
}
