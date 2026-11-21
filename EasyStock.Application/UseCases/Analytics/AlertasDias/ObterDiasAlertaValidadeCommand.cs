namespace EasyStock.Application.UseCases.Analytics.AlertasDias;

/// <summary><paramref name="EmpresaId"/> amarra a leitura da configuração: <c>configuracoes_loja</c> não tem tenant (#2458).</summary>
public sealed record ObterDiasAlertaValidadeCommand(
    Guid EmpresaId,
    Guid? LojaId = null);
