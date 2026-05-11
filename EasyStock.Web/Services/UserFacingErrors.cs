using System.Text.RegularExpressions;

namespace EasyStock.Web.Services;

/// <summary>
/// Sanitiza mensagens de erro vindas da API para apresentação ao usuário.
/// Stack traces, nomes de exception, ProblemDetails crus em inglês e respostas HTTP
/// genéricas ("Bad Request", "Internal Server Error") são detectados como técnicos
/// e substituídos por uma mensagem amigável derivada do código de erro ou do status.
/// </summary>
public static class UserFacingErrors
{
    // Códigos conhecidos do backend → mensagens amigáveis.
    // Quando o backend devolve só o código (sem mensagem útil), traduzimos aqui
    // pra evitar toasts crípticos. A lista cresce conforme novos códigos aparecem.
    private static readonly Dictionary<string, string> KnownErrorMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CATEGORIA_INVALIDA"] = "Categoria inválida ou não encontrada.",
        ["CATEGORIA_DUPLICADA"] = "Já existe uma categoria com esse nome.",
        ["CATEGORIA_EM_USO"] = "Esta categoria está em uso por produtos e não pode ser excluída.",
        ["CNPJ_DUPLICADO"] = "Este CNPJ já está cadastrado.",
        ["CPF_DUPLICADO"] = "Este CPF já está cadastrado.",
        ["EMAIL_DUPLICADO"] = "Este e-mail já está em uso.",
        ["EMAIL_INVALIDO"] = "E-mail inválido.",
        ["DOCUMENTO_INVALIDO"] = "Documento (CPF/CNPJ) inválido.",
        ["SKU_DUPLICADO"] = "Já existe um produto com esse SKU.",
        ["PRODUTO_NAO_ENCONTRADO"] = "Produto não encontrado.",
        ["ESTOQUE_INSUFICIENTE"] = "Estoque insuficiente para esta operação.",
        ["FORNECEDOR_DUPLICADO"] = "Já existe um fornecedor com esse documento.",
        ["CLIENTE_DUPLICADO"] = "Já existe um cliente com esse documento.",
        ["CAIXA_JA_ABERTO"] = "O caixa do dia já está aberto.",
        ["CAIXA_NAO_ABERTO"] = "É necessário abrir o caixa antes de registrar movimentos.",
        ["CAIXA_FECHADO"] = "O caixa do dia já foi fechado.",
        ["EMPRESA_INVALIDA"] = "Loja não identificada. Selecione uma loja e tente novamente.",
        ["LOJA_NAO_SELECIONADA"] = "Selecione uma loja antes de continuar.",
        ["LOJA_DUPLICADA"] = "Já existe uma loja com esse nome.",
        ["VALIDATION_ERROR"] = "Há campos inválidos no formulário. Revise e tente novamente.",
        ["NOT_FOUND"] = "Recurso não encontrado.",
        ["PERMISSAO_INSUFICIENTE"] = "Você não tem permissão para esta ação.",
        ["AUTH_TOKEN_EXPIRED"] = "Sessão expirada. Faça login novamente.",
        ["LIMITE_PLANO"] = "Limite do seu plano atingido.",
        ["LIMITE_IA"] = "Cota de IA esgotada.",
        ["TIMEOUT"] = "O servidor demorou para responder. Tente novamente.",
        ["NETWORK_ERROR"] = "Não foi possível conectar ao servidor. Verifique sua conexão.",
        ["SERVER_ERROR"] = "Erro interno no servidor. Tente novamente em instantes."
    };

    // Placeholders genéricos do backend que devem ser substituídos.
    private static readonly HashSet<string> GenericApiPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Erro na requisicao.",
        "Erro na requisição.",
        "Erro na requisição",
        "Erro na requisicao",
        "Bad Request",
        "Internal Server Error",
        "Not Found",
        "Unauthorized",
        "Forbidden",
        "Conflict",
        "Unprocessable Entity",
        "An error has occurred.",
        "An error occurred.",
        "An error has occurred",
        "An error occurred",
        "Ocorreu um erro.",
        "Ocorreu um erro",
        "Erro inesperado.",
        "Erro inesperado",
        "null",
        "undefined"
    };

    // Padrões técnicos comuns: stack traces, nomes de Exception, paths de arquivo,
    // JSON/ProblemDetails crus, traços ANSI de runtime .NET ou Java.
    private static readonly Regex TechnicalPattern = new(
        @"(?ix)
            \bSystem\.[A-Z]\w+\.[A-Z]\w+   # System.Collections.Generic
          | \bMicrosoft\.\w+\.\w+           # Microsoft.EntityFrameworkCore...
          | \b\w+Exception\b                # NullReferenceException, SqlException
          | \bat\s+\w+\.\w+\.\w+            # at Foo.Bar.Baz (stack frame)
          | --->\s                          # ---> inner exception marker
          | \bStackTrace\b
          | \bInnerException\b
          | \.cs:line\s+\d+
          | \.csproj
          | \bSqlError\b
          | \bDbUpdateException\b
          | \bnull\s+reference
          | \btraceId\s*[:=]
        ",
        RegexOptions.Compiled);

    /// <summary>
    /// True se a mensagem parecer técnica (stack trace, nome de exception, ProblemDetails cru, etc.).
    /// Mensagens técnicas devem ser substituídas pelo fallback amigável antes de virarem toast.
    /// </summary>
    public static bool IsTechnical(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        var trimmed = message.Trim();

        if (GenericApiPlaceholders.Contains(trimmed)) return true;

        // JSON cru ou XML cru não é mensagem para humano.
        if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
            (trimmed.StartsWith('[') && trimmed.EndsWith(']')) ||
            trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            return true;

        // Mensagens longuíssimas quase sempre são dumps técnicos.
        if (trimmed.Length > 400) return true;

        return TechnicalPattern.IsMatch(trimmed);
    }

    /// <summary>
    /// Retorna uma mensagem amigável para o usuário, escolhendo na seguinte ordem:
    /// 1) mensagem da API se ela for legível (não técnica nem placeholder);
    /// 2) tradução amigável do código de erro conhecido;
    /// 3) fallback baseado no status HTTP.
    /// </summary>
    public static string Sanitize(string? code, string? message, int httpStatus)
    {
        if (!string.IsNullOrWhiteSpace(message) && !IsTechnical(message))
            return message.Trim();

        if (!string.IsNullOrWhiteSpace(code) && KnownErrorMessages.TryGetValue(code, out var known))
            return known;

        return FallbackForStatus(httpStatus);
    }

    /// <summary>
    /// Mensagem amigável padrão para um status HTTP, usada quando não há código nem mensagem úteis.
    /// </summary>
    public static string FallbackForStatus(int status) => status switch
    {
        400 => "Dados inválidos no formulário. Revise os campos e tente novamente.",
        401 => "Sessão expirada. Faça login novamente.",
        403 => "Você não tem permissão para esta ação.",
        404 => "Recurso não encontrado.",
        408 => "O servidor demorou para responder. Tente novamente.",
        409 => "Conflito de dados. Esse registro já existe ou está em uso.",
        422 => "Não foi possível processar — algum dado está inconsistente.",
        429 => "Muitas requisições em pouco tempo. Aguarde alguns instantes.",
        500 => "Erro interno no servidor. Tente novamente em alguns instantes.",
        501 => "Operação não suportada nesta versão. Avise o suporte com o que estava fazendo.",
        502 => "Servidor indisponível no momento. Tente novamente em instantes.",
        503 => "Serviço temporariamente indisponível. Tente novamente em instantes.",
        504 => "O servidor demorou para responder. Tente novamente.",
        0   => "Ocorreu um erro inesperado. Tente novamente.",
        _   => $"Não foi possível concluir a operação (erro {status}). Se persistir, contate o suporte."
    };
}
