using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #3068: prazo que a equipe tem para responder antes de o Assistente retomar a conversa. Configuração em
/// <c>Atendimento:RetomadaAssistente</c>; <see cref="Segundos"/> zero ou negativo desliga a retomada.
/// </summary>
public sealed class RetomadaAssistenteOptions
{
    public const string Secao = "Atendimento:RetomadaAssistente";
    public const int PadraoSegundos = 60;
    public const int PadraoVarreduraSegundos = 15;
    public const int PadraoJanelaMinutos = 30;

    public int Segundos { get; set; } = PadraoSegundos;

    /// <summary>Intervalo da varredura que procura conversa parada com a equipe.</summary>
    public int VarreduraSegundos { get; set; } = PadraoVarreduraSegundos;

    /// <summary>Espera máxima que a varredura ainda retoma; além dela a conversa é velha e fica com a equipe.</summary>
    public int JanelaMinutos { get; set; } = PadraoJanelaMinutos;

    public TimeSpan Prazo => TimeSpan.FromSeconds(Math.Max(0, Segundos));
    public TimeSpan Janela => TimeSpan.FromMinutes(Math.Max(0, JanelaMinutos));

    public void Ler(IConfiguration configuracao)
    {
        var secao = configuracao.GetSection(Secao);
        if (int.TryParse(secao[nameof(Segundos)], out var segundos)) Segundos = segundos;
        if (int.TryParse(secao[nameof(VarreduraSegundos)], out var varredura)) VarreduraSegundos = varredura;
        if (int.TryParse(secao[nameof(JanelaMinutos)], out var janela)) JanelaMinutos = janela;
    }
}

/// <summary>
/// #3068: quando a conversa que está com a equipe volta ao Assistente por tempo. Uma regra só, usada pelo turno: a
/// varredura só indica candidatas. Volta quando o cliente espera (escreveu e ninguém respondeu, ou o robô passou a
/// conversa para a equipe) há mais que o prazo, sem resposta humana depois da última mensagem dele, uma vez por mensagem.
/// </summary>
public static class RetomadaDoAssistente
{
    public const string PrefixoNota = "Assistente retomou: ";

    /// <summary>Marca do bloco de orientação no prompt do turno, para o teste e para quem lê o histórico.</summary>
    public const string MarcaNoPrompt = "Retomada do atendimento";

    public static string Nota(TimeSpan prazo, string? motivoAnterior) =>
        $"{PrefixoNota}a equipe não respondeu em {prazo.TotalSeconds:0} s"
        + (string.IsNullOrWhiteSpace(motivoAnterior) ? "." : $" (estava com a equipe por: {motivoAnterior}).");

    /// <summary>
    /// A entrada do cliente que ficou sem resposta da equipe além do prazo; nulo quando não cabe retomar.
    /// <paramref name="historico"/> vem em ordem de envio, como o turno o carrega.
    /// </summary>
    public static Mensagem? EntradaSemResposta(Conversa conversa, IReadOnlyList<Mensagem> historico, DateTime agora, TimeSpan prazo)
    {
        if (prazo <= TimeSpan.Zero || conversa.Situacao != SituacaoConversa.Assumida || !conversa.TemAgente)
            return null;

        var indice = -1;
        for (var i = historico.Count - 1; i >= 0 && indice < 0; i--)
            if (historico[i].Autor == AutorMensagem.Cliente) indice = i;
        if (indice < 0) return null;

        var entrada = historico[indice];
        if (conversa.RetomadaPorTempoDecidida(entrada.Id)) return null;

        var depois = historico.Skip(indice + 1).Where(m => m.Status != StatusMensagem.Falhou).ToList();
        if (depois.Any(m => m.Autor.EhHumanoDaEmpresa())) return null;

        // Resposta do robô sem escalada depois dela: o cliente foi atendido e a equipe só assumiu a conversa.
        var escalada = depois.Any(m => m.Autor == AutorMensagem.Sistema
            && m.Texto?.StartsWith(EscalarConversaUseCase.PrefixoMotivo, StringComparison.Ordinal) == true);
        if (depois.Any(m => m.Autor == AutorMensagem.Agente) && !escalada) return null;

        // O relógio é o da última fala de gente ou do robô. Nota interna do sistema não o reinicia; a que tem autor
        // (reabertura feita pela equipe) reinicia.
        var ultimaFala = historico.Skip(indice)
            .Where(m => m.Status != StatusMensagem.Falhou && (m.Autor != AutorMensagem.Sistema || m.EnviadaPorUsuarioId is not null))
            .Max(m => m.EnviadaEm);
        return agora - ultimaFala >= prazo ? entrada : null;
    }

    /// <summary>
    /// Orientação do turno retomado. Sem futuro sobre a equipe ("a equipe vai..."): a guarda da resposta barra promessa de
    /// encaminhamento que nenhuma ferramenta do turno sustenta, e a escalada já aconteceu antes.
    /// </summary>
    public static string Orientacao(TimeSpan prazo) =>
        $"\n\n{MarcaNoPrompt}: a conversa estava com a equipe, que não respondeu em {prazo.TotalSeconds:0} segundos, e voltou para você. "
        + "O cliente continua esperando. Responda agora à última mensagem dele: diga em uma frase que a equipe já foi avisada e que "
        + "você segue com ele por aqui, resolva o que as ferramentas permitirem e peça só o dado que falta. "
        + "Não use escalar_para_dona nem encaminhar_para_responsavel de novo pelo mesmo motivo, e não diga que a equipe vai responder, "
        + "ligar ou resolver, nem dê prazo. Se o assunto depender só da equipe (dinheiro, cancelamento, reclamação), acolha, anote o "
        + "que o cliente quer com registrar_nota quando a ferramenta existir e continue disponível.";
}
