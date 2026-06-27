using Npgsql;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Fonte única das mensagens amigáveis para violações de constraint conhecidas do cardápio
/// (ADR-0035). Desduplica os blocos <c>catch (PostgresException)</c> que se repetiam nos
/// controllers de vitrine (admin e tenant).
/// </summary>
internal static class CardapioPostgresErrors
{
    /// <summary>
    /// Traduz uma violação conhecida para mensagem de usuário, ou <c>null</c> quando o erro
    /// não é um dos mapeados (deixando a exceção propagar para o tratamento genérico).
    /// </summary>
    public static string? Traduzir(PostgresException ex) => ex switch
    {
        { SqlState: "23505", ConstraintName: "uq_cardapio_item_variacao_rotulo" }
            => "Há opções com o mesmo rótulo neste item.",
        { SqlState: "23505" } => "Já existe um item com esse nome no cardápio.",
        { SqlState: "23514" } => "Nome é obrigatório para itens sem produto vinculado.",
        { SqlState: "23503" } => "Seção informada não existe.",
        _ => null,
    };
}
