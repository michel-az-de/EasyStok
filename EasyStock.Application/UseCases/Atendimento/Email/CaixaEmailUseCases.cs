using System.Net.Mail;
using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.UseCases.Atendimento.Email;

/// <summary>
/// O que a tela Integrações &gt; E-mail manda (#1432). <see cref="Senha"/> vazia mantém a gravada: a tela nunca
/// recebe a senha de volta, então não tem como reenviá-la.
/// </summary>
public sealed record CaixaEmailEntrada(
    string? Endereco,
    string? NomeExibicao,
    string? ImapHost,
    int? ImapPorta,
    string? SmtpHost,
    int? SmtpPorta,
    string? Usuario,
    string? Senha)
{
    /// <summary>Sem a senha, pelo mesmo motivo de <see cref="CaixaEmailAtendimento.ToString"/>.</summary>
    public override string ToString() => $"CaixaEmailEntrada {{ Endereco = {Endereco} }}";
}

/// <summary>A caixa como a tela mostra: tudo menos a senha, que vira <see cref="SenhaDefinida"/>.</summary>
public sealed record CaixaEmailResumo(
    string Endereco,
    string? NomeExibicao,
    string ImapHost,
    int ImapPorta,
    string SmtpHost,
    int SmtpPorta,
    string Usuario,
    bool SenhaDefinida,
    DateTime AtualizadaEm)
{
    internal static CaixaEmailResumo De(CaixaEmailAtendimento c) => new(
        c.Endereco, c.NomeExibicao, c.ImapHost, c.ImapPorta, c.SmtpHost, c.SmtpPorta, c.Usuario,
        !string.IsNullOrEmpty(c.Senha), c.AtualizadaEm);
}

/// <summary>Caixa de suporte gravada da empresa, sem a senha; nula quando não há.</summary>
public sealed class ObterCaixaEmailUseCase(IIntegrationCredentialResolver credenciais)
{
    public async Task<CaixaEmailResumo?> ExecuteAsync(Guid empresaId, CancellationToken ct = default) =>
        await CaixaEmailGravada.ObterAsync(credenciais, empresaId, ct) is { } caixa ? CaixaEmailResumo.De(caixa) : null;
}

/// <summary>Grava a caixa cifrada (nova versão da credencial; a anterior fica inativa).</summary>
public sealed class SalvarCaixaEmailUseCase(IIntegrationCredentialResolver credenciais, TimeProvider relogio)
{
    public async Task<CaixaEmailResumo> ExecuteAsync(
        Guid empresaId, Guid usuarioId, CaixaEmailEntrada entrada, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty)
            throw new UseCaseValidationException("Empresa não identificada.");

        var atual = await CaixaEmailGravada.ObterAsync(credenciais, empresaId, ct);
        var caixa = CaixaEmailGravada.Montar(entrada, atual?.Senha, relogio.GetUtcNow().UtcDateTime);

        await credenciais.SalvarAsync(empresaId, CategoriaIntegracao.Mensageria, CaixaEmailAtendimento.ProviderKey,
            AmbienteIntegracao.Production, caixa, usuarioId, ct: ct);
        return CaixaEmailResumo.De(caixa);
    }
}

/// <summary>
/// "Testar conexão": com os dados da tela (senha vazia usa a gravada) ou, sem eles, com a caixa gravada. Entra
/// no IMAP e no SMTP e sai; não lê nem envia nada.
/// </summary>
public sealed class TestarCaixaEmailUseCase(IIntegrationCredentialResolver credenciais, ICaixaEmailCliente cliente, TimeProvider relogio)
{
    public async Task<ResultadoTesteCaixaEmail> ExecuteAsync(
        Guid empresaId, CaixaEmailEntrada? entrada, CancellationToken ct = default)
    {
        var atual = await CaixaEmailGravada.ObterAsync(credenciais, empresaId, ct);
        var caixa = entrada is null
            ? atual ?? throw new UseCaseValidationException("A caixa de e-mail ainda não foi configurada.")
            : CaixaEmailGravada.Montar(entrada, atual?.Senha, relogio.GetUtcNow().UtcDateTime);
        return await cliente.TestarAsync(caixa, ct);
    }
}

/// <summary>Leitura e validação da caixa, comuns aos três casos de uso.</summary>
internal static class CaixaEmailGravada
{
    public const int TamanhoMaximoTexto = 254;
    public const int TamanhoMaximoNome = 120;
    public const int TamanhoMaximoSenha = 512;

    public static Task<CaixaEmailAtendimento?> ObterAsync(
        IIntegrationCredentialResolver credenciais, Guid empresaId, CancellationToken ct) =>
        credenciais.ObterAsync<CaixaEmailAtendimento>(
            empresaId, CaixaEmailAtendimento.ProviderKey, AmbienteIntegracao.Production, ct);

    public static CaixaEmailAtendimento Montar(CaixaEmailEntrada entrada, string? senhaGravada, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        var endereco = Obrigatorio(entrada.Endereco, "Endereço da caixa").ToLowerInvariant();
        if (!EnderecoValido(endereco))
            throw new UseCaseValidationException("Endereço da caixa inválido: use o e-mail completo, como contato@sualoja.com.");

        var nome = string.IsNullOrWhiteSpace(entrada.NomeExibicao) ? null : entrada.NomeExibicao.Trim();
        if (nome is { Length: > TamanhoMaximoNome })
            throw new UseCaseValidationException($"Nome de exibição passa de {TamanhoMaximoNome} caracteres.");

        var senha = string.IsNullOrEmpty(entrada.Senha) ? senhaGravada : entrada.Senha;
        if (string.IsNullOrEmpty(senha))
            throw new UseCaseValidationException("Informe a senha da caixa.");
        if (senha.Length > TamanhoMaximoSenha)
            throw new UseCaseValidationException("Senha longa demais.");

        return new CaixaEmailAtendimento(
            endereco, nome,
            Host(entrada.ImapHost, "Servidor IMAP"), Porta(entrada.ImapPorta, "Porta IMAP"),
            Host(entrada.SmtpHost, "Servidor SMTP"), Porta(entrada.SmtpPorta, "Porta SMTP"),
            Obrigatorio(entrada.Usuario, "Usuário"), senha, agora);
    }

    private static string Obrigatorio(string? valor, string campo)
    {
        var limpo = valor?.Trim() ?? "";
        if (limpo.Length == 0)
            throw new UseCaseValidationException($"{campo} é obrigatório.");
        if (limpo.Length > TamanhoMaximoTexto)
            throw new UseCaseValidationException($"{campo} passa de {TamanhoMaximoTexto} caracteres.");
        return limpo;
    }

    private static string Host(string? valor, string campo)
    {
        var host = Obrigatorio(valor, campo).ToLowerInvariant();
        if (host.Any(c => char.IsWhiteSpace(c) || c is '/' or ':' or '@'))
            throw new UseCaseValidationException($"{campo} inválido: só o nome do servidor, como imap.hostinger.com.");
        return host;
    }

    private static int Porta(int? valor, string campo) =>
        valor is >= 1 and <= 65535 ? valor.Value : throw new UseCaseValidationException($"{campo} inválida.");

    private static bool EnderecoValido(string endereco)
    {
        try
        {
            var lido = new MailAddress(endereco);
            return string.Equals(lido.Address, endereco, StringComparison.OrdinalIgnoreCase) && endereco.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
