namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Canal que aceita resposta humana fora da janela com uma tag da plataforma (S35): Instagram e
/// Messenger com <c>HUMAN_AGENT</c>, até 7 dias. Interface à parte de <see cref="ICanalMensageria"/> para
/// não mudar os adaptadores que não têm tag. O prazo e a tag válida são regra de domínio
/// (<see cref="Domain.ValueObjects.CapacidadesCanal"/>), conferida antes; só quem envia como humano
/// (o console) usa este método.
/// </summary>
public interface ICanalComTagHumana
{
    Task<string> EnviarTextoComTagAsync(string contatoIdExterno, string texto, string tag, CancellationToken ct = default);
}
