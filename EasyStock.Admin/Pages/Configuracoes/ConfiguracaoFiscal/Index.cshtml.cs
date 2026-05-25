using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Configuracoes.ConfiguracaoFiscal;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)]
    public Guid EmpresaId { get; set; }

    public string? EmpresaNome { get; set; }
    public bool Configurado { get; set; }
    public bool Habilitada { get; set; }
    public string? Ambiente { get; set; }
    public string? RegimeTributario { get; set; }
    public short SerieNfce { get; set; } = 1;
    public long ProximoNumeroNfce { get; set; } = 1;
    public bool TemCsc { get; set; }
    public string? CscId { get; set; }
    public bool TemCertificado { get; set; }
    public string? CertificadoValidoAte { get; set; }
    public int? CertificadoDiasParaExpirar { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (EmpresaId == Guid.Empty)
        {
            SetErro("EmpresaId obrigatorio.");
            return RedirectToPage("/Tenants/Index");
        }

        await CarregarEmpresaAsync();
        await CarregarConfigFiscalAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCertificadoAsync(IFormFile pfx, string senha)
    {
        if (EmpresaId == Guid.Empty) return RedirectToPage("/Tenants/Index");
        if (pfx is null || pfx.Length == 0) { SetErro("Arquivo .pfx obrigatorio."); return RedirectToPage(new { EmpresaId }); }
        if (string.IsNullOrEmpty(senha)) { SetErro("Senha do certificado obrigatoria."); return RedirectToPage(new { EmpresaId }); }

        try
        {
            using var ms = new MemoryStream();
            await pfx.CopyToAsync(ms);
            var bytes = ms.ToArray();

            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(bytes), "pfx", pfx.FileName);
            content.Add(new StringContent(senha), "senha");

            var result = await api.PostMultipartRawAsync($"api/configuracao-fiscal/certificado?empresaId={EmpresaId}", content);
            if (result.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "Erro ao enviar certificado.";
                SetErro(msg ?? "Erro ao enviar certificado.");
            }
            else
            {
                SetSucesso("Certificado A1 enviado com sucesso.");
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao enviar cert A1 para empresa {Empresa}", EmpresaId);
            SetErro("Falha ao enviar certificado. Verifique o arquivo e a senha.");
        }

        return RedirectToPage(new { EmpresaId });
    }

    public async Task<IActionResult> OnPostCscAsync(string cscId, string cscToken)
    {
        if (EmpresaId == Guid.Empty) return RedirectToPage("/Tenants/Index");

        try
        {
            var result = await api.PostRawAsync($"api/configuracao-fiscal/csc?empresaId={EmpresaId}",
                new { cscId, cscToken });

            if (result.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "Erro ao configurar CSC.";
                SetErro(msg ?? "Erro ao configurar CSC.");
            }
            else
            {
                SetSucesso("CSC configurado com sucesso.");
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao configurar CSC para empresa {Empresa}", EmpresaId);
            SetErro("Falha ao configurar CSC.");
        }

        return RedirectToPage(new { EmpresaId });
    }

    public async Task<IActionResult> OnPostSerieAmbienteAsync(string? ambiente, short? serieNfce)
    {
        if (EmpresaId == Guid.Empty) return RedirectToPage("/Tenants/Index");

        try
        {
            var result = await api.PostRawAsync($"api/configuracao-fiscal/serie-ambiente?empresaId={EmpresaId}",
                new { ambiente, serieNfce });

            if (result.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "Erro ao configurar serie/ambiente.";
                SetErro(msg ?? "Erro ao configurar serie/ambiente.");
            }
            else
            {
                SetSucesso("Serie e ambiente atualizados.");
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao configurar serie/ambiente para empresa {Empresa}", EmpresaId);
            SetErro("Falha ao configurar serie e ambiente.");
        }

        return RedirectToPage(new { EmpresaId });
    }

    public async Task<IActionResult> OnPostHabilitarAsync()
    {
        if (EmpresaId == Guid.Empty) return RedirectToPage("/Tenants/Index");

        try
        {
            var result = await api.PostRawAsync($"api/configuracao-fiscal/habilitar?empresaId={EmpresaId}", new { });
            if (result.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "Erro ao habilitar.";
                SetErro(msg ?? "Erro ao habilitar emissao.");
            }
            else
            {
                SetSucesso("Emissao NFC-e habilitada com sucesso!");
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao habilitar emissao para empresa {Empresa}", EmpresaId);
            SetErro("Falha ao habilitar emissao fiscal.");
        }

        return RedirectToPage(new { EmpresaId });
    }

    public async Task<IActionResult> OnPostDesabilitarAsync()
    {
        if (EmpresaId == Guid.Empty) return RedirectToPage("/Tenants/Index");

        try
        {
            await api.PostRawAsync($"api/configuracao-fiscal/desabilitar?empresaId={EmpresaId}", new { });
            SetSucesso("Emissao fiscal desabilitada.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao desabilitar emissao para empresa {Empresa}", EmpresaId);
            SetErro("Falha ao desabilitar emissao.");
        }

        return RedirectToPage(new { EmpresaId });
    }

    private async Task CarregarEmpresaAsync()
    {
        try
        {
            var raw = await api.GetRawAsync($"api/admin/tenants/{EmpresaId}");
            if (raw.TryGetProperty("data", out var d) && d.TryGetProperty("nomeFantasia", out var n))
                EmpresaNome = n.GetString();
            EmpresaNome ??= raw.TryGetProperty("data", out var d2) && d2.TryGetProperty("razaoSocial", out var r)
                ? r.GetString()
                : EmpresaId.ToString()[..8];
        }
        catch { EmpresaNome = EmpresaId.ToString()[..8]; }
    }

    private async Task CarregarConfigFiscalAsync()
    {
        try
        {
            var raw = await api.GetRawAsync($"api/configuracao-fiscal?empresaId={EmpresaId}");
            if (!raw.TryGetProperty("data", out var d)) return;

            Configurado = d.TryGetProperty("configurado", out var c) && c.GetBoolean();
            if (!Configurado) return;

            Habilitada = d.TryGetProperty("habilitada", out var h) && h.GetBoolean();
            Ambiente = d.TryGetProperty("ambiente", out var a) ? a.GetString() : null;
            RegimeTributario = d.TryGetProperty("regimeTributario", out var rt) ? rt.GetString() : null;
            SerieNfce = d.TryGetProperty("serieNfce", out var s) ? s.GetInt16() : (short)1;
            ProximoNumeroNfce = d.TryGetProperty("proximoNumeroNfce", out var pn) ? pn.GetInt64() : 1;
            TemCsc = d.TryGetProperty("temCsc", out var tc) && tc.GetBoolean();
            CscId = d.TryGetProperty("cscId", out var ci) ? ci.GetString() : null;

            if (d.TryGetProperty("certificado", out var cert) && cert.ValueKind == JsonValueKind.Object)
            {
                TemCertificado = true;
                CertificadoValidoAte = cert.TryGetProperty("validoAte", out var va) ? va.GetDateTime().ToString("dd/MM/yyyy") : null;
                CertificadoDiasParaExpirar = cert.TryGetProperty("diasParaExpirar", out var dp) ? dp.GetInt32() : null;
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar config fiscal para empresa {Empresa}", EmpresaId);
        }
    }
}
