namespace EasyStock.Application.Services.Auth;

/// <summary>
/// #1753: quem lê a auditoria é a dona, então a linha diz o nome de quem fez, nunca um identificador. A linha de
/// <see cref="AuditLog"/> pertence à pessoa afetada; o autor vai no texto.
/// </summary>
public static class RastroDeUsuarios
{
    public const string Desativado = "usuario-desativado";
    public const string Reativado = "usuario-reativado";
    public const string PerfilTrocado = "perfil-trocado";
    public const string Convidado = "usuario-convidado";
    public const string Criado = "usuario-criado";
    public const string DonaMarcada = "dona-marcada";
    public const string DonaDesmarcada = "dona-desmarcada";

    /// <summary>A política de hierarquia recusou (#2070): linha com <c>Sucesso = false</c>, o código e quem tentou.</summary>
    public const string HierarquiaRecusada = "hierarquia-recusada";

    /// <summary>Nome de quem está fazendo a ação; sem cadastro legível (suporte, job) vira "suporte".</summary>
    public static async Task<string> AutorAsync(IUsuarioRepository usuarios, ICurrentUserAccessor atual)
    {
        var nome = atual.UsuarioId == Guid.Empty ? null : (await usuarios.GetByIdAsync(atual.UsuarioId))?.Nome;
        return string.IsNullOrWhiteSpace(nome) ? "suporte" : nome.Trim();
    }

    public static AuditLog Linha(
        Guid afetadoId, string acao, string detalhes, ICurrentUserAccessor atual, Guid empresaId, bool sucesso = true)
    {
        var linha = AuditLog.Criar(afetadoId, acao, sucesso, detalhes, atual.Ip, atual.UserAgent);
        linha.EmpresaId = empresaId;
        return linha;
    }
}
