import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { primeiroNome } from '../../dominio/mensagem'
import { atendentesParaPassar } from './apresentacaoDaConversa'
import css from './atendimento.module.css'

// #1239 (S41): "Passar para…" no cabeçalho da conversa. A API já transferia
// (`POST conversas/{id}/transferir`) e a lista de atendentes só alimentava o filtro do Balcão.
// A conversa vai para quem atende nesta empresa; a nota interna de quem passou para quem é do
// EasyStok. Só no modo API (`transferirConversa`): a demonstração tem uma atendente só.
export function PassarConversa({ conversa }) {
  const { listarAtendentes, transferirConversa } = useAcoes()
  const { sessao } = useAtendimento()
  const [aberto, setAberto] = useState(false)
  const [consulta, setConsulta] = useState({ estado: 'carregando', itens: [] })
  const [tentativa, setTentativa] = useState(0)
  const [enviando, setEnviando] = useState(null)
  const [erro, setErro] = useState(null)
  const emVoo = useRef(false)

  useEffect(() => {
    if (!aberto || !listarAtendentes) return undefined
    let vivo = true
    listarAtendentes().then(
      (itens) => { if (vivo) setConsulta({ estado: 'ok', itens: itens ?? [] }) },
      () => { if (vivo) setConsulta({ estado: 'erro', itens: [] }) },
    )
    return () => { vivo = false }
  }, [aberto, listarAtendentes, tentativa])

  if (!transferirConversa) return null

  const opcoes = atendentesParaPassar(consulta.itens, {
    usuarioId: sessao?.usuario?.id ?? null, responsavelId: conversa.assumidaPorUsuarioId ?? null,
  })

  function abrir() {
    setErro(null)
    setConsulta({ estado: 'carregando', itens: [] })
    setAberto(true)
  }

  async function passar(atendente) {
    if (emVoo.current) return
    emVoo.current = true
    setEnviando(atendente.usuarioId)
    setErro(null)
    try {
      const resultado = await transferirConversa(conversa.id, atendente.usuarioId)
      if (resultado?.erro) setErro(`Não foi possível passar a conversa: ${resultado.erro}`)
      else setAberto(false)
    } finally {
      emVoo.current = false
      setEnviando(null)
    }
  }

  return (
    <>
      <Botao variante="secundario" icone="hand" className={css.passar} aria-haspopup="dialog" title="Passar esta conversa para outra atendente" onClick={abrir}>
        Passar para…
      </Botao>
      {aberto && (
        <Modal
          titulo={`Passar a conversa de ${primeiroNome(conversa.nome)}`}
          descricao="Quem você escolher passa a responder esta conversa. Fica uma nota interna de quem passou para quem."
          aoFechar={enviando ? undefined : () => setAberto(false)}
          rodape={<Botao disabled={Boolean(enviando)} onClick={() => setAberto(false)}>Voltar</Botao>}
        >
          {consulta.estado === 'carregando' && <p>Consultando quem atende…</p>}
          {consulta.estado === 'erro' && (
            <p role="alert">
              Não foi possível consultar os atendentes.{' '}
              <Botao variante="texto" onClick={() => { setConsulta({ estado: 'carregando', itens: [] }); setTentativa((n) => n + 1) }}>Tentar novamente</Botao>
            </p>
          )}
          {consulta.estado === 'ok' && opcoes.length === 0 && <p>Nenhuma outra pessoa atende conversas nesta empresa agora.</p>}
          {consulta.estado === 'ok' && opcoes.length > 0 && (
            <ul className={css.listaPassar} aria-label="Atendentes">
              {opcoes.map((a) => (
                <li key={a.usuarioId}>
                  <Botao largo disabled={Boolean(enviando)} onClick={() => passar(a)}>
                    {enviando === a.usuarioId ? 'Passando…' : a.nome}
                  </Botao>
                </li>
              ))}
            </ul>
          )}
          {erro && <p role="alert">{erro}</p>}
        </Modal>
      )}
    </>
  )
}
