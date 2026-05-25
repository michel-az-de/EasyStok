using EasyStock.Domain.Enums.Notifications;
using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Domain.Entities.Notifications;

public class TemplateNotificacao
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nome { get; set; } = null!;
    public CanalNotificacao Canal { get; set; }
    public TipoEventoNotificacao TipoEvento { get; set; }
    public string AssuntoTemplate { get; set; } = string.Empty;
    public string CorpoTemplate { get; set; } = null!;
    public string Idioma { get; set; } = "pt-BR";
    public bool Ativo { get; set; }
    public bool Aprovado { get; set; }
    public Guid? EmpresaId { get; set; }
    public int Versao { get; set; } = 1;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public string AtualizadoPor { get; set; } = "system";

    public Empresa? Empresa { get; set; }

    public static TemplateNotificacao Criar(
        string codigo,
        string nome,
        CanalNotificacao canal,
        TipoEventoNotificacao tipoEvento,
        string assuntoTemplate,
        string corpoTemplate,
        Guid? empresaId = null,
        string idioma = "pt-BR",
        string criadoPor = "system")
    {
        var agora = DateTime.UtcNow;
        var template = new TemplateNotificacao
        {
            Id = Guid.NewGuid(),
            Codigo = codigo,
            Nome = nome,
            Canal = canal,
            TipoEvento = tipoEvento,
            AssuntoTemplate = assuntoTemplate,
            CorpoTemplate = corpoTemplate,
            Idioma = idioma,
            Ativo = false,
            Aprovado = false,
            EmpresaId = empresaId,
            Versao = 1,
            CriadoEm = agora,
            AtualizadoEm = agora,
            AtualizadoPor = criadoPor
        };
        template.RecomputarChecksum();
        return template;
    }

    public void AtualizarConteudo(string assunto, string corpo, string atualizadoPor)
    {
        AssuntoTemplate = assunto;
        CorpoTemplate = corpo;
        AtualizadoEm = DateTime.UtcNow;
        AtualizadoPor = atualizadoPor;
        Aprovado = false;
        Ativo = false;
        RecomputarChecksum();
    }

    public void Aprovar(string adminEmail)
    {
        Aprovado = true;
        AtualizadoPor = adminEmail;
        AtualizadoEm = DateTime.UtcNow;
    }

    public void Ativar()
    {
        if (!Aprovado)
            throw new InvalidOperationException("Template precisa estar aprovado antes de ativar.");
        Ativo = true;
        AtualizadoEm = DateTime.UtcNow;
    }

    public void Desativar()
    {
        Ativo = false;
        AtualizadoEm = DateTime.UtcNow;
    }

    private void RecomputarChecksum()
    {
        var conteudo = $"{AssuntoTemplate}\n---\n{CorpoTemplate}";
        var bytes = Encoding.UTF8.GetBytes(conteudo);
        var hash = SHA256.HashData(bytes);
        ChecksumSha256 = Convert.ToHexString(hash);
    }
}
