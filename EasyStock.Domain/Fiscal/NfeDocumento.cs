using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Fiscal;

/// <summary>
/// Agregado raiz da emissao fiscal NFC-e (modelo 65). Cria-se em
/// <see cref="StatusNfe.Rascunho"/>; transicoes validadas pelos metodos
/// MarcarEnviada / MarcarAutorizada / MarcarRejeitada / Cancelar /
/// MarcarInutilizada. Cada transicao append em <see cref="Eventos"/> para
/// audit append-only.
///
/// <para>
/// <b>Origem:</b> nasce a partir de um <see cref="Pedido"/>. Snapshot do
/// emitente (<see cref="DadosEmitente"/>) e destinatario (<see cref="DadosDestinatario"/>)
/// sao gravados como jsonb no momento da criacao para preservar exatamente o
/// que foi enviado a SEFAZ — alteracoes posteriores em Empresa/Cliente nao
/// afetam o documento ja emitido.
/// </para>
///
/// <para>
/// <b>Idempotencia:</b> tres camadas de defesa contra duplicacao fiscal:
/// (a) unique (EmpresaId, ChaveAcesso) WHERE ChaveAcesso IS NOT NULL impede
/// duplicar autorizacao apos SEFAZ devolver a chave;
/// (b) unique (EmpresaId, Modelo, Serie, Numero) impede reutilizar numero ja
/// consumido por modelo+serie;
/// (c) unique (EmpresaId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL
/// impede que retry HTTP com mesma <see cref="IdempotencyKey"/> queime um
/// segundo numero ANTES de chegar ao SEFAZ — defesa em DB para o caso de
/// falha de persistencia do cache HTTP-level (middleware) silenciar a chave.
/// </para>
///
/// <para>
/// <b>XML:</b> <see cref="XmlAssinadoStorageKey"/> aponta para blob em
/// <c>IFileStorage</c> — nunca inline (XML autorizado pode ter centenas de KB).
/// </para>
/// </summary>
public class NfeDocumento
{
    /// <summary>Modelo fiscal — sempre "65" neste corte (NFC-e). Modelo 55 (NF-e B2B) entra em corte futuro.</summary>
    public const string ModeloNfce = "65";

    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    public Guid PedidoId { get; set; }
    public Pedido? Pedido { get; set; }

    /// <summary>Modelo fiscal SEFAZ. Apenas "65" (NFC-e) por agora.</summary>
    public string Modelo { get; set; } = ModeloNfce;
    public short Serie { get; set; }
    public long Numero { get; set; }

    /// <summary>Chave de acesso de 44 digitos atribuida pela SEFAZ na autorizacao. Null ate <see cref="MarcarAutorizada"/>.</summary>
    public string? ChaveAcesso { get; set; }

    /// <summary>
    /// Chave de idempotencia HTTP-level propagada pelo middleware (header
    /// <c>Idempotency-Key</c>) e gravada em DB para defesa em profundidade.
    /// Quando o caller faz retry da mesma emissao (mesma <c>IdempotencyKey</c>),
    /// o use case devolve este <see cref="NfeDocumento"/> em vez de queimar
    /// um segundo numero fiscal — protege contra falha de persistencia do
    /// cache HTTP do middleware (que e silenciada como WARN).
    /// Null para documentos criados antes da migration <c>AddNfeF1RepoIndexes</c>.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public StatusNfe Status { get; set; } = StatusNfe.Rascunho;

    public string? ProtocoloAutorizacao { get; set; }
    public DateTime? DataAutorizacao { get; set; }
    public string? MotivoRejeicao { get; set; }

    /// <summary>Chave do XML assinado/autorizado em <c>IFileStorage</c>. Nunca inline.</summary>
    public string? XmlAssinadoStorageKey { get; set; }

    /// <summary>URL publica/assinada do DANFE em PDF, gerada pelo provedor (Focus/eNotas).</summary>
    public string? DanfeUrl { get; set; }

    /// <summary>Snapshot dos dados do emitente no momento da emissao (jsonb).</summary>
    public DadosEmissor DadosEmitente { get; set; } = null!;

    /// <summary>Snapshot dos dados do destinatario no momento da emissao (jsonb). Pode ser null para "consumidor nao identificado".</summary>
    public DadosFaturado? DadosDestinatario { get; set; }

    /// <summary>Total da nota (BRL). Imutavel apos criacao — recalcular exige novo documento.</summary>
    public Dinheiro TotalNota { get; set; } = Dinheiro.Zero;

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    /// <summary>RowVersion (xmin) para optimistic concurrency entre worker e webhook.</summary>
    public uint Versao { get; set; }

    public ICollection<NfeItem> Itens { get; set; } = new List<NfeItem>();
    public ICollection<NfeEvento> Eventos { get; set; } = new List<NfeEvento>();

    public static NfeDocumento Criar(
        Guid empresaId,
        Guid pedidoId,
        short serie,
        long numero,
        DadosEmissor dadosEmitente,
        DadosFaturado? dadosDestinatario,
        Dinheiro totalNota,
        Guid? usuarioId = null,
        string? usuarioNome = null,
        string? origem = null,
        string? idempotencyKey = null)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId obrigatorio.", nameof(empresaId));
        if (pedidoId == Guid.Empty)
            throw new ArgumentException("PedidoId obrigatorio.", nameof(pedidoId));
        if (serie <= 0)
            throw new ArgumentException("Serie deve ser positiva.", nameof(serie));
        if (numero <= 0)
            throw new ArgumentException("Numero deve ser positivo.", nameof(numero));
        if (dadosEmitente is null)
            throw new ArgumentNullException(nameof(dadosEmitente));
        if (totalNota.Valor <= 0m)
            throw new RegraDeDominioVioladaException("Total da nota deve ser maior que zero.");
        if (idempotencyKey is not null && idempotencyKey.Length > 120)
            throw new ArgumentException("IdempotencyKey nao pode exceder 120 caracteres.", nameof(idempotencyKey));

        var agora = DateTime.UtcNow;
        var doc = new NfeDocumento
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            PedidoId = pedidoId,
            Modelo = ModeloNfce,
            Serie = serie,
            Numero = numero,
            Status = StatusNfe.Rascunho,
            DadosEmitente = dadosEmitente,
            DadosDestinatario = dadosDestinatario,
            TotalNota = totalNota,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
        doc.AppendEvento("criado", agora, usuarioId, usuarioNome, origem, dadosJson: null);
        return doc;
    }

    public NfeItem AdicionarItem(
        string nomeSnapshot,
        decimal quantidade,
        Dinheiro precoUnitario,
        string unidade,
        string? ncm = null,
        string? cfop = null,
        Guid? produtoIdSnapshot = null,
        byte origemMercadoria = 0,
        string? cstOuCsosn = null)
    {
        if (Status != StatusNfe.Rascunho)
            throw new RegraDeDominioVioladaException("So Rascunho aceita novos itens.");

        var item = NfeItem.Criar(
            nfeDocumentoId: Id,
            ordem: Itens.Count + 1,
            nomeSnapshot: nomeSnapshot,
            quantidade: quantidade,
            precoUnitario: precoUnitario,
            unidade: unidade,
            ncm: ncm,
            cfop: cfop,
            produtoIdSnapshot: produtoIdSnapshot,
            origemMercadoria: origemMercadoria,
            cstOuCsosn: cstOuCsosn);

        Itens.Add(item);
        AlteradoEm = DateTime.UtcNow;
        return item;
    }

    public void MarcarEnviada(Guid? usuarioId = null, string? usuarioNome = null, string? origem = null)
    {
        if (Status == StatusNfe.EnviadaAguardandoRetorno) return;
        if (Status != StatusNfe.Rascunho && Status != StatusNfe.FalhaTransiente)
            throw new RegraDeDominioVioladaException(
                $"So Rascunho ou FalhaTransiente podem ser marcadas como Enviada (atual: {Status}).");
        if (Itens.Count == 0)
            throw new RegraDeDominioVioladaException("NFC-e sem itens nao pode ser enviada.");

        var agora = DateTime.UtcNow;
        Status = StatusNfe.EnviadaAguardandoRetorno;
        AlteradoEm = agora;
        AppendEvento("enviado", agora, usuarioId, usuarioNome, origem, dadosJson: null);
    }

    public void MarcarAutorizada(
        string chaveAcesso,
        string protocoloAutorizacao,
        string? xmlAssinadoStorageKey = null,
        string? danfeUrl = null,
        Guid? usuarioId = null,
        string? usuarioNome = null,
        string? origem = null)
    {
        if (string.IsNullOrWhiteSpace(chaveAcesso) || chaveAcesso.Length != 44)
            throw new ArgumentException("ChaveAcesso deve ter 44 digitos.", nameof(chaveAcesso));
        if (string.IsNullOrWhiteSpace(protocoloAutorizacao))
            throw new ArgumentException("ProtocoloAutorizacao obrigatorio.", nameof(protocoloAutorizacao));

        if (Status == StatusNfe.Autorizada)
        {
            if (ChaveAcesso != chaveAcesso)
                throw new RegraDeDominioVioladaException("ChaveAcesso ja autorizada nao pode ser sobrescrita.");
            return;
        }
        if (Status != StatusNfe.EnviadaAguardandoRetorno)
            throw new RegraDeDominioVioladaException(
                $"So EnviadaAguardandoRetorno pode ser autorizada (atual: {Status}).");

        var agora = DateTime.UtcNow;
        Status = StatusNfe.Autorizada;
        ChaveAcesso = chaveAcesso;
        ProtocoloAutorizacao = protocoloAutorizacao;
        DataAutorizacao = agora;
        XmlAssinadoStorageKey = xmlAssinadoStorageKey;
        DanfeUrl = danfeUrl;
        AlteradoEm = agora;
        AppendEvento("autorizado", agora, usuarioId, usuarioNome, origem, dadosJson: null);
    }

    public void MarcarRejeitada(
        string motivo,
        Guid? usuarioId = null,
        string? usuarioNome = null,
        string? origem = null)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("Motivo da rejeicao obrigatorio.", nameof(motivo));
        if (Status != StatusNfe.EnviadaAguardandoRetorno)
            throw new RegraDeDominioVioladaException(
                $"So EnviadaAguardandoRetorno pode ser rejeitada (atual: {Status}).");

        var agora = DateTime.UtcNow;
        Status = StatusNfe.Rejeitada;
        MotivoRejeicao = motivo;
        AlteradoEm = agora;
        AppendEvento("rejeitado", agora, usuarioId, usuarioNome, origem, dadosJson: null);
    }

    public void Cancelar(
        string motivo,
        Guid? usuarioId = null,
        string? usuarioNome = null,
        string? origem = null)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("Motivo do cancelamento obrigatorio.", nameof(motivo));
        if (Status == StatusNfe.Cancelada) return;
        if (Status != StatusNfe.Autorizada)
            throw new RegraDeDominioVioladaException(
                $"So NFC-e Autorizada pode ser cancelada (atual: {Status}).");

        var agora = DateTime.UtcNow;
        Status = StatusNfe.Cancelada;
        MotivoRejeicao = motivo;
        AlteradoEm = agora;
        AppendEvento("cancelado", agora, usuarioId, usuarioNome, origem, dadosJson: null);
    }

    public void MarcarInutilizada(Guid? usuarioId = null, string? usuarioNome = null, string? origem = null)
    {
        if (Status == StatusNfe.Inutilizada) return;
        if (Status != StatusNfe.Rascunho && Status != StatusNfe.Rejeitada)
            throw new RegraDeDominioVioladaException(
                $"So Rascunho ou Rejeitada podem ser inutilizadas (atual: {Status}).");

        var agora = DateTime.UtcNow;
        Status = StatusNfe.Inutilizada;
        AlteradoEm = agora;
        AppendEvento("inutilizado", agora, usuarioId, usuarioNome, origem, dadosJson: null);
    }

    public void MarcarFalhaTransiente(string? detalhe = null, Guid? usuarioId = null, string? usuarioNome = null, string? origem = null)
    {
        if (Status != StatusNfe.EnviadaAguardandoRetorno)
            throw new RegraDeDominioVioladaException(
                $"FalhaTransiente so e valida apos envio (atual: {Status}).");

        var agora = DateTime.UtcNow;
        Status = StatusNfe.FalhaTransiente;
        AlteradoEm = agora;
        AppendEvento("erro_transiente", agora, usuarioId, usuarioNome, origem, dadosJson: detalhe);
    }

    private void AppendEvento(string tipo, DateTime ocorridoEm, Guid? usuarioId, string? usuarioNome, string? origem, string? dadosJson)
    {
        Eventos.Add(new NfeEvento
        {
            Id = Guid.NewGuid(),
            NfeDocumentoId = Id,
            Tipo = tipo,
            DadosJson = dadosJson,
            UsuarioId = usuarioId,
            UsuarioNome = usuarioNome,
            Origem = origem,
            OcorridoEm = ocorridoEm,
        });
    }
}
