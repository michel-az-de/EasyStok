import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { ocupacaoDeHoje, resumoDeHoje } from '../../dominio/entrega'
import css from './entregas.module.css'

// Resumo do topo: "12h30: 2 de 4". A capacidade da operação é a coisa que ela
// mais teme estourar (áudio 05, D9), e informação que só existe depois de dois
// cliques é informação que não existe numa cozinha.
//
// Em celular as pílulas somem por CSS e sobra o botão: o topo não pode virar
// parede de texto na tela pequena.
export function ResumoEntregas({ aoAbrir, noTrilho = false, classeItem, classeSelo }) {
  const { conversas, agora } = useAtendimento()
  const { janelas, cardapio } = useCatalogo()

  const ocupacoes = ocupacaoDeHoje(janelas, conversas, agora, null, cardapio)
  // Só entra na barra a janela que aperta. Listar as quatro faixas fazia o topo
  // quebrar em duas linhas, e capacidade folgada não muda decisão nenhuma.
  const apertadas = resumoDeHoje(ocupacoes).filter((j) => j.estourou || j.chave === 'cheia')

  // No trilho lateral (desktop), a janela estourada vira selo vermelho no
  // próprio ícone de Entregas: continua visível sem clique, como a seção pede.
  if (noTrilho) {
    const texto = apertadas.map((j) => j.texto).join(', ')
    return (
      <Botao variante="texto" className={classeItem} onClick={aoAbrir} title={texto ? `Entregas, ${texto}` : 'Entregas'}>
        <Icone nome="moto" tamanho={22} />
        <span>Entregas</span>
        {apertadas.length > 0 && <span className={classeSelo} aria-hidden="true">{apertadas[0].texto.split('·').pop().trim()}</span>}
      </Botao>
    )
  }

  return (
    <div className={css.resumo}>
      {/* Ocupação é número, não estado que pede ação: sai em texto, e só a
          janela estourada ganha peso e cor. */}
      <span className={css.chips}>
        {apertadas.map((janela) => (
          <span key={janela.id} className={css.chipCheio}>{janela.texto}</span>
        ))}
      </span>
      <Botao icone="moto" onClick={aoAbrir}>
        Entregas
      </Botao>
    </div>
  )
}
