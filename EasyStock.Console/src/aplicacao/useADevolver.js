import { useEffect, useState } from 'react'
import { aDevolverDe } from '../dominio/cobranca'
import { listarEstornosManuais } from '../infra/api/estornosManuaisApi'
import { consultaADevolver } from './aDevolver'

// J2 (#1990), item 4: quanto falta devolver do pedido cancelado, para o Resumo e o Pagamento da Ficha
// dizerem (antes só Pós-venda › Devoluções dizia). Relê quando o recebido muda (devolução registrada
// mexe no alocado). Falha de leitura vale zero: o bloco Devoluções segue sendo o lugar de conferir.
export function useADevolver(pedido) {
  const consultar = consultaADevolver(pedido)
  const pedidoId = pedido?.pedidoId ?? null
  const marca = `${pedido?.totalPagoApi ?? ''}|${pedido?.alocadoApi ?? ''}`
  const [lido, setLido] = useState({ chave: null, valor: 0 })
  const chave = consultar ? `${pedidoId}|${marca}` : null
  useEffect(() => {
    if (!chave) return undefined
    let vivo = true
    listarEstornosManuais(pedidoId)
      .then((dados) => { if (vivo) setLido({ chave, valor: aDevolverDe(dados) }) })
      .catch(() => { if (vivo) setLido({ chave, valor: 0 }) })
    return () => { vivo = false }
  }, [chave, pedidoId])
  return chave && lido.chave === chave ? lido.valor : 0
}
