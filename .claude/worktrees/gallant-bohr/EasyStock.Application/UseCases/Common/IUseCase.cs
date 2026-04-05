namespace EasyStock.Application.UseCases.Common;

public interface IUseCase<TCommand, TResult> where TCommand : ICommand
{
    Task<TResult> ExecuteAsync(TCommand command);
}
