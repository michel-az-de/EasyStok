import { useState } from 'react'
import { adicionarTagCliente, removerTagCliente } from '../infra/api/tagsClienteApi'
import { TAG_RECOMPRA_CONVIDADA } from '../dominio/recompra'

// #2946: abrir o convite grava a tag que tira o cliente da lista por 30 dias. O sistema não sabe se a mensagem
// saiu, então o aviso diz "convite aberto", e o "Desfazer" devolve a pessoa à lista. 409 é a tag de um convite
// anterior: a data dela é que conta o prazo, então a tag é trocada por uma nova em vez de aceita como está.
export function useConviteDeRecompra({ aoMudar }) {
  const [convite, definir] = useState(null)
  const [erro, falhar] = useState(null)
  async function convidar(clienteId, nome) {
    falhar(null)
    try {
      try { await adicionarTagCliente(clienteId, TAG_RECOMPRA_CONVIDADA) } catch (e) {
        if (e?.status !== 409) throw e
        await removerTagCliente(clienteId, TAG_RECOMPRA_CONVIDADA)
        await adicionarTagCliente(clienteId, TAG_RECOMPRA_CONVIDADA)
      }
    } catch { falhar(`Não consegui registrar o convite de ${nome}. Ele continua na lista.`); return }
    definir({ clienteId, nome }); aoMudar()
  }
  async function desfazer() {
    if (!convite) return
    falhar(null)
    try { await removerTagCliente(convite.clienteId, TAG_RECOMPRA_CONVIDADA) } catch (e) {
      if (e?.status !== 404) { falhar(`Não consegui desfazer o convite de ${convite.nome}. Tente de novo.`); return }
    }
    definir(null); aoMudar()
  }
  return { convite, erro, convidar, desfazer, limpar: () => { definir(null); falhar(null) } }
}
