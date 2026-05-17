namespace EasyStock.Application.UseCases.Common
{
    public class UseCaseConcurrencyException(string message) : InvalidOperationException(message)
    {
    }
}
