using System.Text.RegularExpressions;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2155: cada mensagem do WhatsApp completa o cadastro do cliente com o que ela traz, sem apagar o que a loja já
/// sabe. O nome do perfil troca só o nome provisório do lead; o e-mail entra só no campo vazio; a localização vira
/// nota com o link do mapa. Participa da transação do webhook: não faz commit.
/// </summary>
public sealed partial class PerfilClienteWhatsApp(IClienteRepository clientes, IClienteCrmRepository crm)
{
    public const string Origem = "whatsapp";

    private const int NomeTamanhoMaximo = 150;
    private const int EmailTamanhoMaximo = 255;

    /// <returns>O cliente da conversa, ou <c>null</c> sem cliente vinculado.</returns>
    public async Task<ClienteEntity?> AplicarAsync(Guid empresaId, Guid? clienteId, ClienteEntity? jaCarregado,
        string? nomePerfil, MensagemRecebidaWhatsApp mensagem, Guid mensagemId, DateTime agora, CancellationToken ct)
    {
        var cliente = jaCarregado ?? (clienteId is { } id ? await clientes.GetByIdAsync(empresaId, id) : null);
        if (cliente is null || cliente.EmpresaId != empresaId) return null;

        if (NomeParaTrocar(cliente.Nome, nomePerfil) is { } nome)
            await AlterarAsync(cliente, "Nome", cliente.Nome, nome, agora, c => c.Nome = nome);

        if (string.IsNullOrWhiteSpace(cliente.Email) && EmailDoTexto(mensagem.TextoCorpo) is { } email)
            await AlterarAsync(cliente, "Email", cliente.Email, email, agora, c => c.Email = email);

        if (mensagem.Localizacao is { } local)
            await crm.AdicionarNotaAsync(
                ClienteNota.Criar(empresaId, cliente.Id, TextoDaNota(local), Origem, agora, mensagemId: mensagemId), ct);

        return cliente;
    }

    /// <summary>
    /// Nome do perfil que substitui o atual: só quando o atual é provisório (o padrão do lead, ou sem nenhuma letra,
    /// como o telefone) e o perfil traz um nome com letras. Nome que a loja ou o cliente escreveu nunca é trocado.
    /// </summary>
    public static string? NomeParaTrocar(string? atual, string? nomePerfil)
    {
        var novo = nomePerfil?.Trim();
        if (string.IsNullOrEmpty(novo) || novo.Count(char.IsLetter) < 2) return null;
        if (!EhNomeProvisorio(atual)) return null;
        if (novo.Length > NomeTamanhoMaximo) novo = novo[..NomeTamanhoMaximo];
        return string.Equals(novo, atual?.Trim(), StringComparison.Ordinal) ? null : novo;
    }

    public static bool EhNomeProvisorio(string? nome) =>
        string.IsNullOrWhiteSpace(nome)
        || string.Equals(nome.Trim(), IdentificarClientePorTelefoneUseCase.NomePadraoLead, StringComparison.OrdinalIgnoreCase)
        || !nome.Any(char.IsLetter);

    /// <summary>
    /// O e-mail que o cliente escreveu, em minúsculas. Texto com dois e-mails diferentes não decide nada: pode ser o
    /// de outra pessoa.
    /// </summary>
    public static string? EmailDoTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var achados = Email().Matches(texto)
            .Select(m => m.Value.TrimEnd('.').ToLowerInvariant())
            .Where(e => e.Length <= EmailTamanhoMaximo)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return achados.Count == 1 ? achados[0] : null;
    }

    /// <summary>"Localização enviada pelo WhatsApp: lugar, endereço. Mapa: link", dentro do limite da nota.</summary>
    public static string TextoDaNota(LocalizacaoWhatsApp local)
    {
        var mapa = $"https://www.google.com/maps?q={local.Coordenadas}";
        var descricao = string.Join(", ", new[] { local.Nome, local.Endereco }.Where(p => p is not null));
        var prefixo = "Localização enviada pelo WhatsApp";
        var sufixo = $". Mapa: {mapa}";
        var espaco = ClienteNota.TextoTamanhoMaximo - prefixo.Length - sufixo.Length - 2;
        if (descricao.Length > espaco) descricao = descricao[..Math.Max(0, espaco - 1)] + "…";
        return descricao.Length == 0 ? prefixo + sufixo : $"{prefixo}: {descricao}{sufixo}";
    }

    private Task AlterarAsync(ClienteEntity cliente, string campo, string? antigo, string novo, DateTime agora,
        Action<ClienteEntity> aplicar)
    {
        aplicar(cliente);
        cliente.AlteradoEm = agora;
        return clientes.AddAlteracaoAsync(new ClienteAlteracao
        {
            Id = Guid.NewGuid(), EmpresaId = cliente.EmpresaId, ClienteId = cliente.Id, AlteradoPorNome = "WhatsApp",
            Campo = campo, ValorAntigo = antigo, ValorNovo = novo, AlteradoEm = agora, Origem = Origem
        });
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Email();
}
