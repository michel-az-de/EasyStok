namespace EasyStock.Application.Ports.Output.Atendimento.Email;

/// <summary>
/// Caixa de suporte da loja (#1432): o endereço que recebe os e-mails dos clientes por IMAP e responde por SMTP,
/// em nome dela. Payload cifrado em <c>credencial_integracao</c> (categoria Mensageria, provider
/// <see cref="ProviderKey"/>): a senha nunca sai daqui para a tela nem para o log.
/// </summary>
public sealed record CaixaEmailAtendimento(
    string Endereco,
    string? NomeExibicao,
    string ImapHost,
    int ImapPorta,
    string SmtpHost,
    int SmtpPorta,
    string Usuario,
    string Senha,
    DateTime AtualizadaEm)
{
    public const string ProviderKey = "caixa-email";

    /// <summary>Sem a senha: o <c>ToString</c> gerado do record a imprimiria em qualquer log que interpolasse a caixa.</summary>
    public override string ToString() => $"CaixaEmailAtendimento {{ Endereco = {Endereco} }}";
}
