namespace EasyStock.Application.Ports.Output.Atendimento.Email;

/// <summary>
/// Caixa de suporte e fio do e-mail da empresa do tenant corrente (#1432), para o envio do canal E-mail. O
/// tenant vem do mesmo lugar que o resto da requisição usa (JWT no console, override nos jobs), como em
/// <see cref="IRemetenteWhatsApp"/>.
/// </summary>
public interface ICaixaEmailDoTenant
{
    /// <summary>Caixa configurada da empresa corrente, ou nula sem tenant ou sem caixa (o envio usa o e-mail da plataforma).</summary>
    Task<CaixaEmailAtendimento?> ObterCaixaAsync(CancellationToken ct = default);

    /// <summary>Assunto e último Message-ID recebido da conversa aberta do contato no canal E-mail; nulo sem conversa.</summary>
    Task<FioEmail?> ObterFioAsync(string contatoEmail, CancellationToken ct = default);
}

/// <summary>O que a resposta precisa para cair na mesma conversa no programa de e-mail do cliente.</summary>
public sealed record FioEmail(string? Assunto, string? UltimoMessageIdRecebido);
