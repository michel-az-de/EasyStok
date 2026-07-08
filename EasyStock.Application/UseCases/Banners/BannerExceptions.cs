namespace EasyStock.Application.UseCases.Banners;

/// <summary>Banner não existe — o controller mapeia para 404.</summary>
public sealed class BannerNaoEncontradoException() : Exception("Banner não encontrado.");

/// <summary>
/// Tentativa de excluir banner que já tem confirmações/vistos — o controller mapeia
/// para 409. Preserva a prova de recebimento (auditoria); use desativar (Ativo=false).
/// </summary>
public sealed class BannerComConfirmacoesException()
    : Exception("Banner com confirmações registradas não pode ser excluído. Desative-o.");
