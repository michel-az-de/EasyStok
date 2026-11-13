using EasyStock.Application.UseCases.Common;

namespace EasyStock.Api.Mobile.Services;

internal sealed class CadastroDeOutraEmpresaException()
    : UseCaseValidationException("Cadastro indisponivel para esta empresa.")
{
}
