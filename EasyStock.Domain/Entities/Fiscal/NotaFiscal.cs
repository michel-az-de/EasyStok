using System;
using System.Collections.Generic;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.Exceptions.Fiscal;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.ValueObjects.Fiscal;

namespace EasyStock.Domain.Entities.Fiscal;

/// <summary>
/// Aggregate root do módulo fiscal. Representa um documento fiscal eletrônico
/// (NFC-e modelo 65 ou NFe modelo 55) emitido por uma <see cref="Empresa"/>
/// via <see cref="Loja"/>. Encapsula:
///  - Numeração sequencial por (empresa, loja, modelo, série) (ADR-004).
///  - State machine via <see cref="NotaFiscalStateMachine"/>.
///  - Persistência de XML autorizado e XML de contingência (ADR-003).
///  - Auditoria via <see cref="NotaFiscalEvento"/>.
///  - Idempotência E2E via <see cref="IdempotencyKey"/>.
/// Toda transição de estado vai por método público — setters são privados
/// pra garantir invariantes.
/// </summary>
public sealed class NotaFiscal
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid? LojaId { get; private set; }
    public Guid? PedidoId { get; private set; }
    public Guid? VendaId { get; private set; }

    public ModeloDocumentoFiscal Modelo { get; private set; }
    public int Serie { get; private set; }
    public int Numero { get; private set; }
    public ChaveAcessoNFe ChaveAcesso { get; private set; } = null!;

    public StatusNotaFiscal Status { get; private set; }
    public TipoEmissao TipoEmissao { get; private set; }
    public AmbienteSefaz Ambiente { get; private set; }

    public DateTime DataEmissao { get; private set; }
    public DateTime? DataAutorizacao { get; private set; }
    public DateTime? DataCancelamento { get; private set; }

    public string? ProtocoloAutorizacao { get; private set; }
    public string? ProtocoloCancelamento { get; private set; }

    public string? XmlAutorizado { get; private set; }
    public string? XmlAssinadoLocal { get; private set; }
    public string? XmlEventoCancelamento { get; private set; }

    public string? CodigoRejeicao { get; private set; }
    public string? MotivoRejeicao { get; private set; }
    public string? JustificativaCancelamento { get; private set; }

    public string? ClienteCpfCnpj { get; private set; }
    public Dinheiro ValorTotal { get; private set; } = Dinheiro.Zero;
    public string? FormaPagamentoPrincipal { get; private set; }

    public string IdempotencyKey { get; private set; } = null!;
    public string? Origem { get; private set; }
    public Guid? CriadoPorUsuarioId { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }
    public bool Arquivado { get; private set; }

    private readonly List<NotaFiscalItem> _itens = new();
    public IReadOnlyCollection<NotaFiscalItem> Itens => _itens.AsReadOnly();

    private readonly List<NotaFiscalPagamento> _pagamentos = new();
    public IReadOnlyCollection<NotaFiscalPagamento> Pagamentos => _pagamentos.AsReadOnly();

    private readonly List<NotaFiscalEvento> _eventos = new();
    public IReadOnlyCollection<NotaFiscalEvento> Eventos => _eventos.AsReadOnly();

    private NotaFiscal() { }

    public static NotaFiscal CriarParaEmissao(
        Guid empresaId,
        Guid? lojaId,
        Guid? pedidoId,
        ModeloDocumentoFiscal modelo,
        int serie,
        int numero,
        ChaveAcessoNFe chaveAcesso,
        TipoEmissao tipoEmissao,
        AmbienteSefaz ambiente,
        DateTime dataEmissao,
        Dinheiro valorTotal,
        string? clienteCpfCnpj,
        string idempotencyKey,
        string? origem,
        Guid? criadoPorUsuarioId)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
        if (numero is <= 0 or > 999_999_999)
            throw new ArgumentOutOfRangeException(nameof(numero), "Número fora do intervalo válido (1..999.999.999).");
        if (serie is <= 0 or > 999)
            throw new ArgumentOutOfRangeException(nameof(serie), "Série fora do intervalo válido (1..999).");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("IdempotencyKey é obrigatório.", nameof(idempotencyKey));
        if (chaveAcesso is null)
            throw new ArgumentNullException(nameof(chaveAcesso));
        if (valorTotal is null)
            throw new ArgumentNullException(nameof(valorTotal));
        if (!string.IsNullOrEmpty(clienteCpfCnpj))
        {
            var digitos = clienteCpfCnpj.Trim();
            if (digitos.Length is not (11 or 14))
                throw new ArgumentException("CPF/CNPJ deve ter 11 ou 14 dígitos.", nameof(clienteCpfCnpj));
        }

        var agora = DateTime.UtcNow;
        return new NotaFiscal
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LojaId = lojaId,
            PedidoId = pedidoId,
            Modelo = modelo,
            Serie = serie,
            Numero = numero,
            ChaveAcesso = chaveAcesso,
            Status = StatusNotaFiscal.EmEmissao,
            TipoEmissao = tipoEmissao,
            Ambiente = ambiente,
            DataEmissao = dataEmissao,
            ValorTotal = valorTotal,
            ClienteCpfCnpj = clienteCpfCnpj,
            IdempotencyKey = idempotencyKey,
            Origem = origem,
            CriadoPorUsuarioId = criadoPorUsuarioId,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    public void AdicionarItem(NotaFiscalItem item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));
        if (Status != StatusNotaFiscal.EmEmissao)
            throw new TransicaoNotaFiscalInvalidaException(
                "Itens só podem ser adicionados enquanto status=EmEmissao.");
        _itens.Add(item);
    }

    public void AdicionarPagamento(NotaFiscalPagamento pagamento)
    {
        if (pagamento is null)
            throw new ArgumentNullException(nameof(pagamento));
        if (Status != StatusNotaFiscal.EmEmissao)
            throw new TransicaoNotaFiscalInvalidaException(
                "Pagamentos só podem ser adicionados enquanto status=EmEmissao.");
        _pagamentos.Add(pagamento);
    }

    public void DefinirVenda(Guid vendaId)
    {
        if (vendaId == Guid.Empty)
            throw new ArgumentException("VendaId é obrigatório.", nameof(vendaId));
        VendaId = vendaId;
        AlteradoEm = DateTime.UtcNow;
    }

    public void MarcarAutorizada(string protocolo, string xmlAutorizado, DateTime dataAutorizacao)
    {
        NotaFiscalStateMachine.EnsureTransicaoValida(Status, StatusNotaFiscal.Autorizada);
        if (string.IsNullOrWhiteSpace(protocolo))
            throw new ArgumentException("Protocolo é obrigatório.", nameof(protocolo));
        if (string.IsNullOrWhiteSpace(xmlAutorizado))
            throw new ArgumentException("XML autorizado é obrigatório.", nameof(xmlAutorizado));

        if (Status == StatusNotaFiscal.Autorizada) return;

        Status = StatusNotaFiscal.Autorizada;
        ProtocoloAutorizacao = protocolo;
        XmlAutorizado = xmlAutorizado;
        DataAutorizacao = dataAutorizacao;
        AlteradoEm = DateTime.UtcNow;
        _eventos.Add(NotaFiscalEvento.Criar(Id, EmpresaId, "Autorizada", "{}"));
    }

    public void MarcarAutorizadaPosContingencia(string protocolo, string xmlAutorizado, DateTime dataAutorizacao)
    {
        if (Status != StatusNotaFiscal.EmContingencia)
            throw new TransicaoNotaFiscalInvalidaException(Status, StatusNotaFiscal.Autorizada);
        if (string.IsNullOrWhiteSpace(protocolo))
            throw new ArgumentException("Protocolo é obrigatório.", nameof(protocolo));
        if (string.IsNullOrWhiteSpace(xmlAutorizado))
            throw new ArgumentException("XML autorizado é obrigatório.", nameof(xmlAutorizado));

        Status = StatusNotaFiscal.Autorizada;
        ProtocoloAutorizacao = protocolo;
        XmlAutorizado = xmlAutorizado;
        DataAutorizacao = dataAutorizacao;
        AlteradoEm = DateTime.UtcNow;
        _eventos.Add(NotaFiscalEvento.Criar(Id, EmpresaId, "AutorizadaAposContingencia", "{}"));
    }

    public void MarcarRejeitada(string codigo, string motivo)
    {
        NotaFiscalStateMachine.EnsureTransicaoValida(Status, StatusNotaFiscal.Rejeitada);
        if (Status == StatusNotaFiscal.Rejeitada) return;

        Status = StatusNotaFiscal.Rejeitada;
        CodigoRejeicao = codigo;
        MotivoRejeicao = motivo;
        AlteradoEm = DateTime.UtcNow;
        _eventos.Add(NotaFiscalEvento.Criar(Id, EmpresaId, "Rejeitada",
            $"{{\"codigo\":\"{Escape(codigo)}\",\"motivo\":\"{Escape(motivo)}\"}}"));
    }

    public void MarcarDenegada(string codigo, string motivo)
    {
        NotaFiscalStateMachine.EnsureTransicaoValida(Status, StatusNotaFiscal.Denegada);
        if (Status == StatusNotaFiscal.Denegada) return;

        Status = StatusNotaFiscal.Denegada;
        CodigoRejeicao = codigo;
        MotivoRejeicao = motivo;
        AlteradoEm = DateTime.UtcNow;
        _eventos.Add(NotaFiscalEvento.Criar(Id, EmpresaId, "Denegada",
            $"{{\"codigo\":\"{Escape(codigo)}\"}}"));
    }

    public void MarcarEmContingencia(string xmlAssinadoLocal, string motivo)
    {
        NotaFiscalStateMachine.EnsureTransicaoValida(Status, StatusNotaFiscal.EmContingencia);
        if (Status == StatusNotaFiscal.EmContingencia) return;
        if (string.IsNullOrWhiteSpace(xmlAssinadoLocal))
            throw new ArgumentException("XML local é obrigatório em contingência.", nameof(xmlAssinadoLocal));

        Status = StatusNotaFiscal.EmContingencia;
        XmlAssinadoLocal = xmlAssinadoLocal;
        TipoEmissao = TipoEmissao.OfflineNFCe;
        MotivoRejeicao = motivo;
        AlteradoEm = DateTime.UtcNow;
        _eventos.Add(NotaFiscalEvento.Criar(Id, EmpresaId, "ContingenciaIniciada",
            $"{{\"motivo\":\"{Escape(motivo)}\"}}"));
    }

    public void IniciarCancelamento(string justificativa, Guid usuarioId, DateTime now)
    {
        NotaFiscalStateMachine.EnsureTransicaoValida(Status, StatusNotaFiscal.CancelamentoEmAndamento);

        if (string.IsNullOrWhiteSpace(justificativa) || justificativa.Length is < 15 or > 255)
            throw new ArgumentException("Justificativa deve ter entre 15 e 255 caracteres.", nameof(justificativa));

        if (DataAutorizacao is null)
            throw new InvalidOperationException("Não há DataAutorizacao registrada — não é possível cancelar.");

        var minutosDecorridos = (now - DataAutorizacao.Value).TotalMinutes;
        if (minutosDecorridos > 30)
            throw new PrazoCancelamentoExpiradoException(DataAutorizacao.Value, minutosDecorridos);

        Status = StatusNotaFiscal.CancelamentoEmAndamento;
        JustificativaCancelamento = justificativa;
        AlteradoEm = DateTime.UtcNow;
        _eventos.Add(NotaFiscalEvento.Criar(Id, EmpresaId, "CancelamentoIniciado",
            $"{{\"usuarioId\":\"{usuarioId}\"}}", usuarioId: usuarioId));
    }

    public void MarcarCancelada(string protocolo, string xmlEvento, DateTime dataCancelamento)
    {
        NotaFiscalStateMachine.EnsureTransicaoValida(Status, StatusNotaFiscal.Cancelada);
        if (Status == StatusNotaFiscal.Cancelada) return;
        if (string.IsNullOrWhiteSpace(protocolo))
            throw new ArgumentException("Protocolo de cancelamento é obrigatório.", nameof(protocolo));
        if (string.IsNullOrWhiteSpace(xmlEvento))
            throw new ArgumentException("XML do evento de cancelamento é obrigatório.", nameof(xmlEvento));

        Status = StatusNotaFiscal.Cancelada;
        ProtocoloCancelamento = protocolo;
        XmlEventoCancelamento = xmlEvento;
        DataCancelamento = dataCancelamento;
        AlteradoEm = DateTime.UtcNow;
        _eventos.Add(NotaFiscalEvento.Criar(Id, EmpresaId, "Cancelada", "{}"));
    }

    public void ReverterCancelamento(string motivoFalha)
    {
        if (Status != StatusNotaFiscal.CancelamentoEmAndamento)
            throw new TransicaoNotaFiscalInvalidaException(Status, StatusNotaFiscal.Autorizada);

        Status = StatusNotaFiscal.Autorizada;
        AlteradoEm = DateTime.UtcNow;
        _eventos.Add(NotaFiscalEvento.Criar(Id, EmpresaId, "CancelamentoFalhou",
            $"{{\"motivo\":\"{Escape(motivoFalha)}\"}}"));
    }

    public void Arquivar()
    {
        if (Arquivado) return;
        Arquivado = true;
        AlteradoEm = DateTime.UtcNow;
    }

    public bool DentroDoPrazoCancelamento(DateTime now)
    {
        return Status == StatusNotaFiscal.Autorizada
            && DataAutorizacao.HasValue
            && (now - DataAutorizacao.Value).TotalMinutes <= 30;
    }

    private static string Escape(string? input) =>
        (input ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
}
