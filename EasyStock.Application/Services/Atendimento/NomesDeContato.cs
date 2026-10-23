using EasyStock.Application.UseCases.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2004: o primeiro nome que o cliente lê numa mensagem, com uma regra só para o nome vazio e para o lead sem nome de
/// perfil (<see cref="IdentificarClientePorTelefoneUseCase.NomePadraoLead"/>): nenhum dos dois é nome de pessoa, então
/// ninguém é chamado de "Cliente" nem de "Cliente WhatsApp".
/// </summary>
public static class NomesDeContato
{
    /// <summary>O primeiro nome, ou nulo quando <paramref name="nome"/> é vazio ou é o nome padrão do lead.</summary>
    public static string? PrimeiroNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome) || nome.Trim() == IdentificarClientePorTelefoneUseCase.NomePadraoLead) return null;
        return nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }

    /// <summary>O primeiro nome, ou <paramref name="padrao"/> quando o contato não tem nome de pessoa.</summary>
    public static string PrimeiroNome(string? nome, string padrao) => PrimeiroNome(nome) ?? padrao;
}
