using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record EnviarModeloConversaCommand(
    Guid EmpresaId, Guid UsuarioId, Guid ConversaId, Guid TentativaId, string Nome, string Idioma, IReadOnlyList<string> Parametros);

public sealed partial class EnviarModeloConversaUseCase(
    IConversaRepository conversas, IClienteRepository clientes, ICatalogoModelosAtendimento catalogo,
    PoliticaEnvioCliente politica, ResolvedorCanal canais, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    /// <summary>Aviso da falha incerta: ninguém sabe se o modelo saiu, e por isso ele nunca é reenviado sozinho.</summary>
    public const string AvisoEnvioIncerto = "Confira a conversa no WhatsApp antes de iniciar outro envio.";

    public async Task<MensagemAtendimentoResult> ExecuteAsync(EnviarModeloConversaCommand command, CancellationToken ct = default)
    {
        if (command.EmpresaId == Guid.Empty || command.UsuarioId == Guid.Empty || command.ConversaId == Guid.Empty
            || command.TentativaId == Guid.Empty || string.IsNullOrWhiteSpace(command.Nome) || string.IsNullOrWhiteSpace(command.Idioma)
            || command.Parametros is null || command.Parametros.Count > 99
            || command.Parametros.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 1024 || p.Contains("{{", StringComparison.Ordinal)))
            throw new UseCaseValidationException("Informe a tentativa, o modelo, o idioma e os parâmetros preenchidos (até 1024 caracteres cada).");
        var modelo = new ModeloMensagem(command.Nome.Trim(), command.Idioma.Trim(), command.Parametros.Select(p => p.Trim()).ToArray());
        var conversa = ValidarConversa(await conversas.ObterPorIdAsync(command.EmpresaId, command.ConversaId, ct), command);
        var existente = await ObterTentativaAsync(command, modelo, ct);
        if (existente is not null) return MensagemAtendimentoResult.De(existente);
        GarantirAbertaComModelo(conversa);

        // Aprovação é conferida novamente no envio, sem o cache curto da listagem da tela.
        var aprovado = (await catalogo.ListarAsync(command.EmpresaId, ct))
            .FirstOrDefault(m => m.Nome == modelo.Nome && m.Idioma == modelo.Idioma)
            ?? throw new UseCaseValidationException("Modelo não está aprovado nesta conta e idioma. Atualize a lista.");
        if (aprovado.Indisponivel is not null) throw new UseCaseValidationException(aprovado.Indisponivel);
        if (aprovado.QuantidadeParametros != modelo.Parametros.Count)
            throw new UseCaseValidationException($"O modelo exige {aprovado.QuantidadeParametros} parâmetros.");
        var texto = string.Join("\n", new[] { aprovado.Cabecalho,
            Parametro().Replace(aprovado.Corpo, m => modelo.Parametros[int.Parse(m.Groups[1].Value) - 1]), aprovado.Rodape }
            .Where(p => !string.IsNullOrEmpty(p)));
        if (texto.Length > Mensagem.TextoTamanhoMaximo)
            throw new UseCaseValidationException("A prévia preenchida excede o tamanho máximo da mensagem.");

        var nova = false;
        Mensagem mensagem;
        try
        {
            mensagem = await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
            {
                conversa = ValidarConversa(await conversas.ObterPorIdComLockAsync(command.EmpresaId, command.ConversaId, token), command);
                existente = await ObterTentativaAsync(command, modelo, token);
                if (existente is not null) return existente;
                GarantirAbertaComModelo(conversa);
                await GarantirClienteAsync(conversa, aprovado.Categoria, token);
                var agora = relogio.GetUtcNow().UtcDateTime;
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
        if (!nova) return MensagemAtendimentoResult.De(mensagem);

        // A reserva já está gravada: repetição, cancelamento HTTP ou reinício nunca repetem o POST do canal.
        // Uma falha ao salvar o resultado deixa a intenção visível como sem confirmação.
        try
        {
            var externoId = await canais.Obter(conversa.Canal).EnviarModeloAsync(
                conversa.ContatoIdExterno, modelo.Nome, modelo.Idioma, modelo.Parametros, ct);
            mensagem.ConfirmarEnvio(externoId);
        }
        catch (Exception ex)
        {
            // Nada saiu: a Meta recusou (4xx) ou o cliente barrou antes da rede (permanente e sem status, ex.:
            // empresa sem phone_number_id). O resto fica incerto, inclusive o "2xx sem message id".
            var tipo = ex is WhatsAppCloudException { StatusHttp: >= 400 and < 500 }
                    or WhatsAppCloudException { EhPermanente: true, StatusHttp: null }
                ? TipoFalhaEnvio.Permanente : TipoFalhaEnvio.Incerta;
            mensagem.RegistrarFalhaEnvio(tipo == TipoFalhaEnvio.Incerta
                ? AvisoEnvioIncerto
                : "O WhatsApp não confirmou o envio deste modelo. Confira a conexão e o modelo antes de tentar novamente.", tipo,
                relogio.GetUtcNow().UtcDateTime);
        }
        await unitOfWork.CommitAsync();
        return MensagemAtendimentoResult.De(mensagem);
    }

    private async Task<Mensagem?> ObterTentativaAsync(EnviarModeloConversaCommand command, ModeloMensagem modelo, CancellationToken ct)
    {
        var m = await conversas.ObterMensagemAsync(command.EmpresaId, command.ConversaId, command.TentativaId, ct);
        if (m is null) return null;
        if (m.EmpresaId != command.EmpresaId || m.ConversaId != command.ConversaId || m.EnviadaPorUsuarioId != command.UsuarioId
            || m.Modelo is not { } anterior || anterior.Nome != modelo.Nome || anterior.Idioma != modelo.Idioma
            || !anterior.Parametros.SequenceEqual(modelo.Parametros))
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
        var finalidade = categoria == "MARKETING" ? FinalidadeContato.Marketing : FinalidadeContato.Transacional;
        if (!await politica.PodeEnviarAsync(conversa.EmpresaId, clienteId, conversa.Canal, finalidade, ct))
            throw new RegraDeDominioVioladaException("O cliente não autorizou esta finalidade neste canal. Confira os consentimentos na Ficha.");
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
