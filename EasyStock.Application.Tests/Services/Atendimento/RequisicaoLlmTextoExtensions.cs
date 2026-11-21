using EasyStock.Application.Ports.Output.Ai;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #2862: o system chega em duas partes (estável e volátil). O texto que o modelo recebe é a soma das duas, na ordem de sempre.
/// </summary>
internal static class RequisicaoLlmTextoExtensions
{
    internal static string TextoCompleto(this RequisicaoLlm requisicao) => requisicao.System + requisicao.SystemVolatil;
}
