// Peças comuns aos formulários de ajuste (#2070): pendência, retorno do Salvar, erros de validação e
// falha de leitura, com o mesmo texto em todas as telas.
import { Botao } from '../../componentes/Botao'
import css from './formularioDeAjuste.module.css'

export function Pendencia({ visivel }) {
  return <output className={visivel ? css.pendencia : 'sr'}>{visivel ? 'Alterações não salvas. Salve ou desfaça antes de sair.' : ''}</output>
}

// O erro da API nunca some calado: fica ao lado do botão até a próxima edição.
export function Retorno({ situacao, salvo = 'Salvo no EasyStok.' }) {
  if (situacao.estado === 'salvando') return <output className={css.retorno}>Salvando…</output>
  if (situacao.estado === 'salvo') return <output className={css.retorno}>{salvo}</output>
  if (situacao.estado === 'erro') {
    return <p className={css.erro} role="alert">Não foi possível salvar. {situacao.mensagem} Sua edição continua na tela.</p>
  }
  return null
}

// `rotulos` diz de qual campo é cada mensagem.
export function Erros({ erros, rotulos }) {
  const lista = Object.entries(erros ?? {})
  if (lista.length === 0) return null
  return (
    <ul className={css.erros} role="alert">
      {lista.map(([campo, mensagem]) => <li key={campo}>{rotulos?.[campo] ? `${rotulos[campo]}: ` : ''}{mensagem}</li>)}
    </ul>
  )
}

export function FalhaDeLeitura({ mensagem, aoTentarDeNovo }) {
  return (
    <div className={css.secao} role="alert">
      <p className={css.erro}>{mensagem}</p>
      <div className={css.acoes}>
        <Botao className={css.toque} onClick={aoTentarDeNovo}>Tentar novamente</Botao>
      </div>
    </div>
  )
}
