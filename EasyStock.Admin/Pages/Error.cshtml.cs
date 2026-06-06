using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EasyStock.Admin.Pages;

/// <summary>
/// Página de erro amigável (404/403/401/500...). Alvo de UseExceptionHandler("/Error")
/// e UseStatusCodePagesWithReExecute.
///
/// NÃO herda AdminPageBase de propósito: precisa renderizar para usuário anônimo ou com
/// sessão expirada SEM cair em loop de redirecionamento para o login. É standalone
/// (Layout=null) para ser à prova de falha — não depende do app shell nem da sessão.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    public int Code { get; private set; } = 500;
    public string Titulo { get; private set; } = "Algo deu errado";
    public string Mensagem { get; private set; } =
        "Tivemos um problema ao processar sua solicitação. Tente novamente; se persistir, contate o suporte.";
    public string? CorrelationId { get; private set; }

    public void OnGet(int? code) => Aplicar(code);

    // O re-execute do StatusCodePages preserva o método; um POST que dá 404 reentra como POST.
    public void OnPost(int? code) => Aplicar(code);

    private void Aplicar(int? code)
    {
        Code = code is null or < 400 ? 500 : code.Value;
        CorrelationId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        switch (Code)
        {
            case 404:
                Titulo = "Página não encontrada";
                Mensagem = "A página que você procurou não existe ou foi movida.";
                break;
            case 403:
                Titulo = "Acesso negado";
                Mensagem = "Você não tem permissão para acessar este recurso.";
                break;
            case 401:
                Titulo = "Sessão expirada";
                Mensagem = "Faça login novamente para continuar.";
                break;
            default:
                Titulo = "Algo deu errado";
                Mensagem = "Tivemos um problema ao processar sua solicitação. Tente novamente; se persistir, contate o suporte.";
                break;
        }
    }
}
