import { useEffect, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { useAcoes } from '../../aplicacao/contextos'
import { useNovoPedidoDoCliente } from '../../aplicacao/novoPedidoDoCliente'
import { clientesParaChamar } from '../../dominio/cliente'
import { telefoneDoE164 } from '../../dominio/formato'
import { AnfitriaoDeConfirmacao } from './AnfitriaoDeConfirmacao'
import { ModalEnviarModelo } from './ModalEnviarModelo'
import css from './atendimento.module.css'

const MINIMO_DE_LETRAS = 3
const ESPERA_DA_DIGITACAO_MS = 300

// #1408: a busca do Balcão também acha o cliente cadastrado que ainda não tem conversa aberta. "Chamar no WhatsApp"
// escolhe o modelo aprovado (fora da janela de 24 h só sai modelo) e, enviado, abre a conversa no Balcão.
// Só no modo API: a demonstração não tem a ação `buscarClientesParaChamar`. A Moldura entrega este componente à
// lista de conversas (feature não importa feature).
// #2871: "Novo pedido" registra o pedido de quem pediu por outro canal (WhatsApp pessoal da dona): abre a conversa da
// loja sem mandar mensagem e a comanda já vem aberta; o resumo do pedido é o primeiro contato.
export function ChamarClientes({ busca, conversas, aoAbrir }) {
  const { buscarClientesParaChamar, selecionar, selecionarIniciada } = useAcoes()
  const novoPedido = useNovoPedidoDoCliente({ aoAbrir })
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
  if (lista.length === 0 && !escolhido && !novoPedido.erro) return null

  return (
    <section className={css.chamarClientes} aria-label="Clientes cadastrados sem conversa aberta">
      <AnfitriaoDeConfirmacao />
      {lista.length > 0 && <p className={css.chamarClientesTitulo}>Clientes cadastrados sem conversa aberta</p>}
      {novoPedido.erro && <p ref={novoPedido.refDoErro} tabIndex={-1} className={css.chamarClienteErro} role="alert">{novoPedido.erro}</p>}
      {lista.map((cliente) => (
        <div key={cliente.id} className={css.chamarCliente}>
          <span className={css.chamarClienteNome}>{cliente.nome} · {telefoneDoE164(cliente.telefone)}</span>
          <span className={css.chamarClienteAcoes}>
            {novoPedido.disponivel && (
              <Botao icone="mais" disabled={Boolean(novoPedido.abrindo)} aria-label={`Novo pedido de ${cliente.nome}`}
                onClick={() => novoPedido.iniciar(cliente.id)}>{novoPedido.abrindo === cliente.id ? 'Abrindo…' : novoPedido.rotulo(cliente.id)}</Botao>
            )}
            <Botao disabled={Boolean(novoPedido.abrindo)} onClick={() => setEscolhido(cliente)}>Chamar no WhatsApp</Botao>
          </span>
        </div>
      ))}
      {escolhido && (
        <ModalEnviarModelo
          cliente={escolhido}
          aoFechar={() => setEscolhido(null)}
          aoIniciar={(conversaId) => { (selecionarIniciada ?? selecionar)(conversaId); aoAbrir?.(conversaId) }}
        />
      )}
    </section>
  )
}
