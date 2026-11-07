import { useEffect, useState } from 'react'
import { aDevolverDe } from '../dominio/cobranca'
import { aDevolverDoDia } from '../dominio/caixa'
import { listarEstornosManuais, listarPedidosCanceladosRecentes } from '../infra/api/estornosManuaisApi'
import { somarDevolucoesPendentes } from './devolucoesPendentes'

// J2 (#1990), item 5: lido uma vez quando o modal de fechar o caixa abre (`ativo`). Falhou a
// leitura, a tela diz que não conferiu em vez de calar.
// #2275: com `diaDoCaixa` ("AAAA-MM-DD"), conta só o que entrou nesse dia (o Caixa do dia); sem ele,
// os cancelados dos últimos 90 dias (o fechamento, que não espera).
// estado: inativo | carregando | ok | erro
export function useDevolucoesPendentes(ativo, diaDoCaixa = null) {
  const [leitura, setLeitura] = useState({ estado: ativo ? 'carregando' : 'inativo', total: 0, pedidos: 0, falhas: 0 })
  useEffect(() => {
    if (!ativo) return undefined
    let vivo = true
    somarDevolucoesPendentes({
      listarCancelados: () => listarPedidosCanceladosRecentes(),
      aDevolverDoPedido: (id) => listarEstornosManuais(id).then((dados) => (diaDoCaixa ? aDevolverDoDia(dados, diaDoCaixa) : aDevolverDe(dados))),
    })
      .then((soma) => { if (vivo) setLeitura({ estado: 'ok', ...soma }) })
      .catch(() => { if (vivo) setLeitura({ estado: 'erro', total: 0, pedidos: 0, falhas: 0 }) })
    return () => { vivo = false }
  }, [ativo, diaDoCaixa])
  return leitura
}
