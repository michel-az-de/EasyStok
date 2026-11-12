import { useEffect, useId, useState } from 'react'
import { Botao } from './Botao'
import { BotaoIcone } from './BotaoIcone'
import { Icone } from './Icone'
import { Popover } from './Popover'
import css from './MenuDeAcoes.module.css'

// #2185 (plano balcao-ux, princípio 3 e seção 3.2): o menu ⋮, um só. Eram 7 implementações, com 5
// rótulos e 3 ícones: "Mais ações" escrito, ⋯, ⋮ pequeno e grande. Fecha ao escolher, no Esc e ao
// clicar fora, e devolve o foco ao gatilho (tudo do Popover); no celular vira folha no pé da tela.
//
// `rotulo` é o nome acessível e a dica: "Mais opções" + o contexto ("da conversa", "do pedido").
// `itens`: { chave, rotulo, icone, aoEscolher, tom: 'perigo', desabilitado, motivo, detalhe, abreJanela,
// marcado }. `motivo` diz por que não dá; `detalhe`, o que o item aceita ou faz (o primeiro manda).
// Condição falsa (`cond && {...}`) fica de fora, e sem item nenhum o gatilho não existe:
// menu que não leva a lugar nenhum não aparece. `marcado` (booleano) faz do item uma chave de ligar.
// `grupos`/`aoEscolherGrupo` é a lista com título e detalhe do Popover, para o menu do topo, que no
// celular abre como folha inferior de largura cheia (#2232, M4).
// `texto` põe o nome visível no gatilho, como o "Ações" da Ficha (#2232, F1); `ocupado` anuncia a espera
// (aria-busy) enquanto um item ainda trabalha depois de o menu fechar.
export function MenuDeAcoes({
  rotulo = 'Mais opções', itens, grupos, aoEscolherGrupo, icone = 'ellipsis-vertical', tom = 'texto', alinhamento = 'direita', dica, refGatilho,
  texto, ocupado = false, ...resto
}) {
  const [aberto, setAberto] = useState(false)
  const prefixo = useId()
  const lista = (itens ?? []).filter(Boolean)
  const vazio = !lista.length && !grupos?.length
  // Menu aberto que ficou sem itens fecha de verdade: sem isto, reabria sozinho quando os itens voltassem.
  useEffect(() => { if (vazio) setAberto(false) }, [vazio])
  if (vazio) return null
  const fechar = () => setAberto(false)
  const gatilho = {
    'aria-haspopup': 'menu', 'aria-expanded': aberto, 'aria-busy': ocupado || undefined, onClick: () => setAberto((v) => !v), ...resto,
  }

  return (
    <span className={css.menu}>
      {texto
        ? <Botao ref={refGatilho} variante="secundario" icone={icone} aria-label={rotulo} title={dica} {...gatilho}>{texto}</Botao>
        : <BotaoIcone ref={refGatilho} rotulo={rotulo} dica={dica} icone={icone} tom={tom} {...gatilho} />}
      {aberto && (
        <Popover
          rotulo={rotulo} posicao="abaixo" alinhamento={alinhamento} largura={grupos ? undefined : LARGURA} portal aoFechar={fechar}
          folha={Boolean(grupos)} grupos={grupos} aoEscolher={grupos ? (item) => { fechar(); aoEscolherGrupo?.(item) } : undefined}
        >
          {grupos ? undefined : (
            <div className={css.lista}>
              {lista.map((item) => {
                const explicacao = item.motivo ?? item.detalhe
                const idMotivo = explicacao ? `${prefixo}-${item.chave}` : undefined
                const chave = typeof item.marcado === 'boolean'
                return (
                  <div key={item.chave}>
                    <button
                      type="button" role={chave ? 'menuitemcheckbox' : 'menuitem'} tabIndex={-1}
                      className={[css.item, item.tom === 'perigo' ? css.perigo : ''].filter(Boolean).join(' ')}
                      disabled={item.desabilitado} aria-checked={chave ? item.marcado : undefined}
                      aria-haspopup={item.abreJanela ? 'dialog' : undefined} aria-describedby={idMotivo}
                      onClick={() => { fechar(); item.aoEscolher() }}
                    >
                      {item.icone && <Icone nome={item.icone} tamanho={20} />}
                      <span className={css.rotulo}>{item.rotulo}</span>
                      {item.marcado && <Icone nome="check" tamanho={20} />}
                    </button>
                    {explicacao && <p id={idMotivo} className={css.motivo}>{explicacao}</p>}
                  </div>
                )
              })}
            </div>
          )}
        </Popover>
      )}
    </span>
  )
}

const LARGURA = 280
