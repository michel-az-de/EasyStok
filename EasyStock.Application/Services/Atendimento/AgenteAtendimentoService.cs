using System.Text;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>O que o turno fez: útil para log e teste.</summary>
public sealed record ResultadoTurnoAgente(bool ChamouLlm, bool Respondeu, bool Escalou)
{
    public static readonly ResultadoTurnoAgente Ignorado = new(false, false, false);
}

/// <summary>
/// Turno do agente de atendimento (S06): monta o contexto (últimas 20 mensagens, dossiê do cliente,
/// notas <c>[interno]</c>, tom de S08), chama o LLM com as ferramentas, executa as ferramentas pedidas
/// e repete até a resposta final, no máximo <see cref="MaximoIteracoes"/> chamadas. Grava a resposta
/// como <c>Mensagem(Saida, Agente)</c>, envia pela Cloud API e registra o consumo em <c>UsoIa</c>.
///
/// <para>Guardas: LLM indisponível (<c>Anthropic:Enabled=false</c> ou sem chave), conversa que não está
/// <see cref="SituacaoConversa.Automatica"/> (RN-04), fora da janela de 24 h, ou última mensagem que não é
/// do cliente (job repetido) → não chama o LLM nem envia nada.</para>
/// </summary>
public sealed class AgenteAtendimentoService(
    IAgenteLlmClient llm,
    IConversaRepository conversaRepository,
    IConfiguracaoAtendimentoRepository configuracaoRepository,
    IClienteRepository clienteRepository,
    IEnumerable<IFerramentaAgente> ferramentas,
    IEscaladorConversa escalador,
    IWhatsAppCloudClient cloudClient,
    IUsoIaRepository usoIaRepository,
    IUnitOfWork unitOfWork,
    ILogger<AgenteAtendimentoService> logger)
{
    public const int MaximoIteracoes = 6;
    public const int UltimasMensagens = 20;

    private readonly IReadOnlyDictionary<string, IFerramentaAgente> _ferramentas =
        ferramentas.ToDictionary(f => f.Nome, StringComparer.Ordinal);

    public async Task<ResultadoTurnoAgente> ProcessarTurnoAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct = default)
    {
        if (!llm.Disponivel)
        {
            logger.LogInformation("Agente de atendimento desligado (Anthropic): conversa {ConversaId} fica para a dona.", conversaId);
            return ResultadoTurnoAgente.Ignorado;
        }

        var dados = await conversaRepository.ObterComMensagensAsync(empresaId, conversaId, UltimasMensagens, ct);
        if (dados is null) return ResultadoTurnoAgente.Ignorado;

        var conversa = dados.Conversa;
        if (conversa.Situacao != SituacaoConversa.Automatica || !conversa.DentroDaJanela(agora))
            return ResultadoTurnoAgente.Ignorado;

        // O agente envia pelo cliente do WhatsApp: em outro canal a resposta sairia pelo canal errado.
        // Instagram, Messenger e chat do site ficam com o humano até o agente enviar pela porta de canal.
        if (conversa.Canal != CanalConversa.WhatsApp)
        {
            logger.LogInformation("Agente: conversa {ConversaId} é do canal {Canal}; fica para a dona.", conversaId, conversa.Canal);
            return ResultadoTurnoAgente.Ignorado;
        }

        var mensagens = MontarConversacao(dados.Mensagens);
        if (mensagens.Count == 0 || mensagens[^1].Papel != MensagemLlm.Usuario)
            return ResultadoTurnoAgente.Ignorado;

        var configuracao = await configuracaoRepository.GetByEmpresaIdAsync(empresaId)
            ?? ConfiguracaoAtendimento.CriarPadrao(empresaId);
        var cliente = conversa.ClienteId is { } clienteId
            ? await clienteRepository.GetByIdAsync(empresaId, clienteId)
            : null;

        // S24: bloqueado no meio da conversa → a dona assume; o agente não responde.
        if (cliente?.Bloqueado == true)
        {
            await escalador.EscalarAsync(empresaId, conversa, EscalarConversaUseCase.MotivoClienteBloqueado(cliente), agora, ct);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true);
        }

        var system = PromptAtendimento.Montar(configuracao) + "\n\n" + MontarDossie(conversa, cliente, dados.Mensagens, agora);
        var definicoes = _ferramentas.Values
            .Select(f => new FerramentaLlm(f.Nome, f.Descricao, f.SchemaJson))
            .ToList();
        var contexto = new ContextoTurnoAgente(empresaId, conversa, agora);

        var tokens = 0;
        string? textoFinal = null;
        string? motivoEscalada = null;
        var concluiu = false;

        for (var iteracao = 0; iteracao < MaximoIteracoes; iteracao++)
        {
            RespostaLlm resposta;
            try
            {
                resposta = await llm.EnviarAsync(new RequisicaoLlm(system, [.. mensagens], definicoes), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Agente de atendimento: falha chamando o LLM na conversa {ConversaId}.", conversaId);
                motivoEscalada = "o agente não conseguiu responder (falha ao chamar o LLM)";
                break;
            }

            tokens += resposta.TokensEntrada + resposta.TokensSaida;

            if (resposta.StopReason != RespostaLlm.StopToolUse)
            {
                concluiu = true;
                textoFinal = string.Join("\n", resposta.Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto)).Trim();
                if (textoFinal.Length == 0)
                    motivoEscalada = $"o agente não produziu resposta (stop_reason={resposta.StopReason})";
                break;
            }

            mensagens.Add(new MensagemLlm(MensagemLlm.Assistente, resposta.Conteudo));
            var resultados = new List<BlocoLlm>();
            foreach (var uso in resposta.Conteudo.OfType<BlocoUsoFerramentaLlm>())
                resultados.Add(await ExecutarFerramentaAsync(contexto, uso, ct));
            mensagens.Add(new MensagemLlm(MensagemLlm.Usuario, resultados));
        }

        var respondeu = false;
        if (!concluiu && motivoEscalada is null)
        {
            // Limite de iterações: avisa o cliente e passa para a dona.
            respondeu = await EnviarRespostaAsync(empresaId, conversa, configuracao.FraseEspera, agora, ct);
            motivoEscalada = $"o agente atingiu o limite de {MaximoIteracoes} iterações sem concluir a resposta";
        }
        else if (!string.IsNullOrWhiteSpace(textoFinal))
        {
            respondeu = await EnviarRespostaAsync(empresaId, conversa, textoFinal, agora, ct);
        }

        if (motivoEscalada is not null)
            await escalador.EscalarAsync(empresaId, conversa, motivoEscalada, agora, ct);

        if (tokens > 0)
            await RegistrarUsoAsync(empresaId, tokens, agora);

        await unitOfWork.CommitAsync();
        return new ResultadoTurnoAgente(true, respondeu,
            motivoEscalada is not null || conversa.Situacao == SituacaoConversa.Assumida);
    }

    private async Task<BlocoResultadoFerramentaLlm> ExecutarFerramentaAsync(
        ContextoTurnoAgente contexto, BlocoUsoFerramentaLlm uso, CancellationToken ct)
    {
        if (!_ferramentas.TryGetValue(uso.Nome, out var ferramenta))
            return new BlocoResultadoFerramentaLlm(uso.Id, $"Ferramenta desconhecida: {uso.Nome}.", EhErro: true);

        try
        {
            return new BlocoResultadoFerramentaLlm(uso.Id, await ferramenta.ExecutarAsync(contexto, uso.Entrada, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Agente de atendimento: ferramenta {Ferramenta} falhou na conversa {ConversaId}.",
                ferramenta.Nome, contexto.Conversa.Id);
            return new BlocoResultadoFerramentaLlm(uso.Id, "A ferramenta falhou; não invente o resultado.", EhErro: true);
        }
    }

    private async Task<bool> EnviarRespostaAsync(Guid empresaId, Conversa conversa, string texto, DateTime agora, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
        if (texto.Length > Mensagem.TextoTamanhoMaximo) texto = texto[..Mensagem.TextoTamanhoMaximo];

        Mensagem saida;
        var enviou = false;
        try
        {
            var envio = await cloudClient.EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct: ct);
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Agente, agora, TipoConteudoMensagem.Texto, texto, envio.Wamid);
            enviou = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Agente de atendimento: falha ao enviar a resposta da conversa {ConversaId}.", conversa.Id);
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Agente, agora, TipoConteudoMensagem.Texto, texto);
            saida.AtualizarStatusEntrega(StatusMensagem.Falhou, ex.Message);
        }

        // encerrar_conversa pode ter fechado a conversa neste turno: a despedida ainda vai para o histórico.
        if (conversa.EstaAberta) conversa.RegistrarSaida(agora);
        await conversaRepository.AddMensagemAsync(saida, ct);
        return enviou;
    }

    private async Task RegistrarUsoAsync(Guid empresaId, int tokens, DateTime agora)
    {
        var uso = await usoIaRepository.GetAsync(empresaId, agora.Year, agora.Month);
        if (uso is null)
        {
            await usoIaRepository.AddAsync(new UsoIa
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Ano = agora.Year,
                Mes = agora.Month,
                TotalGeracoes = 1,
                TotalTokens = tokens,
                AtualizadoEm = agora
            });
            return;
        }

        uso.TotalGeracoes++;
        uso.TotalTokens += tokens;
        uso.AtualizadoEm = agora;
        await usoIaRepository.UpdateAsync(uso);
    }

    /// <summary>
    /// Cliente → <c>user</c>; agente e dona → <c>assistant</c>. Mensagens do sistema não entram na
    /// conversa (vão para o dossiê). Mensagens seguidas do mesmo papel viram uma só.
    /// </summary>
    private static List<MensagemLlm> MontarConversacao(IReadOnlyList<Mensagem> historico)
    {
        var resultado = new List<MensagemLlm>();
        foreach (var msg in historico.Where(m => m.Autor != AutorMensagem.Sistema))
        {
            var papel = msg.Autor == AutorMensagem.Cliente ? MensagemLlm.Usuario : MensagemLlm.Assistente;
            var texto = msg.Autor switch
            {
                AutorMensagem.Cliente => TextoDoCliente(msg),
                AutorMensagem.Dona => $"(dona) {msg.Texto}",
                _ => msg.Texto ?? string.Empty
            };
            if (string.IsNullOrWhiteSpace(texto)) continue;

            if (resultado.Count > 0 && resultado[^1].Papel == papel)
                resultado[^1] = resultado[^1] with { Conteudo = [.. resultado[^1].Conteudo, new BlocoTextoLlm(texto)] };
            else
                resultado.Add(new MensagemLlm(papel, [new BlocoTextoLlm(texto)]));
        }

        // A API exige que a conversa comece pelo usuário.
        while (resultado.Count > 0 && resultado[0].Papel != MensagemLlm.Usuario)
            resultado.RemoveAt(0);

        return resultado;
    }

    private static string TextoDoCliente(Mensagem msg)
    {
        var marcador = msg.TipoConteudo switch
        {
            TipoConteudoMensagem.Imagem => "[imagem recebida]",
            TipoConteudoMensagem.Audio => "[áudio recebido]",
            TipoConteudoMensagem.Documento => "[documento recebido]",
            TipoConteudoMensagem.Localizacao => "[localização recebida]",
            TipoConteudoMensagem.Botao => $"[botão tocado: {msg.BotaoId}]",
            TipoConteudoMensagem.Outro => "[mensagem não suportada]",
            _ => null
        };

        if (marcador is null) return msg.Texto ?? string.Empty;
        return string.IsNullOrWhiteSpace(msg.Texto) ? marcador : $"{marcador} {msg.Texto}";
    }

    /// <summary>
    /// Dossiê do turno, depois do prompt fixo. Mensagem do sistema sem <c>wamid</c> nunca saiu para o
    /// cliente: é nota interna e entra marcada <see cref="PromptAtendimento.MarcadorInterno"/> (RN-08).
    /// </summary>
    private static string MontarDossie(Conversa conversa, Cliente? cliente, IReadOnlyList<Mensagem> historico, DateTime agora)
    {
        var interno = PromptAtendimento.MarcadorInterno;
        var sb = new StringBuilder();
        sb.AppendLine("Dossiê desta conversa (contexto para você, não para citar):");
        sb.AppendLine($"- Agora: {HorarioBrasil.ConverterParaBrasilia(agora):dd/MM/yyyy HH:mm} (horário de Brasília).");

        var nome = cliente?.Nome is { } n && n != IdentificarClientePorTelefoneUseCase.NomePadraoLead ? n : conversa.ContatoNome;
        sb.AppendLine($"- Nome: {(string.IsNullOrWhiteSpace(nome) ? "não informado" : nome)}.");
        sb.AppendLine(cliente is null || cliente.OrderCount == 0
            ? "- Situação: lead (ainda não comprou)."
            : $"- Situação: cliente com {cliente.OrderCount} pedido(s).");
        sb.AppendLine(conversa.PedidoEmAndamentoId is { } pedidoId
            ? $"- Pedido em andamento: {pedidoId}."
            : "- Pedido em andamento: nenhum registrado nesta conversa.");
        if (conversa.ContextoJson != "{}")
            sb.AppendLine($"- Contexto salvo: {conversa.ContextoJson}");
        if (!string.IsNullOrWhiteSpace(cliente?.Observacoes))
            sb.AppendLine($"- {interno} Observações do cadastro: {cliente.Observacoes}");

        var sistema = historico.Where(m => m.Autor == AutorMensagem.Sistema && !string.IsNullOrWhiteSpace(m.Texto)).ToList();
        var notas = sistema.Where(m => m.ExternoId is null).ToList();
        if (notas.Count > 0)
        {
            sb.AppendLine("Notas internas:");
            foreach (var nota in notas) sb.AppendLine($"- {interno} {nota.Texto}");
        }

        var automaticas = sistema.Where(m => m.ExternoId is not null).ToList();
        if (automaticas.Count > 0)
        {
            sb.AppendLine("Mensagens automáticas já enviadas ao cliente:");
            foreach (var auto in automaticas) sb.AppendLine($"- {auto.Texto}");
        }

        return sb.ToString().TrimEnd();
    }
}
