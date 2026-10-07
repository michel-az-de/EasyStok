import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Popover } from '../../componentes/Popover'
import { useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { precisaDeVoce } from '../../dominio/automatico'
import { combinaBusca, combinaCanal, contarPorCanal } from '../../dominio/conversa'
import css from './caixa.module.css'

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

// Filtro de canal (issue #2) em chip compacto (#1442): ícone e número das
// abertas, o nome e quantas precisam de você no rótulo acessível e no title.
// Tocar liga, tocar de novo desliga. O ponto âmbar avisa que alguém daquele
// canal espera ação dela, sem o cartão grande de antes.
function ChipCanal({ canal, rotulo, abertas, precisam, ligado, aoTrocar }) {
  const subidas = useSubidas(abertas)
  const descricao = `${rotulo}: ${abertas} ${abertas === 1 ? 'aberta' : 'abertas'}, ${precisam} ${precisam === 1 ? 'precisa' : 'precisam'} de você`
  return (
    <button
      type="button"
      className={`${css.chipFiltro} ${css.chipCanal} ${ligado ? css.chipLigado : ''}`}
      aria-pressed={ligado}
      aria-label={descricao}
      title={descricao}
      onClick={aoTrocar}
    >
      <Icone nome={canal.icone} tamanho={16} className={css['canal_' + canal.icone]} />
      <span key={subidas} className={`${css.numeroChip} ${subidas > 0 ? css.pulso : ''}`}>{abertas}</span>
      {precisam > 0 && <span className={css.pontoPrecisa} aria-hidden="true" />}
    </button>
  )
}

// Linha A (busca e ordenar) e linha B (filtros) da seção 1. #1442
// (homologação de 07/10): o Balcão abre em "Todas"; "Precisa de você" virou
// filtro com contador e os três cartões de canal viraram chips na mesma linha.
export function BarraBalcao({ filtros, aoMudar }) {
  const [ordenarAberto, setOrdenarAberto] = useState(false)
  const { canais, janelas } = useCatalogo()
  const { conversas, agora, automaticoPausado, aberta } = useAtendimento()

  // As duas contagens das abas já respeitam o canal ligado na linha C e a
  // busca da linha A (seção 1): trocar canal ou digitar não pode deixar o
  // número da aba discordando da lista embaixo.
  const doUniverso = conversas.filter((c) => combinaCanal(c, filtros.canais) && combinaBusca(c, filtros.busca))
  const quantasPrecisam = doUniverso
    .filter((c) => precisaDeVoce(c, agora, automaticoPausado[c.id] ?? false, aberta, janelas)).length

  // Tempo real: derivado de `conversas` a cada render, sem contador guardado
  // que possa divergir. Conversa nova da simulação (F2) já sobe o número.
  const contagemPorCanal = contarPorCanal(conversas, canais, agora, automaticoPausado, aberta, janelas)

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

      <fieldset className={css.filtros}>
        <legend className="sr">Filtros do Balcão</legend>
        <button
          type="button"
          aria-pressed={filtros.aba !== 'precisa'}
          className={`${css.chipFiltro} ${filtros.aba !== 'precisa' ? css.chipLigado : ''}`}
          onClick={() => aoMudar('aba', 'todas')}
        >
          Todas <span className={css.numeroChip}>{doUniverso.length}</span>
        </button>
        <button
          type="button"
          aria-pressed={filtros.aba === 'precisa'}
          className={`${css.chipFiltro} ${filtros.aba === 'precisa' ? css.chipLigado : ''} ${quantasPrecisam > 0 ? css.chipAtencao : ''}`}
          onClick={() => aoMudar('aba', filtros.aba === 'precisa' ? 'todas' : 'precisa')}
        >
          Precisa de você <span className={css.numeroChip}>{quantasPrecisam}</span>
        </button>
        <span className={css.divisorFiltros} aria-hidden="true" />
        {canais.map((c, i) => (
          <ChipCanal
            key={c.nome}
            canal={c}
            rotulo={c.nome}
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
