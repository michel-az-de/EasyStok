import { useEffect, useRef, useState } from 'react'
import { Avatar } from '../../componentes/Avatar'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcaoDisponivel } from '../../aplicacao/useAcaoDisponivel'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { conversaEncerrada, fronteirasDoFio } from '../../dominio/conversa'
import { MOTIVO_BLOQUEIO, modelosAVista, permissaoDeEscrita, prazoNoCabecalho } from '../../dominio/janela'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import { primeiroNome } from '../../dominio/mensagem'
import { assistenteAtendendo } from '../../dominio/resumoDoModo'
import { faixaDaJanela, preencherFaixa } from '../../dominio/entrega'
import { itemDaBibliotecaPelaRegra } from '../../dominio/respostas'
import { BarraModo } from './BarraModo'
import { Composer } from './Composer'
import { CompartilharComanda } from './CompartilharComanda'
import { PassarConversa } from './PassarConversa'
import { Thread } from './Thread'
import css from './atendimento.module.css'

export function PainelAtendimento({
  aoAbrirNota, aoAbrirGaleria, aoAbrirBiblioteca,
  focarAoTrocar = false,
}) {
  const {
    selecionada, visiveis, agora, automaticoPausado, rascunho, escritaDeFora, fonteApi,
  } = useAtendimento()
  const {
    enviar, reabrir, definirRascunho, assumirAtendimento, devolverAutomatico,
    selecionar, abrirEncerramento, reenviar, carregarMensagensAnteriores,
  } = useAcoes()
  const { janelas, canais } = useCatalogo()
  const fotoCadastrada = useFotoCliente(selecionada?.cliente?.fotoUrl)
  const { acoes: capacidades } = useAcessoModulos()
  const reabrirLigado = useAcaoDisponivel()('reabrir')
  const podeReabrir = reabrirLigado && (!fonteApi || capacidades.reabrirConversa === true)
  const cabecalhoRef = useRef(null)
  const reabrindoRef = useRef(false)
  const [reabrindo, setReabrindo] = useState(false)
  // #1761: "Modelos aprovados" abre do compositor e da bolha recusada fora da janela de 24 h.
  // Guardado pelo id da conversa: trocar de conversa fecha.
  const [modelosDe, setModelosDe] = useState(null)

  // #2005 (console-09): só o modo API tem a ação; na demonstração o fio já é inteiro.
  const anterioresEmCurso = useRef(false)
  const [abrindoAnteriores, setAbrindoAnteriores] = useState(false)
  async function aoVerAnteriores() {
    if (anterioresEmCurso.current || !selecionada) return
    anterioresEmCurso.current = true
    setAbrindoAnteriores(true)
    try { await carregarMensagensAnteriores(selecionada.id) }
    finally { anterioresEmCurso.current = false; setAbrindoAnteriores(false) }
  }

  async function aoReabrir() {
    if (reabrindoRef.current || !selecionada) return
    reabrindoRef.current = true
    setReabrindo(true)
    try { await reabrir(selecionada.id) }
    finally { reabrindoRef.current = false; setReabrindo(false) }
  }

  useEffect(() => {
    if (focarAoTrocar) cabecalhoRef.current?.focus()
  }, [focarAoTrocar, selecionada?.id])

  if (!selecionada) {
    const primeira = visiveis[0]
    return (
      <Vazio
        titulo={primeira ? 'Escolha uma conversa' : 'Nenhuma conversa para exibir'}
        acao={primeira && (
          <Botao variante="primario" onClick={() => selecionar(primeira.id)}>
            Abrir {primeira.nome.split(' ')[0]}
          </Botao>
        )}
      >
        {primeira
          ? 'Escolha um cliente na lista para ver as mensagens.'
          : 'Altere a busca ou os filtros para encontrar uma conversa.'}
      </Vazio>
    )
  }

  const canal = canalDaConversa(canais, selecionada)
  const { pode, ofereceModelo, janela, motivo } = permissaoDeEscrita(selecionada, agora, canal)
  // Valor CRU, não booleano: a marca PAUSA_POR_ASSUMIR é o que separa "pausado
  // porque você escreveu" de "pausado porque você assumiu". O Boolean() de
  // antes apagava a diferença e a barra dizia a frase errada.
  const pausado = automaticoPausado[selecionada.id] ?? false
  const encerrada = conversaEncerrada(selecionada)
  // Uma conta só na tela: quem diz que a conversa está bloqueada é a mesma
  // permissao de escrita que decide o composer e o reducer.
  const bloqueada = motivo?.acao === MOTIVO_BLOQUEIO
  // Rodada 11 (registro 92): resposta automática na fila e automático ainda
  // conduzindo. Assumir apaga o "digitando" no mesmo toque.
  const digitando = Boolean(selecionada.respostaPendente?.length) && !pausado && !bloqueada
  const faixa = faixaDaJanela(janelas, selecionada.pedido?.janela)
  // #1551: modelo aprovado só existe com a API; na demonstração o botão só devolvia erro.
  const podeEnviarModelo = fonteApi && podeReabrir && canal.aceitaModelo && !encerrada && !bloqueada
  const prazo = prazoNoCabecalho(janela)

  const aplicarTemplate = (template) => definirRascunho(
    selecionada.id,
    preencherFaixa(template.texto, faixa ?? 'o horário combinado')
      .replace('{nome}', primeiroNome(selecionada.nome)),
  )

  return (
    <>
      <header className={css.cabecalho}>
        <span className={css.identidade}>
          <Avatar
            nome={selecionada.nome}
            foto={fotoCadastrada.url ?? fotoDoCliente(canal, selecionada.cliente)}
            tamanho="grande"
          />
          <h2 tabIndex={-1} ref={cabecalhoRef} data-foco-reserva>
            <span className={css.nomeCliente} title={selecionada.nome}>{selecionada.nome}</span>
            {/* Comanda, canal e, quando pede atenção, o prazo da janela, em uma linha.
                #1761 (L4): a quantidade de pedidos saiu (está em Histórico) e o prazo só aparece
                perto do fim (`prazoNoCabecalho`); a linha quebrava em duas. */}
            <small>
              {selecionada.numeroComanda && <>Comanda {selecionada.numeroComanda} · </>}
              <Icone nome={canal.icone} className={css['canal_' + canal.icone]} /> {canal.nome}
              {prazo && <>{' · '}<span title={canal.explicacao[0]}>{prazo}</span></>}
            </small>
          </h2>
        </span>
        <div className={css.direita} data-ancora-assistente>
          {bloqueada && <Pilula tom="perigo"><Icone nome="bloqueio" /> Bloqueado</Pilula>}
          {!encerrada && !bloqueada && <details key={selecionada.id} className={css.opcoesConversa} onKeyDown={(evento) => {
            if (evento.key !== 'Escape' || evento.defaultPrevented || evento.target.closest('dialog')) return
            evento.preventDefault(); evento.stopPropagation()
            evento.currentTarget.open = false
            evento.currentTarget.querySelector('summary')?.focus()
          }}>
            <summary aria-label="Opções da conversa" title="Opções da conversa"><Icone nome="ellipsis" /></summary>
            <div className={css.menuConversa} role="group" aria-label="Ações da conversa">
          {/* #1989: irmãos com a mesma chave (o id da conversa) faziam o React perder a conta e
              deixar um "Link de compra" velho a cada troca de conversa. Cada um tem o seu prefixo. */}
          {!encerrada && !bloqueada && <CompartilharComanda key={'compartilhar-' + selecionada.id} conversa={selecionada} canalNome={canal.nome} podeEnviar={pode} />}
          {/* Assumir e devolver ao automático moram na BarraModo, um lugar só.
              Aqui em cima fica o que é estado da conversa: bloqueio, encerrar
              e reabrir. */}
          {/* Rodada 5, seção 5 (passo zero): "Encerrar" abre a modal do
              resumo (vazia até a F5 construir o conteúdo); com a conversa já
              encerrada, dá lugar ao "Reabrir conversa" que já existia. */}
          {/* #1239: passar a conversa para outra atendente (só no modo API). */}
          {fonteApi && !encerrada && !bloqueada && <PassarConversa key={'passar-' + selecionada.id} conversa={selecionada} />}
          {!encerrada && !bloqueada && (
            <Botao variante="texto" icone="circle-check" className={css.encerrar} aria-label="Concluir conversa" title="Concluir conversa" onClick={() => abrirEncerramento(selecionada.id)}>
              Concluir conversa
            </Botao>
          )}
            </div>
          </details>}
        </div>
      </header>

      {/* #2134: quem responde a conversa e o Assumir ficam fixos entre o cabeçalho e a conversa.
          Dentro da área que rola, o aviso saía da tela (y=-228 px em 1280x720) e o campo seguia
          enviando como pessoa por cima do Assistente. */}
      <BarraModo
        conversa={selecionada}
        pausado={pausado}
        bloqueada={bloqueada}
        aoAssumir={() => assumirAtendimento(selecionada.id)}
        aoDevolver={() => devolverAutomatico(selecionada.id)}
        aoEncerrar={() => abrirEncerramento(selecionada.id)}
      />

      {/* Uma área rolável só: conversa e painel do agente rolam juntos, e o composer fica
          preso no pé da coluna em qualquer largura. */}
      <div className={css.rolagemCentro}>
        <Thread
          mensagens={selecionada.mensagens}
          chaveRolagem={selecionada.id}
          fronteiras={fronteirasDoFio(selecionada.atendimentos)}
          agora={agora}
          aoAbrirDefinicaoAutomatica={(regra) => aoAbrirBiblioteca(itemDaBibliotecaPelaRegra(regra))}
          aoReenviar={reenviar ? (mensagemId) => reenviar(selecionada.id, mensagemId) : undefined}
          aoAbrirModelos={podeEnviarModelo ? () => setModelosDe(selecionada.id) : undefined}
          digitando={digitando}
          notas={selecionada.cliente?.notas ?? []}
          temAnteriores={Boolean(selecionada.temAnteriores) && !selecionada.anterioresEsgotadas}
          abrindoAnteriores={abrindoAnteriores}
          aoVerAnteriores={carregarMensagensAnteriores ? aoVerAnteriores : undefined}
        />
      </div>

      {/* #1510: remonta por conversa; anexo em espera e gravação não passam para a próxima. */}
      <Composer
        key={selecionada.id}
        conversa={selecionada}
        canal={canal}
        podeEscrever={pode}
        motivo={motivo}
        ofereceModelo={ofereceModelo}
        // #1577: o texto digitado mora no campo; daqui desce o rascunho global e o carimbo de quem
        // escreveu nele de fora (sugestão, assistente, limpar do envio).
        rascunho={rascunho}
        escritaDeFora={escritaDeFora}
        aoEnviar={(texto) => enviar(selecionada.id, texto.trim())}
        // #2134: responder com o Assistente atendendo é assumir, pelo mesmo caminho do botão Assumir.
        aoAssumirAntes={assistenteAtendendo(selecionada, pausado, bloqueada) ? () => assumirAtendimento(selecionada.id) : null}
        podeEnviarModelo={podeEnviarModelo}
        modelosFixos={modelosAVista(janela)}
        modeloAberto={modelosDe === selecionada.id}
        aoAbrirModelos={() => setModelosDe(selecionada.id)}
        aoFecharModelos={() => setModelosDe(null)}
        aoAplicarTemplate={aplicarTemplate}
        aoAbrirNota={aoAbrirNota}
        aoAbrirGaleria={aoAbrirGaleria}
        aoAbrirBiblioteca={aoAbrirBiblioteca}
        aoReabrir={podeReabrir ? aoReabrir : null}
        reabrindo={reabrindo}
      />
    </>
  )
}
