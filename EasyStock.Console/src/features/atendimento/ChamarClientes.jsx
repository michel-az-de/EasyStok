import { useEffect, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { useAcoes } from '../../aplicacao/contextos'
import { clientesParaChamar } from '../../dominio/cliente'
import { telefoneDoE164 } from '../../dominio/formato'
import { ModalEnviarModelo } from './ModalEnviarModelo'
import css from './atendimento.module.css'

const MINIMO_DE_LETRAS = 3
const ESPERA_DA_DIGITACAO_MS = 300

// #1408: a busca do Balcão também acha o cliente cadastrado que ainda não tem conversa aberta. "Chamar no WhatsApp"
// escolhe o modelo aprovado (fora da janela de 24 h só sai modelo) e, enviado, abre a conversa no Balcão.
// Só no modo API: a demonstração não tem a ação `buscarClientesParaChamar`. A Moldura entrega este componente à
// lista de conversas (feature não importa feature).
export function ChamarClientes({ busca, conversas, aoAbrir }) {
  const { buscarClientesParaChamar, selecionar } = useAcoes()
  const [achados, setAchados] = useState({ termo: '', lista: [] })
  const [escolhido, setEscolhido] = useState(null)
  const termo = busca.trim()
  const ativa = Boolean(buscarClientesParaChamar) && termo.length >= MINIMO_DE_LETRAS

  useEffect(() => {
    if (!ativa) return undefined
    let vivo = true
    const espera = setTimeout(() => {
      buscarClientesParaChamar(termo).then(
        (lista) => { if (vivo) setAchados({ termo, lista }) },
        () => { if (vivo) setAchados({ termo, lista: [] }) },
      )
    }, ESPERA_DA_DIGITACAO_MS)
    return () => { vivo = false; clearTimeout(espera) }
  }, [ativa, termo, buscarClientesParaChamar])

  // Resultado de outra busca (digitação mais rápida que a resposta) não aparece.
  const lista = ativa && achados.termo === termo ? clientesParaChamar(achados.lista, conversas) : []
  if (lista.length === 0 && !escolhido) return null

  return (
    <section className={css.chamarClientes} aria-label="Clientes cadastrados sem conversa aberta">
      {lista.length > 0 && <p className={css.chamarClientesTitulo}>Clientes cadastrados sem conversa aberta</p>}
      {lista.map((cliente) => (
        <div key={cliente.id} className={css.chamarCliente}>
          <span className={css.chamarClienteNome}>{cliente.nome} · {telefoneDoE164(cliente.telefone)}</span>
          <Botao onClick={() => setEscolhido(cliente)}>Chamar no WhatsApp</Botao>
        </div>
      ))}
      {escolhido && (
        <ModalEnviarModelo
          cliente={escolhido}
          aoFechar={() => setEscolhido(null)}
          aoIniciar={(conversaId) => { selecionar(conversaId); aoAbrir?.(conversaId) }}
        />
      )}
    </section>
  )
}
