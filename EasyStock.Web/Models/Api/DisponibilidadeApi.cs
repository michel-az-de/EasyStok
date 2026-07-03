namespace EasyStock.Web.Models.Api;

/// <summary>
/// Resposta de empresas/email-disponivel e empresas/cnpj-disponivel (signup, issue 800).
/// </summary>
public record DisponibilidadeApi(bool Disponivel);
