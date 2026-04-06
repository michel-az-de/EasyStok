using System;

namespace EasyStock.Domain.Exceptions
{
    public class EstoqueInsuficienteException(Guid produtoId, int quantidadeRequisitada, int quantidadeDisponivel) : RegraDeDominioVioladaException($"Estoque insuficiente para o produto '{produtoId}'. Requisitado: {quantidadeRequisitada}, disponível: {quantidadeDisponivel}.")
    {
    }
}
