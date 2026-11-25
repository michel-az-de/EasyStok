import { useCallback, useRef, useState } from 'react'
import { AssuntoEmail } from '../../componentes/AssuntoEmail'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { marcarMudancasDeAssunto } from '../../dominio/email'
import { agruparFalhasSeguidas, marcarNaConversa, marcasDaConversa } from '../../dominio/falhasSeguidas'
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
      <span>Atendimento {numero} concluído<span className={css.divisoriaResto}> · novo atendimento a seguir</span></span>
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
// cliente e ninguém via o Assistente trabalhar. Os três pontos animam; com
// movimento reduzido (base.css) ficam parados e o texto continua dizendo.
function Digitando() {
  return (
    <div className={`${css.balao} ${css.saida} ${css.doAutomatico} ${css.digitando}`}>
      <span className={css.pontos} aria-hidden="true"><i /><i /><i /></span>
      <span className={css.meta}>
        <span className={css.selo}><Icone nome="agente" /> Assistente digitando</span>
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
      <details className={css.eventoNota}>
        <summary aria-label={`Nota interna${nota.autor ? ` de ${nota.autor}` : ''}${nota.em ? `, ${nota.em}` : ''}`}><span className={css.postItQuem}><Icone nome="lock" /> Nota interna · {nota.autor} · {nota.em}</span><span className={css.verNota}>Ver nota</span></summary>
        <p>{nota.texto}</p>
      </details>
    </aside>
  )
}

export function Thread({
  mensagens, chaveRolagem, fronteiras = [], agora, aoAbrirDefinicaoAutomatica, aoReenviar, aoAbrirModelos, digitando = false, notas = [], visivel = true,
  temAnteriores = false, abrindoAnteriores = false, aoVerAnteriores, numeroComanda = null, aoReagir, aoApagar,
}) {
  const fim = useRef(null)
  // #3300 (F8): as mensagens que já estavam no fio quando a conversa abriu não animam; só a que chega depois.
  // Ajuste de estado no render quando a conversa muda (padrão do React), sem efeito nem ref.
  const [base, setBase] = useState(() => ({ chave: chaveRolagem, ids: new Set(mensagens.map((m) => m.id)) }))
  if (base.chave !== chaveRolagem || (base.ids.size === 0 && mensagens.length > 0)) {
    setBase({ chave: chaveRolagem, ids: new Set(mensagens.map((m) => m.id)) })
  }
  // #1577: o pai recria os dois handlers a cada render; com identidade fixa o `memo` do balão segura.
  const abrirDefinicaoAutomatica = useChamadaEstavel(aoAbrirDefinicaoAutomatica)
  const reenviar = useChamadaEstavel(aoReenviar)
  const abrirModelos = useChamadaEstavel(aoAbrirModelos)
  const reagir = useChamadaEstavel(aoReagir)
  const apagar = useChamadaEstavel(aoApagar)
  // #3234 (C1): por conversa (o fio não remonta ao trocar), os grupos de falhas abertos e as mensagens que a
  // atendente reenviou soltas, que não somem num grupo. A regra está em dominio/falhasSeguidas.
  const [marcas, setMarcas] = useState({})
  const daConversa = marcasDaConversa(marcas, chaveRolagem)
  const marcar = useCallback(
    (campo, id, alternar) => setMarcas((antes) => marcarNaConversa(antes, chaveRolagem, campo, id, alternar)), [chaveRolagem],
  )
  const alternarTentativas = useCallback((id) => marcar('abertas', id, true), [marcar])
  const reenviarSolta = useCallback((id) => { marcar('soltas', id, false); return reenviar(id) }, [marcar, reenviar])

  // #1587: não rola mais a cada mensagem; a regra está no hook.
  // #2232 (A5): `visivel` é o painel aberto na aba; ao abrir, o fio vai para a última mensagem.
  const { novas, irParaNovas } = useRolagemDoFio(fim, { mensagens, chaveRolagem, digitando, totalDeNotas: notas.length, visivel })

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

  // #2232 (B3): cada dia é um grupo, para a pílula de data ficar fixa só enquanto o dia dela rola.
  // #3234 (C1): antes, as falhas seguidas iguais viram uma linha só (o grupo não cruza dia, nota nem divisória).
  // Aberto, as tentativas de antes entram como linhas próprias acima da mais recente: cada balão fica com a chave
  // do seu id, entrando ou saindo de um grupo, e não remonta (o retorno do Reenviar e o foco ficam).
  const grupos = []
  const juntar = (l) => {
    const atual = grupos.at(-1)
    if (l.tipo === 'dia') grupos.push({ chave: l.chave, itens: [l] })
    else if (atual) atual.itens.push(l)
    else grupos.push({ chave: l.chave, itens: [l], solto: true })
  }
  for (const l of agruparFalhasSeguidas(linhas, { soltas: daConversa.soltas })) {
    if (!l.anteriores?.length) { juntar(l); continue }
    const total = l.anteriores.length + 1
    const aberto = daConversa.abertas.has(l.chaveDoGrupo)
    if (aberto) {
      l.anteriores.forEach((m, j) => juntar({
        tipo: 'tentativa', chave: m.id, mensagem: m, trocaDeVoz: j === 0 && l.trocaDeVoz, rotulo: `Tentativa ${j + 1} de ${total}`,
      }))
    }
    juntar({ ...l, trocaDeVoz: !aberto && l.trocaDeVoz, tentativas: total, aberto })
  }
  const desenhar = (l) => {
    if (l.tipo === 'divisoria') return <Divisoria key={l.chave} numero={l.numero} />
    if (l.tipo === 'dia') return <SeparadorDeDia key={l.chave} rotulo={l.rotulo} />
    if (l.tipo === 'nota') return <PostIt key={l.chave} nota={l.nota} />
    if (l.tipo === 'assunto') return <AssuntoEmail key={l.chave} assunto={l.assunto} noFio />
    const comuns = {
      numeroComanda,
      aoAbrirDefinicaoAutomatica: abrirDefinicaoAutomatica,
      // #1761: só onde o compositor já oferece Modelos aprovados.
      aoAbrirModelos: aoAbrirModelos ? abrirModelos : undefined,
      // #2833: reagir e apagar para todos só com a API.
      aoReagir: aoReagir ? reagir : undefined,
      aoApagar: aoApagar ? apagar : undefined,
    }
    // #3234 (C1): a tentativa de antes, aberta a pedido, não repete o aviso nem tem Reenviar (fica só na última).
    if (l.tipo === 'tentativa') return <Balao key={l.chave} mensagem={l.mensagem} novo={!base.ids.has(l.mensagem.id)} trocaDeVoz={l.trocaDeVoz} rotuloDaTentativa={l.rotulo} {...comuns} />
    // Sem a ação (demonstração) o balão não mostra o Reenviar. Fora de grupo, quem reenvia fica solta.
    const aoReenviarAqui = aoReenviar ? (l.tentativas ? reenviar : reenviarSolta) : undefined
    if (!l.tentativas) return <Balao key={l.chave} mensagem={l.mensagem} novo={!base.ids.has(l.mensagem.id)} trocaDeVoz={l.trocaDeVoz} {...comuns} aoReenviar={aoReenviarAqui} />
    return (
      <Balao
        key={l.chave} mensagem={l.mensagem} novo={!base.ids.has(l.mensagem.id)} trocaDeVoz={l.trocaDeVoz} {...comuns} aoReenviar={aoReenviarAqui}
        tentativas={l.tentativas} tentativasAbertas={l.aberto} aoAlternarTentativas={alternarTentativas} chaveDoGrupo={l.chaveDoGrupo}
      />
    )
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
      {grupos.map((g) => (g.solto
        ? g.itens.map(desenhar)
        : <div key={g.chave} className={css.grupoDia}>{g.itens.map(desenhar)}</div>))}
      {digitando && <Digitando />}
      {novas > 0 && (
        <Botao variante="primario" icone="arrow-up" className={css.novasMensagens} onClick={irParaNovas}>
          Novas mensagens ({novas})
        </Botao>
      )}
      <div ref={fim} />
    </div>
  )
}
