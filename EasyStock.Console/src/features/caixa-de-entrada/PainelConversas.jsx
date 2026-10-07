import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { CartaoConversa } from './CartaoConversa'
import { BarraBalcao } from './BarraBalcao'
import { NaoEntregues } from './NaoEntregues'
import css from './caixa.module.css'

// Grupo recolhido do fim de "Todas" (seção 1): Encerradas e Bloqueados, cada
// um com sua própria lista e sua própria memória de aberto/fechado — abrir um
// não deve fechar o outro.
function GrupoRecolhido({
  titulo, conversas, aberto, aoAlternar, agora, automaticoPausado, aoAbrir, chave, busca,
}) {
  if (conversas.length === 0) return null
  return (
    <div className={css.grupo}>
      <button
        type="button"
        className={css.tituloGrupo}
        aria-expanded={aberto}
        onClick={aoAlternar}
      >
        <Icone nome="chevron-right" tamanho={20} className={aberto ? css.chevronAberto : ''} />
        {titulo} · {conversas.length}
      </button>
      {aberto && conversas.map((conversa) => (
        <CartaoConversa
          key={conversa.id}
          conversa={conversa}
          selecionada={false}
          agora={agora}
          automaticoPausado={automaticoPausado}
          grupo={chave}
          busca={busca}
          aoAbrir={aoAbrir}
        />
      ))}
    </div>
  )
}

export function PainelConversas({ aoAbrir }) {
  const {
    visiveis, encerradasDoBalcao, bloqueadasDoBalcao, selecionadaId, filtros, agora,
    automaticoPausado, aberta, horarioDaLoja,
  } = useAtendimento()
  const { mudarFiltro, selecionar } = useAcoes()
  const [gruposAbertos, setGruposAbertos] = useState({ encerradas: false, bloqueados: false })
  // Com busca, os dois grupos abrem sozinhos: achar a cliente encerrada ou
  // bloqueada e ela ficar escondida atrás de um recolhido parecia "não achou".
  // Ajuste de estado na troca de busca, no render (padrão do React para
  // estado que depende de prop), sem efeito.
  const [buscaVista, setBuscaVista] = useState(filtros.busca)
  if (buscaVista !== filtros.busca) {
    setBuscaVista(filtros.busca)
    const abrir = Boolean(filtros.busca.trim())
    setGruposAbertos({ encerradas: abrir, bloqueados: abrir })
  }

  function abrir(id) {
    selecionar(id)
    aoAbrir?.(id)
  }

  function alternarGrupo(chave) {
    setGruposAbertos((atual) => ({ ...atual, [chave]: !atual[chave] }))
  }

  // A lista não pula debaixo do dedo (seção 1): enquanto o ponteiro está sobre
  // ela ou o foco está dentro dela, a reordenação espera; aplica 300 ms depois
  // que ele sai. Só a ORDEM congela — o conteúdo de cada cartão continua vindo
  // de `visiveis`, então mensagem nova ainda aparece na hora.
  // `ordemCongelada` mora em estado, não em ref: o valor entra na conta de
  // `lista` durante o render, e ref lido no render é o tipo de coisa que o
  // lint de refs (react/refs) reprova com razão — o componente não acorda
  // sozinho quando só o ref muda.
  const [ordemCongelada, setOrdemCongelada] = useState(null)
  const destravarRef = useRef(null)

  useEffect(() => () => clearTimeout(destravarRef.current), [])

  function travar() {
    clearTimeout(destravarRef.current)
    setOrdemCongelada((atual) => atual ?? visiveis.map((c) => c.id))
  }
  function destravarComAtraso() {
    clearTimeout(destravarRef.current)
    destravarRef.current = setTimeout(() => setOrdemCongelada(null), 300)
  }

  const lista = useMemo(() => {
    if (!ordemCongelada) return visiveis
    const porId = new Map(visiveis.map((c) => [c.id, c]))
    const congeladas = new Set(ordemCongelada)
    const ordenada = ordemCongelada.map((id) => porId.get(id)).filter(Boolean)
    const novas = visiveis.filter((c) => !congeladas.has(c.id))
    return [...ordenada, ...novas]
  }, [visiveis, ordemCongelada])

  const semCanal = filtros.canais.length === 0
  const semBusca = !filtros.busca.trim()

  return (
    <>
      <BarraBalcao filtros={filtros} aoMudar={mudarFiltro} />
      <NaoEntregues aoAbrir={aoAbrir} />
      <div
        className={css.rolagem}
        onMouseEnter={travar}
        onMouseLeave={destravarComAtraso}
        onFocusCapture={travar}
        onBlurCapture={destravarComAtraso}
      >
        {lista.length === 0 && !semBusca
          && (filtros.aba === 'precisa' || encerradasDoBalcao.length + bloqueadasDoBalcao.length === 0) && (
          <Vazio
            titulo={`Nada com "${filtros.busca.trim()}"`}
            acao={<Botao variante="secundario" onClick={() => mudarFiltro('busca', '')}>Limpar busca</Botao>}
          >
            Nenhuma conversa tem esse nome, telefone, mensagem ou tag.
          </Vazio>
        )}
        {lista.length === 0 && semBusca && !semCanal && (
          <Vazio
            titulo={`Nada no ${filtros.canais.join(', ')}`}
            acao={<Botao variante="texto" onClick={() => mudarFiltro('canais', [])}>Ver todos os canais</Botao>}
          >
            Nenhuma conversa deste canal está aqui agora.
          </Vazio>
        )}
        {lista.length === 0 && semBusca && semCanal && filtros.aba === 'precisa' && (
          <Vazio
            titulo="Ninguém esperando você"
            acao={<Botao variante="texto" onClick={() => mudarFiltro('aba', 'todas')}>Ver todas</Botao>}
          >
            Toda conversa em aberto está com o automático ou já foi respondida.
          </Vazio>
        )}
        {lista.length === 0 && semBusca && semCanal && filtros.aba !== 'precisa' && (
          <Vazio titulo="Nada por aqui">Nenhuma conversa neste canal e nesta busca.</Vazio>
        )}
        {lista.map((conversa) => (
          <CartaoConversa
            key={conversa.id}
            conversa={conversa}
            selecionada={conversa.id === selecionadaId}
            agora={agora}
            automaticoPausado={automaticoPausado}
            aberta={aberta}
            expediente={horarioDaLoja}
            busca={filtros.busca}
            aoAbrir={abrir}
          />
        ))}
        {filtros.aba === 'todas' && (
          <>
            <GrupoRecolhido
              chave="encerradas"
              titulo="Encerradas"
              conversas={encerradasDoBalcao}
              aberto={gruposAbertos.encerradas}
              aoAlternar={() => alternarGrupo('encerradas')}
              agora={agora}
              automaticoPausado={automaticoPausado}
              busca={filtros.busca}
              aoAbrir={abrir}
            />
            <GrupoRecolhido
              chave="bloqueados"
              titulo="Bloqueados"
              conversas={bloqueadasDoBalcao}
              aberto={gruposAbertos.bloqueados}
              aoAlternar={() => alternarGrupo('bloqueados')}
              agora={agora}
              automaticoPausado={automaticoPausado}
              busca={filtros.busca}
              aoAbrir={abrir}
            />
          </>
        )}
      </div>
    </>
  )
}
