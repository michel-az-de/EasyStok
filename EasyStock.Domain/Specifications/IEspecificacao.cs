namespace EasyStock.Domain.Specifications;

public interface IEspecificacao<T>
{
    bool EhSatisfeitaPor(T entidade);
}
