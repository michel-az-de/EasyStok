using System;
using System.Collections.Generic;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Financeiro;

/// <summary>
/// Categoria do plano de contas operacional do tenant. Hierarquica ate 3 niveis
/// (raiz, sub, sub-sub). NAO e plano contabil contabil/DRE — e categorizacao
/// pratica pra dashboard de fluxo de caixa.
/// </summary>
public class CategoriaFinanceira
{
    public const int ProfundidadeMaxima = 3;

    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }

    public string Nome { get; set; } = null!;
    public TipoCategoriaFinanceira Tipo { get; set; }

    /// <summary>FK auto-referencial. Null = raiz.</summary>
    public Guid? ParentId { get; set; }
    public CategoriaFinanceira? Parent { get; set; }

    /// <summary>Profundidade calculada (1 = raiz). Usada pra reforcar limite.</summary>
    public int Profundidade { get; set; } = 1;

    public bool Ativa { get; set; } = true;
    public string? Cor { get; set; }
    public string? Icone { get; set; }
    public int Ordem { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public Empresa? Empresa { get; set; }

    public static CategoriaFinanceira Criar(
        Guid empresaId,
        string nome,
        TipoCategoriaFinanceira tipo,
        CategoriaFinanceira? parent = null,
        string? cor = null,
        string? icone = null,
        int ordem = 0)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new RegraDeDominioVioladaException("Nome da categoria nao pode ser vazio.");
        if (parent is not null && parent.EmpresaId != empresaId)
            throw new RegraDeDominioVioladaException("Categoria pai pertence a outra empresa.");
        if (parent is not null && parent.Profundidade >= ProfundidadeMaxima)
            throw new RegraDeDominioVioladaException(
                $"Profundidade maxima ({ProfundidadeMaxima}) atingida — nao e possivel criar sub-categoria.");
        if (parent is not null && parent.Tipo != tipo && parent.Tipo != TipoCategoriaFinanceira.Ambas)
            throw new RegraDeDominioVioladaException("Tipo da sub-categoria deve coincidir com o pai.");

        var agora = DateTime.UtcNow;
        return new CategoriaFinanceira
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = nome.Trim(),
            Tipo = tipo,
            ParentId = parent?.Id,
            Profundidade = (parent?.Profundidade ?? 0) + 1,
            Ativa = true,
            Cor = string.IsNullOrWhiteSpace(cor) ? null : cor.Trim(),
            Icone = string.IsNullOrWhiteSpace(icone) ? null : icone.Trim(),
            Ordem = ordem,
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    public void Renomear(string novoNome)
    {
        if (string.IsNullOrWhiteSpace(novoNome))
            throw new RegraDeDominioVioladaException("Nome da categoria nao pode ser vazio.");
        Nome = novoNome.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    public void AtualizarApresentacao(string? cor, string? icone, int? ordem)
    {
        Cor = string.IsNullOrWhiteSpace(cor) ? null : cor.Trim();
        Icone = string.IsNullOrWhiteSpace(icone) ? null : icone.Trim();
        if (ordem.HasValue) Ordem = ordem.Value;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Inativar()
    {
        if (!Ativa) return;
        Ativa = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Reativar()
    {
        if (Ativa) return;
        Ativa = true;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Move pra um novo parent. Use case deve verificar ciclos antes de chamar.
    /// </summary>
    public void MoverPara(CategoriaFinanceira? novoParent)
    {
        if (novoParent is not null)
        {
            if (novoParent.EmpresaId != EmpresaId)
                throw new RegraDeDominioVioladaException("Categoria pai pertence a outra empresa.");
            if (novoParent.Id == Id)
                throw new RegraDeDominioVioladaException("Categoria nao pode ser pai de si mesma.");
            if (novoParent.Profundidade >= ProfundidadeMaxima)
                throw new RegraDeDominioVioladaException(
                    $"Profundidade maxima ({ProfundidadeMaxima}) atingida no destino.");
            if (novoParent.Tipo != Tipo && novoParent.Tipo != TipoCategoriaFinanceira.Ambas)
                throw new RegraDeDominioVioladaException("Tipo do destino incompativel com o desta categoria.");
        }

        ParentId = novoParent?.Id;
        Parent = novoParent;
        Profundidade = (novoParent?.Profundidade ?? 0) + 1;
        AlteradoEm = DateTime.UtcNow;
    }
}
