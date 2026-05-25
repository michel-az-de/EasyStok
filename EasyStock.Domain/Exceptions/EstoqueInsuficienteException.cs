using System;

namespace EasyStock.Domain.Exceptions
{
    public class EstoqueInsuficienteException(Guid produtoId, decimal quantidadeRequisitada, decimal quantidadeDisponivel) : RegraDeDominioVioladaException($"Estoque insuficiente para o produto '{produtoId}'. Requisitado: {quantidadeRequisitada}, disponível: {quantidadeDisponivel}.")
    {
    }
}
