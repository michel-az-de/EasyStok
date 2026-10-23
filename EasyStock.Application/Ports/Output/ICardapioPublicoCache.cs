namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Cardápio público montado e guardado na memória do servidor que o atende (<c>MenuController</c>). Quem grava o
/// cardápio (prato, categoria, disponibilidade, foto) invalida pelo slug da vitrine, em vez de esperar o prazo (#2002).
/// Fica opcional nos use cases: só a API serve o menu, então os outros hosts não registram.
/// </summary>
public interface ICardapioPublicoCache
{
    void Invalidar(string slug);
}
