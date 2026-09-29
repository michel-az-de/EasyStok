using System.Globalization;

namespace EasyStock.Domain.Tests.ValueObjects;

/// <summary>
/// Troca a <see cref="CultureInfo.CurrentCulture"/> durante o teste e restaura no Dispose.
/// Fixa a cultura para que o resultado não dependa da máquina (issue 1075: o CI Linux roda
/// invariante e o Windows do dev roda pt-BR).
/// </summary>
internal sealed class CulturaCorrente : IDisposable
{
    private readonly CultureInfo _anterior = CultureInfo.CurrentCulture;

    private CulturaCorrente(string nome) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(nome);

    public static CulturaCorrente PtBr() => new("pt-BR");

    public void Dispose() => CultureInfo.CurrentCulture = _anterior;
}
