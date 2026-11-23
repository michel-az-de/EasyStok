using System.Text.Json.Serialization;

namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// De onde vem o modelo de mensagem (#3205). Meta: aprovado na conta da Meta, sai como template. Local: mensagem padrão
/// do EasyStok (#3204), sai como texto renderizado pela ponte. Gravado como texto no <c>ModeloJson</c>; registro antigo
/// sem o campo é Meta.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrigemModelo
{
    Meta = 0,
    Local = 1
}
