using System;

namespace EasyStock.Application.UseCases.Common
{
    /// <summary>
    /// Lancada por use cases quando input falha validacao de negocio.
    /// Mapeada pelo middleware para HTTP 400 com `{ code?, message, details? }`.
    /// Code/Details opcionais — permite identificacao programatica no client
    /// (ex: PWA mostra empty state quando RECIPE_NOT_FOUND).
    /// </summary>
    public class UseCaseValidationException : InvalidOperationException
    {
        public string? Code { get; }
        public object? Details { get; }

        public UseCaseValidationException(string message) : base(message) { }

        public UseCaseValidationException(string code, string message, object? details = null)
            : base(message)
        {
            Code = code;
            Details = details;
        }
    }
}
