using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>cadastrar_cliente(nome, email)</c> (#3105): o agente completa o cadastro do cliente já vinculado à conversa com
/// o que o próprio cliente informou. Não vincula conversa, não recebe telefone nem endereço (o endereço tem
/// <c>validar_endereco</c> e <c>confirmar_endereco</c>). Nome que a equipe ou o cliente escreveu nunca é trocado,
/// e-mail de outro cadastro não entra, e toda troca fica no histórico do cliente. O commit é do turno do agente.
/// </summary>
public sealed class CadastrarClienteFerramenta(
    IClienteCrmRepository crm, IClienteRepository clientes, IEmailAtendimentoQuery emails) : IFerramentaAgente
{
    public const string Origem = "assistente";

    private const int NomeTamanhoMaximo = 150;
    private const int EmailTamanhoMaximo = 255;

    public string Nome => "cadastrar_cliente";

    public string Descricao =>
        "Grava no cadastro o nome completo e o e-mail que o próprio cliente informou na conversa. Use sem pedir permissão " +
        "quando ele disser o nome ou o e-mail dele. Nunca grave dado de outra pessoa. Nome já conferido pela equipe e e-mail " +
        "já cadastrado são mantidos. Endereço não entra aqui: use validar_endereco e confirmar_endereco.";

    public string SchemaJson =>
        $$$"""{"type":"object","properties":{"nome":{"type":"string","maxLength":{{{NomeTamanhoMaximo}}}},"email":{"type":"string","maxLength":{{{EmailTamanhoMaximo}}}}},"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (contexto.Conversa.ClienteId is not { } clienteId || contexto.Conversa.VinculoEmRevisao)
            return Erro("cliente_nao_identificado");

        var nome = FerramentaJson.LerTexto(entrada, "nome")?.Trim();
        var email = FerramentaJson.LerTexto(entrada, "email")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(nome) && string.IsNullOrEmpty(email))
            return Erro("nada_para_gravar");

        // Tudo é conferido antes de gravar: erro num campo não deixa o outro pela metade no commit do turno.
        if (!string.IsNullOrEmpty(nome) && !EhNomeDePessoa(nome))
            return Erro("nome_invalido");
        if (!string.IsNullOrEmpty(email) && !EhEmailValido(email))
            return Erro("email_invalido");

        var cliente = await crm.ObterComTagsAsync(contexto.EmpresaId, clienteId, ct);
        if (cliente is null)
            return Erro("cliente_nao_identificado");

        var gravarEmail = !string.IsNullOrEmpty(email) && string.IsNullOrWhiteSpace(cliente.Email);
        // O e-mail recebido pela loja é ligado ao cadastro que tem o endereço: o de outra pessoa não entra aqui.
        if (gravarEmail && await emails.ObterClienteIdPorEmailAsync(contexto.EmpresaId, email!, ct) is { } dono && dono != cliente.Id)
            return Erro("email_em_uso");

        string? situacaoNome = null, situacaoEmail = null;
        if (!string.IsNullOrEmpty(nome))
        {
            situacaoNome = "mantido";
            if (PodeTrocarNome(cliente.Nome, contexto.Conversa.ContatoNome) && !string.Equals(nome, cliente.Nome?.Trim(), StringComparison.Ordinal))
            {
                await AlterarAsync(cliente, "Nome", cliente.Nome, nome, contexto.Agora, c => c.Nome = nome);
                situacaoNome = "gravado";
            }
        }
        if (!string.IsNullOrEmpty(email))
        {
            situacaoEmail = "mantido";
            if (gravarEmail)
            {
                await AlterarAsync(cliente, "Email", cliente.Email, email, contexto.Agora, c => c.Email = email);
                situacaoEmail = "gravado";
            }
        }

        return FerramentaJson.Serializar(new { nome = situacaoNome, email = situacaoEmail });
    }

    /// <summary>
    /// Nome que ainda pode ser trocado: o provisório do lead, o genérico do chat do site ou o apelido que veio do perfil do
    /// WhatsApp. Qualquer outro foi escrito pela equipe ou pelo cliente e fica.
    /// </summary>
    private static bool PodeTrocarNome(string? atual, string? nomeDoPerfil) =>
        PerfilClienteWhatsApp.EhNomeProvisorio(atual)
        || string.Equals(atual!.Trim(), ConversaChatSiteService.NomeSemFormulario, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(nomeDoPerfil) && string.Equals(atual.Trim(), nomeDoPerfil.Trim(), StringComparison.Ordinal));

    private static bool EhNomeDePessoa(string nome) =>
        nome.Length <= NomeTamanhoMaximo && nome.Count(char.IsLetter) >= 2 && !nome.Any(char.IsDigit) && !nome.Contains('@');

    private static bool EhEmailValido(string email)
    {
        if (email.Length > EmailTamanhoMaximo) return false;
        try
        {
            return ContatoInformadoVisitante.EmailNormalizado(email) is not null;
        }
        catch (RegraDeDominioVioladaException)
        {
            return false;
        }
    }

    private Task AlterarAsync(Cliente cliente, string campo, string? antigo, string novo, DateTime agora, Action<Cliente> aplicar)
    {
        aplicar(cliente);
        cliente.AlteradoEm = agora;
        return clientes.AddAlteracaoAsync(new ClienteAlteracao
        {
            Id = Guid.NewGuid(), EmpresaId = cliente.EmpresaId, ClienteId = cliente.Id, AlteradoPorNome = "Assistente",
            Campo = campo, ValorAntigo = antigo, ValorNovo = novo, AlteradoEm = agora, Origem = Origem
        });
    }

    private static string Erro(string codigo) => FerramentaJson.Serializar(new { erro = codigo });
}
