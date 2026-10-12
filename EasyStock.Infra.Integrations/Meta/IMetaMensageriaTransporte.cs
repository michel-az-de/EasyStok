namespace EasyStock.Infra.Integrations.Meta;

/// <summary>Leva um payload da Send API até a Meta e devolve o <c>message_id</c>.</summary>
public interface IMetaMensageriaTransporte
{
    Task<string> EnviarAsync(object payload, CancellationToken ct = default);
}

/// <summary>A Meta recusou o envio. <see cref="Codigo"/> e <see cref="Subcodigo"/> são os dela (ex.: 10 / 2018278 = fora da janela).</summary>
public sealed class MetaMensageriaException(int codigo, int? subcodigo, string mensagem) : Exception(mensagem)
{
    public int Codigo { get; } = codigo;
    public int? Subcodigo { get; } = subcodigo;
}