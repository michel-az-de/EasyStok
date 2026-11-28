using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record EnviarModeloConversaCommand(
    Guid EmpresaId, Guid UsuarioId, Guid ConversaId, Guid TentativaId, string Nome, string Idioma, IReadOnlyList<string> Parametros, bool Confirmada = false);

public sealed partial class EnviarModeloConversaUseCase(
    IConversaRepository conversas, IClienteRepository clientes, ICatalogoModelosAtendimento catalogo,
    PoliticaEnvioCliente politica, ResolvedorCanal canais, IUnitOfWork unitOfWork, TimeProvider relogio, IAtendenteRepository? atendentes = null,
    ILogger<EnviarModeloConversaUseCase>? logger = null)
{
    /// <summary>Aviso da falha incerta: ninguém sabe se o modelo saiu, e por isso ele nunca é reenviado sozinho.</summary>
    public const string AvisoEnvioIncerto = "Confira a conversa no WhatsApp antes de iniciar outro envio.";

    public async Task<MensagemAtendimentoResult> ExecuteAsync(EnviarModeloConversaCommand command, CancellationToken ct = default)
    {
        if (command.EmpresaId == Guid.Empty || command.UsuarioId == Guid.Empty || command.ConversaId == Guid.Empty)
            throw new UseCaseValidationException(EntradaInvalida);
        var modelo = ModeloDaTentativa(command.TentativaId, command.Nome, command.Idioma, command.Parametros);
        var conversa = ValidarConversa(await conversas.ObterPorIdAsync(command.EmpresaId, command.ConversaId, ct), command);
        var existente = await ObterTentativaAsync(command, modelo, ct);
        if (existente is not null) return Repetida(existente);
        GarantirAbertaComModelo(conversa);
        // #2440: modelo em conversa de outra atendente pede confirmação, como o texto (ATD-1, #2305).
        await GuardaAtribuicaoConversa.GarantirAsync(atendentes, conversa, command.UsuarioId, command.Confirmada, ct);

        // Aprovação é conferida novamente no envio, sem o cache curto da listagem da tela.
        var aprovado = await ModeloAprovadoAsync(catalogo, command.EmpresaId, modelo, ct);
        // #3205: a origem vem do catálogo da empresa, nunca do pedido; ela segue na mensagem gravada e na idempotência.
        modelo = modelo with { Origem = aprovado.Origem };
        var texto = TextoDoModelo(aprovado, modelo.Parametros);
        if (texto.Length > Mensagem.TextoTamanhoMaximo)
            throw new UseCaseValidationException("A prévia preenchida excede o tamanho máximo da mensagem.");

        var nova = false;
        Mensagem mensagem;
        try
        {
            mensagem = await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
            {
                conversa = ValidarConversa(await conversas.ObterPorIdComLockAsync(command.EmpresaId, command.ConversaId, token), command);
                existente = await ObterTentativaAsync(command, modelo, token, compararOrigem: true);
                if (existente is not null) return existente;
                GarantirAbertaComModelo(conversa);
                var agora = relogio.GetUtcNow().UtcDateTime;
                if (modelo.Origem == OrigemModelo.Local) await GarantirDestinoDoModeloLocalAsync(conversa, aprovado.Categoria, agora, token);
                else await GarantirClienteAsync(conversa, aprovado.Categoria, token);
                var intencao = Mensagem.SaidaModelo(command.TentativaId, command.EmpresaId, conversa.Id, command.UsuarioId, agora, modelo, texto);
                conversa.Assumir(agora, command.UsuarioId);
                conversa.RegistrarSaida(agora);
                await conversas.AddMensagemAsync(intencao, token);
                await unitOfWork.CommitAsync();
                nova = true;
                return intencao;
            }, ct);
        }
        catch (Exception ex) when (unitOfWork.EhViolacaoDeUnicidade(ex))
        {
            unitOfWork.DescartarAlteracoesPendentes();
            throw new RegraDeDominioVioladaException("Esta tentativa já foi usada em outra mensagem.");
        }
        if (!nova) return Repetida(mensagem);

        // A reserva já está gravada: repetição, cancelamento HTTP ou reinício nunca repetem o POST do canal.
        // Uma falha ao salvar o resultado deixa a intenção visível como sem confirmação.
        string? externoId = null;
        Exception? falha = null;
        try
        {
            var canal = canais.Obter(conversa.Canal);
            // #3205: a ponte não tem modelo; o local sai como o texto renderizado, gravado como modelo.
            externoId = modelo.Origem == OrigemModelo.Local
                ? await canal.EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct)
                : await canal.EnviarModeloAsync(conversa.ContatoIdExterno, modelo.Nome, modelo.Idioma, modelo.Parametros, ct);
        }
        catch (Exception ex)
        {
            falha = ex;
            // #3229: registrada antes da transação, para não depender da releitura sob lock.
            RegistrarFalha(command.ConversaId, mensagem.Id, Classificar(ex), ex);
        }

        // #1599: a varredura de intenção presa pode ter marcado falha incerta com o POST ainda no ar. O desfecho é
        // gravado sobre a linha relida sob lock, e sem o token da requisição: cancelar o HTTP não descarta o resultado.
        return await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
        {
            var atual = await conversas.ObterMensagemComLockAsync(command.EmpresaId, command.ConversaId, mensagem.Id, token) ?? mensagem;
            if (falha is null)
            {
                try
                {
                    // O canal entregou: a confirmação vence a falha incerta que a varredura gravou.
                    if (atual.Status == StatusMensagem.Falhou) atual.ConfirmarEnvioAposFalhaIncerta(externoId!);
                    else atual.ConfirmarEnvio(externoId!);
                }
                catch (Exception ex)
                {
                    falha = ex;
                    RegistrarFalha(command.ConversaId, mensagem.Id, Classificar(ex), ex);
                }
            }
            var tipo = Classificar(falha);
            // Falha depois de a varredura já ter encerrado a intenção não soma outra tentativa.
            if (falha is not null && atual.Status == StatusMensagem.Pendente)
                atual.RegistrarFalhaEnvio(tipo == TipoFalhaEnvio.Incerta
                    ? AvisoEnvioIncerto
                    : "O WhatsApp não confirmou o envio deste modelo. Confira a conexão e o modelo antes de tentar novamente.", tipo,
                    relogio.GetUtcNow().UtcDateTime);
            await unitOfWork.CommitAsync();
            return MensagemAtendimentoResult.De(atual);
        }, CancellationToken.None);
    }

    private MensagemAtendimentoResult Repetida(Mensagem existente)
    {
        logger?.LogInformation("Envio de modelo repetido: devolve a mensagem {MensagemId} já gravada, status {Status}.",
            existente.Id, existente.Status);
        return MensagemAtendimentoResult.De(existente);
    }

    /// <summary>
    /// Nada saiu: a Meta recusou (4xx) ou o cliente barrou antes da rede (permanente e sem status, ex.: empresa sem
    /// phone_number_id). O resto fica incerto, inclusive o "2xx sem message id".
    /// </summary>
    private static TipoFalhaEnvio Classificar(Exception? falha) =>
        falha is WhatsAppCloudException { StatusHttp: >= 400 and < 500 }
            or WhatsAppCloudException { EhPermanente: true, StatusHttp: null }
            ? TipoFalhaEnvio.Permanente : TipoFalhaEnvio.Incerta;

    /// <summary>
    /// #3229: a falha do canal não sai muda, nem quando a varredura já encerrou a intenção. Sem a exceção e sem a
    /// Message dela: a ponte devolve o telefone e o texto do cliente nela (#2590).
    /// </summary>
    private void RegistrarFalha(Guid conversaId, Guid mensagemId, TipoFalhaEnvio tipo, Exception falha)
    {
        var canal = falha as WhatsAppCloudException;
        logger?.LogWarning(
            "Envio de modelo sem confirmação do canal: tipo {TipoFalha}, status HTTP {StatusHttp}, código {CodigoExcecao}, exceção {TipoExcecao}, conversa {ConversaId}, mensagem {MensagemId}.",
            tipo, canal?.StatusHttp, canal?.Codigo, falha.GetType().Name, conversaId, mensagemId);
    }

    private const string EntradaInvalida = "Informe a tentativa, o modelo, o idioma e os parâmetros preenchidos (até 1024 caracteres cada).";

    /// <summary>Entrada do envio de modelo, conferida também pelo <see cref="IniciarConversaClienteUseCase"/> antes de abrir conversa.</summary>
    internal static ModeloMensagem ModeloDaTentativa(Guid tentativaId, string? nome, string? idioma, IReadOnlyList<string>? parametros)
    {
        if (tentativaId == Guid.Empty || string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(idioma)
            || parametros is null || parametros.Count > 99
            || parametros.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 1024 || p.Contains("{{", StringComparison.Ordinal)))
            throw new UseCaseValidationException(EntradaInvalida);
        return new ModeloMensagem(nome.Trim(), idioma.Trim(), parametros.Select(p => p.Trim()).ToArray());
    }

    /// <summary>O modelo aprovado no catálogo, disponível e com a quantidade de parâmetros informada.</summary>
    internal static async Task<ModeloAtendimento> ModeloAprovadoAsync(
        ICatalogoModelosAtendimento catalogo, Guid empresaId, ModeloMensagem modelo, CancellationToken ct)
    {
        var aprovado = (await catalogo.ListarAsync(empresaId, ct))
            .FirstOrDefault(m => m.Nome == modelo.Nome && m.Idioma == modelo.Idioma)
            ?? throw new UseCaseValidationException("Modelo não está aprovado nesta conta e idioma. Atualize a lista.");
        if (aprovado.Indisponivel is not null) throw new UseCaseValidationException(aprovado.Indisponivel);
        if (aprovado.QuantidadeParametros != modelo.Parametros.Count)
            throw new UseCaseValidationException($"O modelo exige {aprovado.QuantidadeParametros} parâmetros.");
        return aprovado;
    }

    /// <summary>
    /// #3415: o modelo de retomada (uma variável, o primeiro nome, categoria UTILITY: ele reabre a conversa, não faz
    /// propaganda), com o nome dele na frase da recusa.
    /// </summary>
    internal static async Task<ModeloAtendimento> ModeloDeRetomadaAsync(
        ICatalogoModelosAtendimento catalogo, Guid empresaId, ModeloRetomada modelo, CancellationToken ct)
    {
        try
        {
            var aprovado = await ModeloAprovadoAsync(catalogo, empresaId, new ModeloMensagem(modelo.Nome, modelo.Idioma, ["cliente"]), ct);
            if (aprovado.Categoria != "UTILITY")
                throw new UseCaseValidationException("a retomada só usa modelo da categoria UTILITY.");
            return aprovado;
        }
        catch (UseCaseValidationException ex)
        {
            throw new UseCaseValidationException($"Modelo de retomada {modelo.Nome} ({modelo.Idioma}): {ex.Message}");
        }
    }

    /// <summary>O texto do modelo preenchido (cabeçalho, corpo com as variáveis e rodapé), como sai na ponte web (#3205).</summary>
    internal static string TextoDoModelo(ModeloAtendimento modelo, IReadOnlyList<string> parametros) =>
        string.Join("\n", new[] { modelo.Cabecalho,
            Parametro().Replace(modelo.Corpo, m => parametros[int.Parse(m.Groups[1].Value) - 1]), modelo.Rodape }
            .Where(p => !string.IsNullOrEmpty(p)));

    internal static FinalidadeContato FinalidadeDaCategoria(string categoria) =>
        categoria == "MARKETING" ? FinalidadeContato.Marketing : FinalidadeContato.Transacional;

    private async Task<Mensagem?> ObterTentativaAsync(
        EnviarModeloConversaCommand command, ModeloMensagem modelo, CancellationToken ct, bool compararOrigem = false)
    {
        var m = await conversas.ObterMensagemAsync(command.EmpresaId, command.ConversaId, command.TentativaId, ct);
        if (m is null) return null;
        if (m.EmpresaId != command.EmpresaId || m.ConversaId != command.ConversaId || m.EnviadaPorUsuarioId != command.UsuarioId
            || m.Modelo is not { } anterior || anterior.Nome != modelo.Nome || anterior.Idioma != modelo.Idioma
            || !anterior.Parametros.SequenceEqual(modelo.Parametros) || compararOrigem && anterior.Origem != modelo.Origem)
            throw new RegraDeDominioVioladaException("Esta tentativa já foi usada com outro conteúdo ou autor.");
        return m;
    }

    private async Task GarantirClienteAsync(Conversa conversa, string categoria, CancellationToken ct)
    {
        if (conversa.ClienteId is not { } clienteId)
            throw new UseCaseValidationException("Cadastre o cliente na Ficha antes de enviar um modelo.");
        var cliente = await clientes.GetByIdAsync(conversa.EmpresaId, clienteId);
        if (cliente is null || cliente.EmpresaId != conversa.EmpresaId)
            throw new UseCaseValidationException("Cliente não encontrado nesta empresa.");
        if (cliente.Bloqueado) throw new RegraDeDominioVioladaException("Cliente bloqueado: modelo não enviado.");
        var finalidade = FinalidadeDaCategoria(categoria);
        if (!await politica.PodeEnviarAsync(conversa.EmpresaId, clienteId, conversa.Canal, finalidade, ct))
            throw new RegraDeDominioVioladaException("O cliente não autorizou esta finalidade neste canal. Confira os consentimentos na Ficha.");
    }

    /// <summary>Dias, a contar da última mensagem do cliente, em que a mensagem padrão local pode sair (S80).</summary>
    public const int DiasDaJanelaDoModeloLocal = 7;

    /// <summary>
    /// Mensagem padrão local (#3205): só por conversa em que o cliente escreveu até 7 dias atrás. Com cliente cadastrado valem
    /// bloqueio e consentimento; o lead sem cadastro recebe, salvo se pediu para parar (SAIR, PARAR ou STOP).
    /// </summary>
    private async Task GarantirDestinoDoModeloLocalAsync(Conversa conversa, string categoria, DateTime agora, CancellationToken ct)
    {
        GarantirJanelaDoModeloLocal(conversa.UltimaMensagemEntradaEm, agora);
        if (conversa.ClienteId is not null)
        {
            await GarantirClienteAsync(conversa, categoria, ct);
            return;
        }
        await GarantirSemPedidoDeOptOutAsync(conversas, conversa, ct);
    }

    /// <summary>A mensagem padrão local só sai a quem escreveu até 7 dias atrás (S80); o reenvio usa o mesmo prazo (#3415).</summary>
    internal static void GarantirJanelaDoModeloLocal(DateTime? ultimaEntrada, DateTime agora)
    {
        if (ultimaEntrada is not { } entrada)
            throw new RegraDeDominioVioladaException("A mensagem padrão só sai depois que o cliente escreve. Este contato ainda não escreveu.");
        if (entrada < agora.AddDays(-DiasDaJanelaDoModeloLocal))
            throw new RegraDeDominioVioladaException(
                $"O cliente escreveu há mais de {DiasDaJanelaDoModeloLocal} dias: a mensagem padrão não pode mais ser enviada. Aguarde ele escrever de novo.");
    }

    /// <summary>Quem escreveu SAIR, PARAR ou STOP na conversa não recebe mensagem padrão.</summary>
    internal static async Task GarantirSemPedidoDeOptOutAsync(IConversaRepository conversas, Conversa conversa, CancellationToken ct)
    {
        var recentes = await conversas.ListarMensagensAsync(conversa.EmpresaId, conversa.Id, null, 100, ct);
        if (recentes.Any(m => m.Direcao == DirecaoMensagem.Entrada && PoliticaConsentimento.EhPedidoDeOptOut(m.Texto)))
            throw new RegraDeDominioVioladaException("O contato pediu para parar de receber mensagens: a mensagem padrão não foi enviada.");
    }

    private static void GarantirAbertaComModelo(Conversa conversa)
    {
        if (!conversa.EstaAberta) throw new RegraDeDominioVioladaException("Reabra a conversa antes de enviar um modelo.");
        if (!conversa.Capacidades.AceitaModelo) throw new UseCaseValidationException("Este canal não aceita modelos aprovados.");
    }

    private static Conversa ValidarConversa(Conversa? conversa, EnviarModeloConversaCommand command) =>
        conversa is not null && conversa.EmpresaId == command.EmpresaId && conversa.Id == command.ConversaId
            ? conversa : throw new ConversaNaoEncontradaException(command.ConversaId);

    [GeneratedRegex(@"\{\{([1-9][0-9]?)\}\}")]
    private static partial Regex Parametro();
}
