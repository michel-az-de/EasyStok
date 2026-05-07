using System;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Financeiro;

/// <summary>
/// Centro de custo opcional pra agrupar despesas/receitas por loja, departamento
/// ou projeto. <see cref="Codigo"/> e UNIQUE por empresa (validado no use case).
/// </summary>
public class CentroCusto
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid? LojaId { get; set; }

    public string Codigo { get; set; } = null!;
    public string Nome { get; set; } = null!;
    public string? Descricao { get; set; }

    public bool Ativo { get; set; } = true;

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public Empresa? Empresa { get; set; }
    public Loja? Loja { get; set; }

    public static CentroCusto Criar(Guid empresaId, string codigo, string nome, Guid? lojaId = null, string? descricao = null)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new RegraDeDominioVioladaException("Codigo do centro de custo nao pode ser vazio.");
        if (string.IsNullOrWhiteSpace(nome))
            throw new RegraDeDominioVioladaException("Nome do centro de custo nao pode ser vazio.");

        var agora = DateTime.UtcNow;
        return new CentroCusto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LojaId = lojaId,
            Codigo = codigo.Trim().ToUpperInvariant(),
            Nome = nome.Trim(),
            Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim(),
            Ativo = true,
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    public void Atualizar(string? nome, string? descricao, Guid? lojaId)
    {
        if (!string.IsNullOrWhiteSpace(nome)) Nome = nome.Trim();
        Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
        LojaId = lojaId;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Inativar()
    {
        if (!Ativo) return;
        Ativo = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Reativar()
    {
        if (Ativo) return;
        Ativo = true;
        AlteradoEm = DateTime.UtcNow;
    }
}
