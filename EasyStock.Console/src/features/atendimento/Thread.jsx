import { useRef } from 'react'
import { AssuntoEmail } from '../../componentes/AssuntoEmail'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { marcarMudancasDeAssunto } from '../../dominio/email'
import { Pilula } from '../../componentes/Pilula'
import { inicioDoDiaNoFuso, rotuloDoDia } from '../../dominio/formato'
import { intercalarNotas } from '../../dominio/notas'
import { useChamadaEstavel } from '../../hooks/useChamadaEstavel'
import { Balao } from './Balao'
import { useRolagemDoFio } from './useRolagemDoFio'
import css from './atendimento.module.css'

// Divisória entre atendimentos (decisão 46, "Encerrar" v2): encerrado nunca
// reabre o MESMO atendimento — a próxima mensagem do cliente começa um
// atendimento novo na mesma conversa, e o fio marca essa fronteira em vez de
// deixar a passagem muda entre duas mensagens quaisquer.
function Divisoria({ numero }) {
  return (
    <div className={css.divisoriaAtendimento} role="separator">
      <span>Atendimento {numero} encerrado · novo atendimento a seguir</span>
    </div>
  )
}

// Separador de dia (achado 8, pendência 18 da banca 64): pílula "Hoje",
// "Ontem" ou a data, antes da primeira mensagem de cada dia. Sem isto, duas
// mensagens de dias diferentes (Sandra: 21:45 de um dia, 10:05 do seguinte)
// ficavam uma embaixo da outra sem nenhuma marca de que o dia mudou.
function SeparadorDeDia({ rotulo }) {
  return (
    <div className={css.separadorDia} role="separator">
      <Pilula tom="neutro">{rotulo}</Pilula>
    </div>
  )
}

// Rodada 11 (issue #8, registro 92): a casa "digitando" antes da resposta
// automática sair, do lado de quem responde, com o mesmo selo "automática"
// da bolha que vem a seguir. Sem isto a resposta caía pronta junto da fala do
// cliente e ninguém via o automático trabalhar. Os três pontos animam; com
// movimento reduzido (base.css) ficam parados e o texto continua dizendo.
function Digitando() {
  return (
    <div className={`${css.balao} ${css.saida} ${css.doAutomatico} ${css.digitando}`}>
      <span className={css.pontos} aria-hidden="true"><i /><i /><i /></span>
      <span className={css.meta}>
        <span className={css.selo}><Icone nome="raio" /> automática digitando</span>
      </span>
    </div>
  )
}

// Nota interna como post-it (#1441): entra no fio pela hora em que foi escrita, do
// lado de quem atende, e nunca vai ao cliente. A nota é do cadastro, então uma nota
// de outra conversa do mesmo cliente também aparece aqui, na hora dela.
function PostIt({ nota }) {
  return (
    <aside className={css.postIt} aria-label="Nota interna">
      <span className={css.postItQuem}><Icone nome="nota" /> Nota interna · {nota.autor} · {nota.em}</span>
      <p>{nota.texto}</p>
    </aside>
  )
}

export function Thread({
  mensagens, chaveRolagem, fronteiras = [], agora, aoAbrirDefinicaoAutomatica, aoReenviar, aoAbrirModelos, digitando = false, notas = [],
  temAnteriores = false, abrindoAnteriores = false, aoVerAnteriores,
}) {
  const fim = useRef(null)
  // #1577: o pai recria os dois handlers a cada render; com identidade fixa o `memo` do balão segura.
  const abrirDefinicaoAutomatica = useChamadaEstavel(aoAbrirDefinicaoAutomatica)
  const reenviar = useChamadaEstavel(aoReenviar)
  const abrirModelos = useChamadaEstavel(aoAbrirModelos)

  // #1587: não rola mais a cada mensagem; a regra está no hook.
  useRolagemDoFio(fim, { mensagens, chaveRolagem, digitando, totalDeNotas: notas.length })

  // Intercala as divisórias na posição certa: por ÍNDICE (`aposIndice`), não
  // por horário — o relógio simulado anda em passos e duas mensagens de
  // atendimentos diferentes podem nascer no mesmo instante gravado.
  // `trocaDeVoz` continua olhando `mensagens` puro (índice `i`), a divisória
  // não é remetente e não deve contar como troca de voz.
  let proxima = 0
  const linhas = []
  // #1432: assunto acima do e-mail recebido, só quando muda.
  const assuntos = marcarMudancasDeAssunto(mensagens)
  let i = -1
  let diaAnterior = null
  for (const item of intercalarNotas(mensagens, notas)) {
    if (item.tipo === 'nota') {
      linhas.push({ tipo: 'nota', chave: `nota-${item.nota.id}`, nota: item.nota })
      continue
    }
    i += 1
    const m = item.mensagem
    while (proxima < fronteiras.length && i > fronteiras[proxima].aposIndice) {
      linhas.push({ tipo: 'divisoria', chave: `divisoria-${fronteiras[proxima].numero}`, numero: fronteiras[proxima].numero })
      proxima += 1
    }
    // #1600: quebra no dia da loja, o mesmo critério de `rotuloDoDia`. O dia da mensagem anterior
    // fica guardado: cada conta de fuso custa caro num fio longo.
    const diaDaMensagem = inicioDoDiaNoFuso(m.em)
    if (i === 0 || diaDaMensagem !== diaAnterior) {
      linhas.push({ tipo: 'dia', chave: `dia-${m.id}`, rotulo: rotuloDoDia(m.em, agora) })
    }
    diaAnterior = diaDaMensagem
    if (assuntos[i]) linhas.push({ tipo: 'assunto', chave: `assunto-${m.id}`, assunto: assuntos[i] })
    linhas.push({
      tipo: 'mensagem', chave: m.id, mensagem: m, trocaDeVoz: i > 0 && mensagens[i - 1].dir !== m.dir,
    })
  }

  return (
    <div className={css.thread} role="log" aria-live="polite" aria-label="Mensagens da conversa">
      {/* #2005 (console-09): o fio traz as 100 mais recentes; as de antes abrem sob demanda. */}
      {temAnteriores && aoVerAnteriores && (
        <div className={css.verAnteriores}>
          <Botao variante="secundario" disabled={abrindoAnteriores} onClick={aoVerAnteriores}>
            {abrindoAnteriores ? 'Abrindo…' : 'Ver mensagens anteriores'}
          </Botao>
        </div>
      )}
      {linhas.map((l) => {
        if (l.tipo === 'divisoria') return <Divisoria key={l.chave} numero={l.numero} />
        if (l.tipo === 'dia') return <SeparadorDeDia key={l.chave} rotulo={l.rotulo} />
        if (l.tipo === 'nota') return <PostIt key={l.chave} nota={l.nota} />
        if (l.tipo === 'assunto') return <AssuntoEmail key={l.chave} assunto={l.assunto} noFio />
        return (
          <Balao
            key={l.chave}
            mensagem={l.mensagem}
            trocaDeVoz={l.trocaDeVoz}
            aoAbrirDefinicaoAutomatica={abrirDefinicaoAutomatica}
            // Sem a ação (demonstração) o balão não mostra o Reenviar.
            aoReenviar={aoReenviar ? reenviar : undefined}
            // #1761: só onde o compositor já oferece Modelos aprovados.
            aoAbrirModelos={aoAbrirModelos ? abrirModelos : undefined}
          />
        )
      })}
      {digitando && <Digitando />}
      <div ref={fim} />
    </div>
  )
}
