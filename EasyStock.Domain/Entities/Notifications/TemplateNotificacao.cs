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

    /// <summary>
    /// Metadados do envio, objeto JSON de strings com expressões Scriban renderizadas com as mesmas variáveis
    /// do corpo (S13). No WhatsApp da Meta: <c>template</c> (nome aprovado no WhatsApp Manager), <c>idioma</c>
    /// e <c>param1..N</c>, usados fora da janela de 24 h. Nulo: o canal usa só o corpo.
    /// </summary>
    public string? MetadadosJson { get; set; }

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

    public void DefinirMetadados(string? metadadosJson)
    {
        MetadadosJson = string.IsNullOrWhiteSpace(metadadosJson) ? null : metadadosJson.Trim();
        AtualizadoEm = DateTime.UtcNow;
    }

    /// <summary>Versão da entrada do catálogo (N13): o seed cria a linha nova quando a do catálogo passa da gravada.</summary>
    public void DefinirVersao(int versao)
    {
        if (versao < 1) throw new ArgumentOutOfRangeException(nameof(versao));
        Versao = versao;
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
