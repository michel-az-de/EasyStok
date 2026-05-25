using System.ComponentModel.DataAnnotations;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fiscal.InutilizarNumeracao;

/// <summary>
/// Inutiliza uma faixa de numeracao de NFC-e (motivo: numero pulado por bug,
/// lote queimado, falha sistemica). SEFAZ aceita inutilizacao APENAS no
/// mesmo ano fiscal — operacao retroativa nao e permitida.
///
/// <para>
/// <b>Justificativa:</b> SEFAZ exige minimo 15 caracteres.
/// </para>
/// </summary>
public sealed record InutilizarNumeracaoCommand(
    [property: Required] Guid EmpresaId,
    short Serie,
    long NumeroInicial,
    long NumeroFinal,
    [property: Required][property: MinLength(15)][property: MaxLength(255)] string Justificativa,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    [property: MaxLength(20)] string? Origem = "web") : ICommand;

public sealed record InutilizarNumeracaoResult(string ProtocoloEvento, DateTime DataInutilizacao);
