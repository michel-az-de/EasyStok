using EasyStock.Domain.Entities.Banners;

namespace EasyStock.Application.UseCases.Banners;

/// <summary>Projeção do banner para o console Admin (listagem e detalhe).</summary>
public sealed record BannerAdminDto(
    Guid Id,
    string TituloInterno,
    string Tipo,
    bool Ativo,
    bool ExigeConfirmacao,
    bool VisualizacaoUnica,
    bool NotificarAoPublicar,
    DateTime? NotificadoEm,
    DateTime? InicioEm,
    DateTime? FimEm,
    int Prioridade,
    string? ImagemUrl,
    DateTime CriadoEm,
    DateTime AtualizadoEm)
{
    public static BannerAdminDto De(Banner b) => new(
        b.Id, b.TituloInterno, b.Tipo.ToString(), b.Ativo,
        b.ExigeConfirmacao, b.VisualizacaoUnica, b.NotificarAoPublicar, b.NotificadoEm,
        b.InicioEm, b.FimEm, b.Prioridade, b.ImagemUrl, b.CriadoEm, b.AtualizadoEm);
}

/// <summary>
/// Projeção do banner para o app Web (só o necessário para renderizar). Enums viram
/// strings estáveis; <c>FimEm</c> vai junto para o client descartar do replay o que venceu.
/// </summary>
public sealed record BannerWebDto(
    Guid Id,
    string Tipo,
    string? Corpo,
    string? ImagemUrl,
    string TamanhoModo,
    int? LarguraPx,
    int? AlturaPx,
    bool LinkAtivo,
    string? LinkUrl,
    bool NovaAba,
    bool TooltipAtivo,
    string? TooltipTexto,
    bool ExigeConfirmacao,
    bool VisualizacaoUnica,
    int Prioridade,
    DateTime? FimEm)
{
    public static BannerWebDto De(Banner b) => new(
        b.Id,
        b.Tipo == Domain.Enums.BannerTipo.Mensagem ? "mensagem" : "imagem",
        b.Corpo,
        b.ImagemUrl,
        b.TamanhoModo == Domain.Enums.BannerTamanhoModo.Manual ? "manual" : "herdado",
        b.LarguraPx,
        b.AlturaPx,
        b.LinkAtivo,
        b.LinkUrl,
        b.NovaAba,
        b.TooltipAtivo,
        b.TooltipTexto,
        b.ExigeConfirmacao,
        b.VisualizacaoUnica,
        b.Prioridade,
        b.FimEm);
}
