using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Mensagem de uma <see cref="Conversa"/> (S04, ADR-0050).
///
/// <para>
/// Entrada vem do cliente e ja chegou (nasce <see cref="StatusMensagem.Entregue"/>).
/// Saida e do agente, da dona ou do sistema: nasce <see cref="StatusMensagem.Pendente"/>
/// ate a Meta devolver o <c>wamid</c> (<see cref="ConfirmarEnvio"/>) e depois so avanca
/// de status; a Meta pode entregar "read" antes de "delivered" e um status menor
/// nunca regride o maior. <see cref="StatusMensagem.Falhou"/> e terminal e guarda o erro.
/// </para>
/// </summary>
public class Mensagem
{
    public const int TextoTamanhoMaximo = 4096;
    public const int ExternoIdTamanhoMaximo = 128;
    public const int BotaoIdTamanhoMaximo = 256;
    public const int MidiaChaveTamanhoMaximo = 300;
    public const int MidiaMimeTamanhoMaximo = 100;
    public const int ErroTamanhoMaximo = 500;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid ConversaId { get; private set; }

    /// <summary>Id da mensagem na Meta (<c>wamid</c>). Unico por empresa quando presente.</summary>
    public string? ExternoId { get; private set; }

    public DirecaoMensagem Direcao { get; private set; }
    public AutorMensagem Autor { get; private set; }
    public TipoConteudoMensagem TipoConteudo { get; private set; }
    public string? Texto { get; private set; }

    /// <summary>Id do botao interativo respondido (ex.: <c>acao:avaliacao:positiva:{pedidoId}</c>).</summary>
    public string? BotaoId { get; private set; }

    /// <summary>Chave da midia no storage interno (a URL da Meta expira em minutos).</summary>
    public string? MidiaChave { get; private set; }
    public string? MidiaMime { get; private set; }

    public StatusMensagem Status { get; private set; }
    public string? Erro { get; private set; }
    public DateTime EnviadaEm { get; private set; }

    /// <summary>Saiu de uma mensagem programada (S39): o console mostra o selo "programada".</summary>
    public bool Programada { get; private set; }
    public DateTime? ProcessadaEm { get; private set; }

    /// <summary>Usuario do console que enviou a mensagem (S41). So em saida humana (<see cref="AutorMensagem.Dona"/>).</summary>
    public Guid? EnviadaPorUsuarioId { get; private set; }

    /// <summary>S57 (#1355): esperas do reenvio automático depois de cada falha temporária.</summary>
    public static readonly TimeSpan[] EsperasReenvio =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];

    /// <summary>Depois disto, contado do primeiro envio, a mensagem não é mais reenviada sozinha.</summary>
    public static readonly TimeSpan PrazoReenvio = TimeSpan.FromHours(6);

    /// <summary>Tentativas de envio que falharam (S57).</summary>
    public int TentativasEnvio { get; private set; }

    /// <summary>Quando o reenvio automático tenta de novo; nulo quando não há reenvio agendado (S57).</summary>
    public DateTime? ProximoReenvioEm { get; private set; }

    /// <summary>Tipo da última falha de envio (S57); decide se a reserva por SMS cabe (S60).</summary>
    public TipoFalhaEnvio? UltimaFalhaEnvio { get; private set; }

    /// <summary>
    /// S58 (#1391): fora da janela de 24 h, o modelo de retomada saiu e a mensagem espera o cliente responder.
    /// Nulo quando não espera.
    /// </summary>
    public DateTime? AguardaClienteDesde { get; private set; }

    /// <summary>#1397: tentativas de baixar o anexo antes de desistir.</summary>
    public const int MaxTentativasMidia = 3;
    public const int MidiaIdExternoTamanhoMaximo = 200;
    public const int ErroMidiaTamanhoMaximo = 500;

    /// <summary>#1397: esperas entre as tentativas de baixar o anexo (depois da 1ª e da 2ª falha).</summary>
    public static readonly TimeSpan[] EsperasMidia = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)];

    /// <summary>
    /// #1397: folga dada à fila em memória (ou ao processo que reservou) antes de a varredura assumir o anexo.
    /// Se a API reiniciar no meio, a varredura retoma depois deste prazo.
    /// </summary>
    public static readonly TimeSpan PrazoFilaMidia = TimeSpan.FromMinutes(2);

    /// <summary>#1397: id da mídia na Cloud API, guardado para a varredura baixar de novo depois de um restart.</summary>
    public string? MidiaIdExterno { get; private set; }

    /// <summary>#1397: tentativas de baixar o anexo que falharam.</summary>
    public int TentativasMidia { get; private set; }

    /// <summary>#1397: quando a varredura tenta baixar o anexo; nulo quando não há anexo pendente.</summary>
    public DateTime? ProximaTentativaMidiaEm { get; private set; }

    /// <summary>#1397: erro da última tentativa de baixar o anexo.</summary>
    public string? ErroMidia { get; private set; }

    /// <summary>#1397: o anexo não veio e não haverá nova tentativa.</summary>
    public bool MidiaFalhou => MidiaChave is null && TentativasMidia >= MaxTentativasMidia;

    /// <summary>S60: quando o texto saiu pela reserva por SMS; nulo se não saiu.</summary>
    public DateTime? ReservaSmsEm { get; private set; }

    /// <summary>
    /// S60: o WhatsApp desistiu (sem reenvio agendado, sem esperar o cliente), a falha não é incerta e o SMS
    /// ainda não saiu.
    /// </summary>
    public bool PrecisaDeReservaSms =>
        PodeReenviar && ProximoReenvioEm is null && AguardaClienteDesde is null && ReservaSmsEm is null
        && UltimaFalhaEnvio is TipoFalhaEnvio.Temporaria or TipoFalhaEnvio.Permanente;

    /// <summary>Texto que falhou e pode ser reenviado (automático ou pelo console).</summary>
    public bool PodeReenviar =>
        Direcao == DirecaoMensagem.Saida && Status == StatusMensagem.Falhou
        && TipoConteudo == TipoConteudoMensagem.Texto && !string.IsNullOrWhiteSpace(Texto);

    // EF Core ctor sem parametros
    private Mensagem() { }

    /// <summary>Mensagem recebida do cliente pelo webhook.</summary>
    public static Mensagem Entrada(
        Guid empresaId,
        Guid conversaId,
        DateTime em,
        TipoConteudoMensagem tipoConteudo,
        string? texto = null,
        string? externoId = null,
        string? botaoId = null)
    {
        var msg = Criar(empresaId, conversaId, em, DirecaoMensagem.Entrada, AutorMensagem.Cliente, tipoConteudo, texto, externoId, botaoId);
        msg.Status = StatusMensagem.Entregue;
        return msg;
    }

    /// <summary>Mensagem nossa. Com <paramref name="externoId"/> ja nasce enviada; sem, fica pendente ate <see cref="ConfirmarEnvio"/>.</summary>
    public static Mensagem Saida(
        Guid empresaId,
        Guid conversaId,
        AutorMensagem autor,
        DateTime em,
        TipoConteudoMensagem tipoConteudo,
        string? texto = null,
        string? externoId = null)
    {
        if (autor == AutorMensagem.Cliente)
            throw new RegraDeDominioVioladaException("Mensagem de saida nao pode ter o cliente como autor.");

        var msg = Criar(empresaId, conversaId, em, DirecaoMensagem.Saida, autor, tipoConteudo, texto, externoId, botaoId: null);
        msg.Status = externoId is null ? StatusMensagem.Pendente : StatusMensagem.Enviada;
        return msg;
    }

    /// <summary>A Meta aceitou a mensagem e devolveu o <c>wamid</c>.</summary>
    public void ConfirmarEnvio(string externoId)
    {
        if (Direcao != DirecaoMensagem.Saida)
            throw new RegraDeDominioVioladaException("Confirmacao de envio so se aplica a mensagem de saida.");
        ExternoId = NormalizarExternoId(externoId)
            ?? throw new RegraDeDominioVioladaException("ExternoId e obrigatorio para confirmar o envio.");
        if (Status == StatusMensagem.Pendente)
            Status = StatusMensagem.Enviada;
    }

    /// <summary>Callback de status da Meta. Nunca regride; <see cref="StatusMensagem.Falhou"/> e terminal.</summary>
    public void AtualizarStatusEntrega(StatusMensagem novo, string? erro = null)
    {
        if (Direcao != DirecaoMensagem.Saida)
            throw new RegraDeDominioVioladaException("Status de entrega so se aplica a mensagem de saida.");

        if (novo == StatusMensagem.Falhou)
        {
            Status = StatusMensagem.Falhou;
            Erro = Truncar(erro, ErroTamanhoMaximo);
            return;
        }

        if (Status == StatusMensagem.Falhou) return;
        if (novo > Status) Status = novo;
    }

    /// <summary>
    /// S57: falha de envio. Temporária agenda o reenvio pela próxima espera de <see cref="EsperasReenvio"/>,
    /// dentro do <see cref="PrazoReenvio"/>; permanente e incerta não agendam (a incerta avisa no erro).
    /// </summary>
    public void RegistrarFalhaEnvio(string? erro, TipoFalhaEnvio tipo, DateTime agora)
    {
        var motivo = tipo == TipoFalhaEnvio.Incerta ? $"Envio incerto (sem resposta do canal): {erro}" : erro;
        AtualizarStatusEntrega(StatusMensagem.Falhou, motivo);
        TentativasEnvio++;
        UltimaFalhaEnvio = tipo;

        var podeAgendar = tipo == TipoFalhaEnvio.Temporaria
            && TentativasEnvio <= EsperasReenvio.Length
            && agora - EnviadaEm <= PrazoReenvio;
        ProximoReenvioEm = podeAgendar ? agora + EsperasReenvio[TentativasEnvio - 1] : null;
    }

    /// <summary>S57: tira do agendamento antes de enviar, para outro processo não pegar a mesma mensagem.</summary>
    public void ReservarReenvio() => ProximoReenvioEm = null;

    /// <summary>S57: o reenvio saiu. Única saída de <see cref="StatusMensagem.Falhou"/>.</summary>
    public void RegistrarReenviada(string externoId)
    {
        if (!PodeReenviar)
            throw new RegraDeDominioVioladaException("Só texto que falhou pode ser reenviado.");
        ExternoId = Truncar(externoId, ExternoIdTamanhoMaximo);
        Status = StatusMensagem.Enviada;
        Erro = null;
        ProximoReenvioEm = null;
        AguardaClienteDesde = null;
    }

    /// <summary>S58: o modelo de retomada saiu; a mensagem espera o cliente responder para reabrir a janela.</summary>
    public void AguardarCliente(DateTime agora, string motivo)
    {
        if (!PodeReenviar)
            throw new RegraDeDominioVioladaException("Só texto que falhou pode esperar o cliente.");
        AguardaClienteDesde = Utc(agora);
        ProximoReenvioEm = null;
        Erro = Truncar(motivo, ErroTamanhoMaximo);
    }

    /// <summary>S58: o cliente respondeu e a janela reabriu: reenvio na hora.</summary>
    public void LiberarAposResposta(DateTime agora)
    {
        if (AguardaClienteDesde is null)
            throw new RegraDeDominioVioladaException("Mensagem não está esperando o cliente.");
        AguardaClienteDesde = null;
        ProximoReenvioEm = Utc(agora);
    }

    /// <summary>S60: o texto saiu por SMS. Uma vez por mensagem.</summary>
    public void RegistrarReservaSms(DateTime agora)
    {
        if (!PrecisaDeReservaSms)
            throw new RegraDeDominioVioladaException("Reserva por SMS não cabe nesta mensagem.");
        ReservaSmsEm = Utc(agora);
    }

    public void MarcarComoProgramada()
    {
        if (Direcao != DirecaoMensagem.Saida)
            throw new RegraDeDominioVioladaException("So mensagem de saida pode ser programada.");
        Programada = true;
    }

    public void AnexarMidia(string chave, string mime)
    {
        if (string.IsNullOrWhiteSpace(chave))
            throw new RegraDeDominioVioladaException("Chave da midia e obrigatoria.");
        if (string.IsNullOrWhiteSpace(mime))
            throw new RegraDeDominioVioladaException("Mime da midia e obrigatorio.");
        MidiaChave = Truncar(chave.Trim(), MidiaChaveTamanhoMaximo);
        MidiaMime = Truncar(mime.Trim(), MidiaMimeTamanhoMaximo);
        ProximaTentativaMidiaEm = null;
        ErroMidia = null;
    }

    /// <summary>#1397: anexo a baixar; fica no banco para sobreviver a restart da fila em memória.</summary>
    public void AguardarMidia(string mediaId, DateTime agora)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
            throw new RegraDeDominioVioladaException("Id da midia e obrigatorio.");
        MidiaIdExterno = Truncar(mediaId.Trim(), MidiaIdExternoTamanhoMaximo);
        ProximaTentativaMidiaEm = Utc(agora) + PrazoFilaMidia;
    }

    /// <summary>#1397: a varredura assumiu o anexo; outro processo não o pega dentro do prazo.</summary>
    public void ReservarTentativaMidia(DateTime agora) => ProximaTentativaMidiaEm = Utc(agora) + PrazoFilaMidia;

    /// <summary>#1397: falha ao baixar o anexo. Reagenda com espera crescente até <see cref="MaxTentativasMidia"/>.</summary>
    public void RegistrarFalhaMidia(string? erro, DateTime agora)
    {
        TentativasMidia++;
        ErroMidia = Truncar(string.IsNullOrWhiteSpace(erro) ? "Falha ao baixar o anexo." : erro.Trim(), ErroMidiaTamanhoMaximo);
        ProximaTentativaMidiaEm = TentativasMidia < MaxTentativasMidia
            ? Utc(agora) + EsperasMidia[Math.Min(TentativasMidia, EsperasMidia.Length) - 1]
            : null;
    }

    public void MarcarProcessada(DateTime em) => ProcessadaEm = Utc(em);

    public void RegistrarEnviadaPor(Guid usuarioId)
    {
        if (Autor != AutorMensagem.Dona)
            throw new RegraDeDominioVioladaException("So mensagem enviada por humano da empresa guarda quem enviou.");
        if (usuarioId == Guid.Empty)
            throw new RegraDeDominioVioladaException("Usuario que enviou e obrigatorio.");
        EnviadaPorUsuarioId = usuarioId;
    }

    private static Mensagem Criar(
        Guid empresaId,
        Guid conversaId,
        DateTime em,
        DirecaoMensagem direcao,
        AutorMensagem autor,
        TipoConteudoMensagem tipoConteudo,
        string? texto,
        string? externoId,
        string? botaoId)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId e obrigatorio.");
        if (conversaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("ConversaId e obrigatorio.");

        var textoLimpo = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        if (tipoConteudo == TipoConteudoMensagem.Texto && textoLimpo is null)
            throw new RegraDeDominioVioladaException("Mensagem de texto precisa de texto.");
        if (textoLimpo is { Length: > TextoTamanhoMaximo })
            throw new RegraDeDominioVioladaException($"Texto excede {TextoTamanhoMaximo} caracteres.");

        var botaoLimpo = string.IsNullOrWhiteSpace(botaoId) ? null : botaoId.Trim();
        if (tipoConteudo == TipoConteudoMensagem.Botao && botaoLimpo is null)
            throw new RegraDeDominioVioladaException("Resposta de botao precisa do id do botao.");
        if (botaoLimpo is { Length: > BotaoIdTamanhoMaximo })
            throw new RegraDeDominioVioladaException($"BotaoId excede {BotaoIdTamanhoMaximo} caracteres.");

        return new Mensagem
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ConversaId = conversaId,
            ExternoId = NormalizarExternoId(externoId),
            Direcao = direcao,
            Autor = autor,
            TipoConteudo = tipoConteudo,
            Texto = textoLimpo,
            BotaoId = botaoLimpo,
            EnviadaEm = Utc(em),
        };
    }

    private static string? NormalizarExternoId(string? externoId)
    {
        if (string.IsNullOrWhiteSpace(externoId)) return null;
        var limpo = externoId.Trim();
        if (limpo.Length > ExternoIdTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"ExternoId excede {ExternoIdTamanhoMaximo} caracteres.");
        return limpo;
    }

    private static string? Truncar(string? valor, int max) =>
        valor is null ? null : valor.Length <= max ? valor : valor[..max];

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
