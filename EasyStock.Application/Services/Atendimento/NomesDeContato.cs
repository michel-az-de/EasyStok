using EasyStock.Application.UseCases.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2004 e #2018: o primeiro nome de um contato, numa regra só para a mensagem ao cliente, o aviso à equipe, a comanda
/// e a impressão. Três coisas não são nome de pessoa: o nome vazio, o lead sem nome de perfil
/// (<see cref="IdentificarClientePorTelefoneUseCase.NomePadraoLead"/>) e o "nome" que traz dígito (o WhatsApp preenche o
/// perfil com o próprio telefone). Nesses casos o primeiro nome é nulo, e cada tela escolhe o seu padrão.
/// </summary>
public static class NomesDeContato
{
    /// <summary>
    /// O primeiro nome, ou nulo quando <paramref name="nome"/> é vazio, é o nome padrão do lead ou começa por uma palavra
    /// com dígito (telefone, "+55 11 98888-7777").
    /// </summary>
    public static string? PrimeiroNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome) || nome.Trim() == IdentificarClientePorTelefoneUseCase.NomePadraoLead) return null;
        var primeiro = nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return primeiro.Any(char.IsDigit) ? null : primeiro;
    }

    /// <summary>O primeiro nome, ou <paramref name="padrao"/> quando o contato não tem nome de pessoa.</summary>
    public static string PrimeiroNome(string? nome, string padrao) => PrimeiroNome(nome) ?? padrao;
}
