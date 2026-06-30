namespace EasyStock.Application.UseCases.Analytics.Reposicao;

/// <summary>
/// Comando da fonte única de reposição (ADR-0039 / issue 748): orquestra a projeção
/// por-produto (porta) + a função pura AnalisadorReposicao. EmpresaId obrigatório;
/// LojaId opcional (sem loja usa defaults globais); DiasHistorico = janela da velocidade.
/// </summary>
public sealed record ObterReposicaoCommand(Guid EmpresaId, Guid? LojaId = null, int DiasHistorico = 30);
