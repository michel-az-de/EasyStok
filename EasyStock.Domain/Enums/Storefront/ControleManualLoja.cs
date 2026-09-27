namespace EasyStock.Domain.Enums.Storefront;

/// <summary>
/// Controle manual da loja (S40): vence o relógio e não volta sozinho. Espelha o
/// <c>lojaAberta</c> do protótipo (<c>null</c>/<c>true</c>/<c>false</c>).
/// </summary>
public enum ControleManualLoja
{
    Automatico = 0,
    ForcarAberta = 1,
    ForcarFechada = 2
}
