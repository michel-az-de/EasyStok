using EasyStock.Application.UseCases.Faturas.Common;
using EasyStock.Application.UseCases.Faturas.EmitirFatura;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Api.Services.Faturacao;

/// <summary>
/// Constroi <see cref="EmitirFaturaCommand"/> para faturas auto-geradas pelo
/// SaaS (Origem=Assinatura). Encapsula a logica de snapshot de
/// <see cref="DadosEmissor"/> (a empresa SaaS — EasyStock) e
/// <see cref="DadosFaturado"/> (a empresa cliente/tenant).
///
/// <para>
/// Os dados do emissor sao lidos de configuration <c>Saas:Emissor:*</c>
/// (appsettings ou env vars). Fallback: nome "EasyStock" sem documento. Assim
/// vamos para producao sem quebrar e empresas multi-region podem sobrescrever.
/// </para>
///
/// <para>
/// Os dados do faturado vem da <see cref="Empresa"/> entity — snapshot
/// preserva <c>Nome</c> e <c>Documento</c> mesmo se a empresa for editada
/// depois.
/// </para>
/// </summary>
public sealed class FaturaSaasFactory(IConfiguration configuration)
{
    private DadosEmissor BuildEmissor()
    {
        var section = configuration.GetSection("Saas:Emissor");
        return new DadosEmissor(
            Nome: section["Nome"] ?? "EasyStock",
            Documento: section["Documento"],
            RazaoSocial: section["RazaoSocial"],
            InscricaoMunicipal: section["InscricaoMunicipal"],
            InscricaoEstadual: section["InscricaoEstadual"],
            RegimeTributario: section["RegimeTributario"],
            Endereco: BuildEnderecoSection(section.GetSection("Endereco")),
            Email: section["Email"],
            Telefone: section["Telefone"]
        );
    }

    private static Endereco? BuildEnderecoSection(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        return new Endereco(
            Logradouro: section["Logradouro"],
            Numero: section["Numero"],
            Complemento: section["Complemento"],
            Bairro: section["Bairro"],
            Cidade: section["Cidade"],
            Uf: section["Uf"],
            Cep: section["Cep"],
            Pais: section["Pais"] ?? "BR"
        );
    }

    private static DadosFaturado BuildFaturado(Empresa empresa) => new(
        Nome: empresa.Nome,
        Documento: empresa.Documento
    );

    /// <summary>
    /// Constroi o command de emissao de fatura para uma cobranca de assinatura
    /// recem-criada. Vencimento sera <c>DateTime.UtcNow + 1 dia</c> (alinhado
    /// com a expiracao do Pix do gateway).
    /// </summary>
    public EmitirFaturaCommand BuildParaAssinatura(
        AssinaturaEmpresa assinatura,
        Plano plano,
        Empresa empresa,
        DateTime? dataEmissao = null,
        DateTime? dataVencimento = null)
    {
        ArgumentNullException.ThrowIfNull(assinatura);
        ArgumentNullException.ThrowIfNull(plano);
        ArgumentNullException.ThrowIfNull(empresa);

        var emissao = dataEmissao ?? DateTime.UtcNow;
        var venc = dataVencimento ?? emissao.AddDays(7);

        var item = new FaturaItemInput(
            Descricao: $"Assinatura EasyStock — {plano.Nome}",
            Quantidade: 1,
            PrecoUnitario: plano.PrecoMensal,
            Tipo: TipoItemFatura.Recorrencia
        );

        // Observacao: DataFim e null durante trial — anotar isso de forma legivel
        // (ao inves de "Plano vigente ate ." com data vazia).
        var observacao = assinatura.DataFim.HasValue
            ? $"Plano vigente ate {assinatura.DataFim.Value:yyyy-MM-dd}."
            : assinatura.TrialFim.HasValue
                ? $"Periodo de avaliacao ate {assinatura.TrialFim.Value:yyyy-MM-dd}."
                : "Plano vigente.";

        return new EmitirFaturaCommand(
            EmpresaId: empresa.Id,
            DadosFaturado: BuildFaturado(empresa),
            DadosEmissor: BuildEmissor(),
            Origem: OrigemFatura.Assinatura,
            DataVencimento: venc,
            Itens: new[] { item },
            ClienteId: null,
            OrigemRefId: assinatura.Id,
            Observacoes: observacao,
            DataEmissao: emissao,
            IdempotentePorOrigem: true,
            OrigemRegistro: "saas-job"
        );
    }
}
