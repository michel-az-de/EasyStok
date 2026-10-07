import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Popover } from '../../componentes/Popover'
import { useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { precisaDeVoce } from '../../dominio/automatico'
import { combinaBusca, combinaCanal, contarPorCanal } from '../../dominio/conversa'
import css from './caixa.module.css'

// "Site" é o rótulo curto de "Chat do site" só no contador (seção 1): no cartão e
// na ficha o nome continua inteiro.
const ROTULO_CURTO_DO_CANAL = { 'Chat do site': 'Site' }

const OPCOES_DE_ORDEM = [
  { chave: 'urgencia', titulo: 'Urgência', detalhe: 'Precisa de você primeiro; dentro, quem espera há mais tempo' },
  { chave: 'recentes', titulo: 'Mais recentes', detalhe: 'Última mensagem mais nova primeiro' },
  { chave: 'entrega', titulo: 'Entrega mais cedo', detalhe: 'Pedido com janela mais próxima primeiro; sem pedido vai para o fim' },
]

// Guarda o último valor visto e devolve quantas vezes ele SUBIU: serve de
// `key` do número, e trocar a key remonta o span e refaz o pulso. Descer não
// pulsa (conversa encerrada não é novidade). Ajuste de estado no render, mesmo
// padrão de `buscaVista` no PainelConversas.
function useSubidas(valor) {
  const [visto, setVisto] = useState(valor)
  const [subidas, setSubidas] = useState(0)
  if (valor !== visto) {
    setVisto(valor)
    if (valor > visto) setSubidas((n) => n + 1)
  }
  return subidas
}

// Contador de um canal (issue #2): número grande das abertas no Balcão e,
// embaixo, quantas delas precisam de você. É o próprio filtro de canal da
// linha C: tocar liga, tocar de novo desliga.
function ContadorCanal({ canal, rotulo, abertas, precisam, ligado, aoTrocar }) {
  const subidasAbertas = useSubidas(abertas)
  const subidasPrecisam = useSubidas(precisam)
  const pulso = (subidas) => (subidas > 0 ? css.pulso : '')
  return (
    <button
      type="button"
      className={`${css.contadorCanal} ${ligado ? css.contadorLigado : ''}`}
      aria-pressed={ligado}
      aria-label={`${rotulo}: ${abertas} ${abertas === 1 ? 'aberta' : 'abertas'}, ${precisam} ${precisam === 1 ? 'precisa' : 'precisam'} de você`}
      onClick={aoTrocar}
    >
      <span className={css.topoContador}>
        <Icone nome={canal.icone} tamanho={20} className={css['canal_' + canal.icone]} />
        <span className={css.rotuloContador}>{rotulo}</span>
      </span>
      <span className={css.numerosContador}>
        <span key={subidasAbertas} className={`${css.numeroContador} ${pulso(subidasAbertas)}`}>{abertas}</span>
        <span
          key={`p${subidasPrecisam}`}
          className={`${css.precisamContador} ${precisam > 0 ? css.precisamAtivo : ''} ${pulso(subidasPrecisam)}`}
        >
          {precisam} {precisam === 1 ? 'precisa' : 'precisam'}
        </span>
      </span>
    </button>
  )
}

// Linha A (busca e ordenar), linha B (abas) e linha C (contadores de canal) da
// seção 1: substitui os dez controles empilhados do Balcão antigo (achado #1
// da onda seguinte, 31-gp-onda-seguinte.md).
export function BarraBalcao({ filtros, aoMudar }) {
  const [ordenarAberto, setOrdenarAberto] = useState(false)
  const { canais, janelas } = useCatalogo()
  const { conversas, agora, automaticoPausado, aberta, horarioDaLoja } = useAtendimento()

  // As duas contagens das abas já respeitam o canal ligado na linha C e a
  // busca da linha A (seção 1): trocar canal ou digitar não pode deixar o
  // número da aba discordando da lista embaixo.
  const doUniverso = conversas.filter((c) => combinaCanal(c, filtros.canais) && combinaBusca(c, filtros.busca))
  const quantasPrecisam = doUniverso
    .filter((c) => precisaDeVoce(c, agora, automaticoPausado[c.id] ?? false, aberta, janelas, horarioDaLoja)).length

  // Tempo real: derivado de `conversas` a cada render, sem contador guardado
  // que possa divergir. Conversa nova da simulação (F2) já sobe o número.
  const contagemPorCanal = contarPorCanal(conversas, canais, agora, automaticoPausado, aberta, janelas, horarioDaLoja)

  function trocarCanal(nome) {
    const ligados = filtros.canais.includes(nome)
      ? filtros.canais.filter((c) => c !== nome)
      : [...filtros.canais, nome]
    aoMudar('canais', ligados)
  }

  function escolherOrdem(opcao) {
    setOrdenarAberto(false)
    aoMudar('ordenacao', opcao.chave)
  }

  return (
    <div className={css.barra}>
      <div className={css.linhaA}>
        <span className={css.campoBusca}>
          <Icone nome="search" tamanho={20} />
          <label className="sr" htmlFor="balcao-busca">Buscar nome, telefone, mensagem ou tag</label>
          <input
            id="balcao-busca"
            type="search"
            className={css.entradaBusca}
            placeholder="Buscar, ou uma tag"
            value={filtros.busca}
            onChange={(e) => {
              // Integração: buscar um nome estando em "Precisa de você" dava
              // "Nada com ..." para cliente que existe, só porque ele não
              // espera resposta. Quem busca quer achar: a busca leva para Todas.
              if (e.target.value.trim() && filtros.aba === 'precisa') aoMudar('aba', 'todas')
              aoMudar('busca', e.target.value)
            }}
          />
          {filtros.busca && (
            <button
              type="button"
              className={css.limparBusca}
              aria-label="Limpar busca"
              onClick={() => aoMudar('busca', '')}
            >
              <Icone nome="x" tamanho={20} />
            </button>
          )}
        </span>

        <span className={css.comOrdenar}>
          <Botao
            variante="secundario"
            className={css.botaoOrdenar}
            aria-haspopup="menu"
            aria-expanded={ordenarAberto}
            aria-label="Ordenar"
            title="Ordenar"
            onClick={() => setOrdenarAberto((v) => !v)}
          >
            <Icone nome="arrow-up-down" tamanho={20} />
            {filtros.ordenacao !== 'urgencia' && <span className={css.pontoOrdem} aria-hidden="true" />}
          </Botao>
          {ordenarAberto && (
            <Popover rotulo="Ordenar" posicao="abaixo" aoFechar={() => setOrdenarAberto(false)}>
              <div className={css.menuOrdem} role="menu" aria-label="Ordenar">
                {OPCOES_DE_ORDEM.map((opcao) => (
                  <button
                    key={opcao.chave}
                    type="button"
                    role="menuitemradio"
                    aria-checked={filtros.ordenacao === opcao.chave}
                    className={css.itemOrdem}
                    onClick={() => escolherOrdem(opcao)}
                  >
                    <Icone nome="check" tamanho={20} className={filtros.ordenacao === opcao.chave ? '' : css.semMarca} />
                    <span>
                      <strong>{opcao.titulo}</strong>
                      <span className={css.detalheOrdem}>{opcao.detalhe}</span>
                    </span>
                  </button>
                ))}
              </div>
            </Popover>
          )}
        </span>
      </div>

      <div className={css.abas} role="tablist" aria-label="Conversas do Balcão">
        <button
          type="button"
          role="tab"
          aria-selected={filtros.aba === 'precisa'}
          className={`${css.aba} ${filtros.aba === 'precisa' ? css.abaAtiva : ''}`}
          onClick={() => aoMudar('aba', 'precisa')}
        >
          Precisa de você <span className={css.contadorAba}>{quantasPrecisam}</span>
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={filtros.aba === 'todas'}
          className={`${css.aba} ${filtros.aba === 'todas' ? css.abaAtiva : ''}`}
          onClick={() => aoMudar('aba', 'todas')}
        >
          Todas <span className={css.contadorAba}>{doUniverso.length}</span>
        </button>
      </div>

      <fieldset className={css.contadoresCanal}>
        <legend className="sr">Canal</legend>
        {canais.map((c, i) => (
          <ContadorCanal
            key={c.nome}
            canal={c}
            rotulo={ROTULO_CURTO_DO_CANAL[c.nome] ?? c.nome}
            abertas={contagemPorCanal[i].abertas}
            precisam={contagemPorCanal[i].precisam}
            ligado={filtros.canais.includes(c.nome)}
            aoTrocar={() => trocarCanal(c.nome)}
          />
        ))}
      </fieldset>
    </div>
  )
}
