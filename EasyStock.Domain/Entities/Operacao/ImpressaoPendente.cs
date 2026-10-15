using EasyStock.Domain.Enums.Operacao;

namespace EasyStock.Domain.Entities.Operacao;

/// <summary>
/// Item da fila de impressão (S20). A API está na nuvem e não alcança a impressora: o pedido pago entra
/// aqui e um consumidor (decidido pela onda 0.6) busca por polling, imprime e confirma.
///
/// <para>
/// <see cref="MarcarImpressa"/> é idempotente: o consumidor pode repetir a confirmação (timeout, retry)
/// sem mudar o instante nem as tentativas. Falha depois de impressa é ignorada (o papel já saiu).
/// Reimprimir cria outro item: o histórico fica. O instante vem sempre por parâmetro.
/// </para>
/// </summary>
public class ImpressaoPendente
{
    public const int ErroTamanhoMaximo = 500;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid? LojaId { get; private set; }
    public Guid PedidoId { get; private set; }
    public TipoImpressao Tipo { get; private set; }
    public StatusImpressao Status { get; private set; }
    public DateTime CriadaEm { get; private set; }
    public DateTime? ImpressaEm { get; private set; }

    /// <summary>Quantas vezes o consumidor respondeu (impressa ou falhou).</summary>
    public int Tentativas { get; private set; }

    /// <summary>Último erro relatado pelo consumidor.</summary>
    public string? Erro { get; private set; }

    public Guid? SolicitacaoId { get; private set; }
    public Guid? OriginalId { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public Guid? ConfirmadaPor { get; private set; }
    public string? Documento { get; private set; }
    public string? Modelo { get; private set; }
    public string? Codigo { get; private set; }
    public string? Perfil { get; private set; }
    public string? Versao { get; private set; }
    public string? Html { get; private set; }
    public string? Hash { get; private set; }
    public string? Intencao { get; private set; }

    // Snapshot só recebe conteúdo renderizado pelo servidor, nunca HTML enviado pelo operador.
    public static ImpressaoPendente PrepararDocumento(Guid empresaId, Guid? lojaId, Guid pedidoId,
        Guid solicitacaoId, Guid? usuarioId, string documento, string modelo, string codigo,
        string perfil, string versao, string html, string hash, string intencao, DateTime agora, Guid? originalId = null)
    {
        if (solicitacaoId == Guid.Empty) throw new ArgumentException("Solicitação obrigatória.", nameof(solicitacaoId));
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        var i = CriarCanhoto(empresaId, lojaId, pedidoId, agora);
        i.Tipo = TipoImpressao.Documento;
        i.Status = StatusImpressao.Preparada;
        i.SolicitacaoId = solicitacaoId;
        i.UsuarioId = usuarioId;
        i.OriginalId = originalId;
        i.Documento = documento;
        i.Modelo = modelo;
        i.Codigo = codigo;
        i.Perfil = perfil;
        i.Versao = versao;
        i.Html = html;
        i.Hash = hash;
        i.Intencao = intencao;
        return i;
    }

    // EF Core
    private ImpressaoPendente() { }

    public static ImpressaoPendente CriarCanhoto(Guid empresaId, Guid? lojaId, Guid pedidoId, DateTime agora)
    {
        if (empresaId == Guid.Empty) throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
        if (pedidoId == Guid.Empty) throw new ArgumentException("PedidoId é obrigatório.", nameof(pedidoId));

        return new ImpressaoPendente
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LojaId = lojaId,
            PedidoId = pedidoId,
            Tipo = TipoImpressao.Canhoto,
            Status = StatusImpressao.Pendente,
            CriadaEm = agora,
        };
    }

    /// <summary>Confirma a impressão. Devolve <c>false</c> quando já estava impressa (nada muda).</summary>
    public bool MarcarImpressa(DateTime agora, Guid? usuarioId = null)
    {
        if (Status == StatusImpressao.Impressa) return false;
        Status = StatusImpressao.Impressa;
        ImpressaEm = agora;
        ConfirmadaPor = usuarioId;
        Tentativas++;
        return true;
    }

    /// <summary>Registra a falha relatada pelo consumidor. Devolve <c>false</c> quando já estava impressa.</summary>
    public bool RegistrarFalha(string? erro)
    {
        if (Status == StatusImpressao.Impressa) return false;
        Status = StatusImpressao.Falhou;
        Tentativas++;
        var texto = string.IsNullOrWhiteSpace(erro) ? "falha sem detalhe" : erro.Trim();
        Erro = texto.Length > ErroTamanhoMaximo ? texto[..ErroTamanhoMaximo] : texto;
        return true;
    }
}
