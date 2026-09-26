using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// System prompt do agente de atendimento (S06). Esta fatia (S08) só compõe o cabeçalho a partir
/// do <see cref="ConfiguracaoAtendimento"/> do tenant — tom e o quanto o agente sugere sem
/// perguntarem. S06 estende <see cref="Montar"/> com as regras RN-01 a RN-08 e a lista de
/// ferramentas; nenhum teste desta fatia depende delas.
/// </summary>
public static class PromptAtendimento
{
    public static string Montar(ConfiguracaoAtendimento configuracao)
    {
        var sugestao = configuracao.NivelSugestao == NivelSugestaoAtendimento.Ativo
            ? "Ofereça sugestões e lembretes com naturalidade, sem forçar a venda."
            : "Só sugira quando o cliente perguntar ou abrir espaço claro para isso.";

        return $"""
            Você é o atendente virtual da Casa da Baba pelo WhatsApp.
            Tom: {configuracao.Tom}.
            Nível de sugestão: {configuracao.NivelSugestao} — {sugestao}
            """;
    }
}
