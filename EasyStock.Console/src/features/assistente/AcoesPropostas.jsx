import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { ACOES_DO_ASSISTENTE, descreverAcaoDoAssistente } from '../../dominio/acoesDoAssistente'
import css from './assistente.module.css'

const ICONE = {
  [ACOES_DO_ASSISTENTE.ENVIAR_CARDAPIO]: 'cardapio',
  [ACOES_DO_ASSISTENTE.NOTA_INTERNA]: 'nota',
  [ACOES_DO_ASSISTENTE.RASCUNHO]: 'modelo',
  [ACOES_DO_ASSISTENTE.ABRIR_TELA]: 'ficha',
}

// #1445: o que o assistente propôs fazer, uma linha por ação, com um botão só. Nada roda
// antes do clique; o único botão que manda algo ao cliente é o primário ("Enviar ao cliente").
// Ignorar a linha é descartar: sem botão a mais.
export function AcoesPropostas({ acoes, nomeCliente, aoExecutar }) {
  if (!acoes?.length) return null
  return (
    <ul className={css.acoesPropostas} aria-label="Ações sugeridas pelo assistente">
      {acoes.map((acao) => {
        const rotulo = descreverAcaoDoAssistente(acao, nomeCliente)
        if (!rotulo) return null
        return (
          <li key={acao.chave} className={css.acaoProposta}>
            <div className={css.acaoTexto}>
              <span className={css.acaoTitulo}><Icone nome={ICONE[acao.tipo]} tamanho={16} /> {rotulo.titulo}</span>
              {rotulo.detalhe && <span className={css.acaoDetalhe}>{rotulo.detalhe}</span>}
              {acao.estado === 'falhou' && (
                <span className={css.acaoErro}><Icone nome="alerta" tamanho={14} /> {acao.erro}</span>
              )}
            </div>
            {acao.estado === 'feita' ? (
              <span className={css.acaoFeita}><Icone nome="check" tamanho={16} /> {rotulo.feito}</span>
            ) : (
              <Botao
                variante={rotulo.saiParaCliente ? 'primario' : 'secundario'}
                className={css.acaoBotao}
                disabled={acao.estado === 'executando'}
                onClick={() => aoExecutar(acao)}
              >
                {acao.estado === 'executando' ? 'Fazendo…' : rotulo.botao}
              </Botao>
            )}
          </li>
        )
      })}
    </ul>
  )
}
