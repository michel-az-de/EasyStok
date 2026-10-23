import { Component } from 'react'
import { Botao } from './Botao'
import { Vazio } from './Vazio'

// Contenção de erro (frente C da #1889): um bloco que quebra no render deixava a tela inteira em
// branco, e a atendente perdia a lista e a conversa junto com a Ficha. Cada coluna do Balcão
// (lista, conversa, Resumo) e cada tela de módulo fica dentro de um limite: a parte que quebrou
// mostra o que houve e "Recarregar esta parte"; o resto da tela continua de pé.
//
// "Recarregar" monta a parte de novo (a `key` muda). O estado do console (conversas, rascunhos)
// mora fora dela e não se perde. Erro de evento ou de promessa não chega aqui: esse já volta
// como `{ erro }` ou aviso na faixa.
export const TEXTO_DO_LIMITE = 'Esta parte da tela parou por um erro. O resto do console continua funcionando.'

export class LimiteDeErro extends Component {
  constructor(props) {
    super(props)
    this.state = { erro: null, montagem: 0 }
    this.recarregar = this.recarregar.bind(this)
  }

  static getDerivedStateFromError(erro) {
    return { erro }
  }

  componentDidCatch(erro, info) {
    // eslint-disable-next-line no-console -- o erro precisa ficar no console do navegador para o diagnóstico
    console.error(`[LimiteDeErro] ${this.props.parte ?? 'parte da tela'}:`, erro, info?.componentStack ?? '')
  }

  recarregar() {
    this.setState((atual) => ({ erro: null, montagem: atual.montagem + 1 }))
  }

  render() {
    const { parte, children } = this.props
    if (this.state.erro) {
      return (
        <Vazio
          como="output"
          icone="alerta"
          titulo={parte ? `${parte}: não deu para mostrar` : 'Não deu para mostrar esta parte'}
          acao={<Botao variante="primario" icone="refresh-cw" onClick={this.recarregar}>Recarregar esta parte</Botao>}
        >
          {TEXTO_DO_LIMITE}
        </Vazio>
      )
    }
    return <LimiteMontado key={this.state.montagem}>{children}</LimiteMontado>
  }
}

// A `key` do recarregar fica num invólucro sem DOM: trocar a key remonta só os filhos.
function LimiteMontado({ children }) {
  return children
}
