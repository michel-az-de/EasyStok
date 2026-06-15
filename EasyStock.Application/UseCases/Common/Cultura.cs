using System.Globalization;

namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Cultura pt-BR compartilhada para formatar valores (<c>:C</c>/<c>:N</c>) nos use cases
/// que rodam no processo da Api. A Api NAO fixa <c>DefaultThreadCurrentCulture</c> (ao
/// contrario de Web/Admin), entao um <c>:C</c> sem cultura cai em Invariant e renderiza o
/// simbolo de moeda generico no container. Ver issue 610 (QA v1.10 r3 BUG-008).
/// </summary>
public static class Cultura
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
}
