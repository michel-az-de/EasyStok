// Configurações › Histórico de acesso (#1753, só no modo API): quem entrou, quem foi convidado,
// quem mudou de perfil e quem foi desativado, com o nome de quem fez. Só a dona lê.
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { listarAuditoria, listarUsuarios } from '../../../aplicacao/usuarios'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao } from '../../../componentes/Campo'
import { Vazio } from '../../../componentes/Vazio'
import { FUSO } from '../../../dominio/formato'
import {
  agruparPorDia, condensarAuditoria, instanteDaApi, juntarPaginas, linhaDeAuditoria, opcoesDePessoas,
} from '../../../dominio/usuarios'
import css from './usuarios.module.css'

const TAMANHO = 50
const INICIO = { carregando: true, linhas: [], total: 0, pagina: 0, erro: null }
const dia = (texto) => {
  const ms = instanteDaApi(texto)
  if (ms === null) return 'Sem data'
  const escrito = new Date(ms).toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long', timeZone: FUSO })
  return escrito.charAt(0).toLocaleUpperCase('pt-BR') + escrito.slice(1)
}
const hora = (texto) => {
  const ms = instanteDaApi(texto)
  return ms === null ? '' : new Date(ms).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: FUSO })
}

export function AbaAuditoria() {
  const [pessoas, setPessoas] = useState([])
  const [usuarioId, setUsuarioId] = useState('')
  const [estado, setEstado] = useState(INICIO)
  // Toda busca nova cancela a anterior, e resposta de busca antiga é descartada: sem isso a página de uma pessoa
  // chegava atrasada e entrava na lista de outra.
  const geracao = useRef(0)
  const controle = useRef(null)

  useEffect(() => {
    const controle = new AbortController()
    listarUsuarios(controle.signal).then((lista) => setPessoas(lista ?? [])).catch(() => {})
    return () => controle.abort()
  }, [])

  const cancelar = useCallback(() => {
    geracao.current += 1
    controle.current?.abort()
    controle.current = null
  }, [])

  const buscar = useCallback(async (pagina) => {
    cancelar()
    const minha = geracao.current
    const meuControle = new AbortController()
    controle.current = meuControle
    setEstado((atual) => ({ ...atual, carregando: true, erro: null }))
    try {
      const resposta = await listarAuditoria({ usuarioId: usuarioId || null, pagina, tamanho: TAMANHO }, meuControle.signal)
      if (minha !== geracao.current) return
      const novas = (resposta?.data ?? []).map(linhaDeAuditoria)
      setEstado((atual) => ({
        carregando: false, linhas: pagina === 1 ? novas : juntarPaginas(atual.linhas, novas),
        total: resposta?.meta?.total ?? novas.length, pagina, erro: null,
      }))
    } catch (falha) {
      if (minha !== geracao.current) return
      setEstado((atual) => ({ ...atual, carregando: false, erro: falha.message }))
    }
  }, [usuarioId, cancelar])

  useEffect(() => {
    buscar(1)
    return cancelar
  }, [buscar, cancelar])

  // Trocar a pessoa zera a lista antes da busca: linha de uma pessoa nunca fica sob o filtro de outra.
  function trocarPessoa(evento) {
    cancelar()
    setEstado(INICIO)
    setUsuarioId(evento.target.value)
  }

  const grupos = useMemo(
    () => agruparPorDia(condensarAuditoria(estado.linhas, (a, b) => dia(a) === dia(b)), dia),
    [estado.linhas],
  )
  const opcoes = useMemo(() => [{ valor: '', rotulo: 'Todas as pessoas' }, ...opcoesDePessoas(pessoas)], [pessoas])
  // A contagem de linhas pode não bater com o total (linha repetida é descartada); a página diz se ainda há o que buscar.
  const haMais = estado.pagina * TAMANHO < estado.total

  return (
    <div className={css.aba}>
      <div className={css.filtro}>
        <CampoSelecao rotulo="Pessoa" opcoes={opcoes} value={usuarioId} onChange={trocarPessoa} />
      </div>
      {estado.erro && (
        <p className={css.erro} role="alert">
          O histórico não carregou: {estado.erro}{' '}
          <Botao variante="texto" onClick={() => buscar(estado.pagina + 1)}>Tentar de novo</Botao>
        </p>
      )}
      {!estado.erro && !estado.carregando && estado.linhas.length === 0 && (
        <Vazio titulo="Nada registrado ainda">Entradas, convites e mudanças de acesso aparecem aqui.</Vazio>
      )}
      {grupos.map((grupo) => (
        <section key={grupo.dia} className={css.dia} aria-label={grupo.dia}>
          <h3 className={css.tituloDia}>{grupo.dia}</h3>
          <ul className={css.historico}>
            {grupo.linhas.map((linha) => (
              <li key={linha.id} className={css.linha} data-rotina={linha.rotina || undefined}>
                <time className={css.hora} dateTime={linha.quando}>{hora(linha.quando)}</time>
                <span className={css.fato}>
                  {/* Com a pessoa escolhida no filtro, o nome dela em toda linha só repete. */}
                  {!usuarioId && <><strong>{linha.pessoa}</strong> · </>}{linha.oQue}{linha.falhou && ' (não concluído)'}
                  {linha.vezes > 1 && hora(linha.primeira) !== hora(linha.quando) && <span className={css.detalhe}>A primeira às {hora(linha.primeira)}</span>}
                </span>
              </li>
            ))}
          </ul>
        </section>
      ))}
      {estado.carregando && <output className={css.descricao}>Carregando o histórico…</output>}
      {haMais && !estado.carregando && (
        <Botao className={css.mais} onClick={() => buscar(estado.pagina + 1)}>Carregar mais antigos</Botao>
      )}
    </div>
  )
}
