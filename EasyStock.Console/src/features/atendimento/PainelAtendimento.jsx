import { useEffect, useRef } from 'react'
import { Avatar } from '../../componentes/Avatar'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcaoDisponivel } from '../../aplicacao/useAcaoDisponivel'
import { conversaEncerrada, fronteirasDoFio } from '../../dominio/conversa'
import { MOTIVO_BLOQUEIO, permissaoDeEscrita } from '../../dominio/janela'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import { preencherModelo, primeiroNome } from '../../dominio/mensagem'
import { faixaDaJanela, preencherFaixa } from '../../dominio/entrega'
import { itemDaBibliotecaPelaRegra } from '../../dominio/respostas'
import { BarraModo } from './BarraModo'
import { Composer } from './Composer'
import { Thread } from './Thread'
import css from './atendimento.module.css'

export function PainelAtendimento({
  aoAbrirNota, aoAbrirGaleria, aoAbrirBiblioteca,
  focarAoTrocar = false,
}) {
  const {
    selecionada, visiveis, agora, automaticoPausado, rascunho,
  } = useAtendimento()
  const {
    enviar, reabrir, definirRascunho, assumirAtendimento, devolverAutomatico,
    selecionar, abrirEncerramento, reenviar,
  } = useAcoes()
  const { cardapio, janelas, canais } = useCatalogo()
  // #1474 (R2): reabrir conversa não tem endpoint no EasyStok; no modo API o botão some.
  const podeReabrir = useAcaoDisponivel()('reabrir')
  const cabecalhoRef = useRef(null)

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
          ? 'Selecione um nome no Balcão para ver as mensagens e continuar o atendimento.'
          : 'Confira a busca e os filtros do Balcão. As conversas recebidas aparecem por lá.'}
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
  const novidade = cardapio.find((i) => i.estoque > 0)
  const faixa = faixaDaJanela(janelas, selecionada.pedido?.janela)

  const aplicarTemplate = (template) => definirRascunho(
    selecionada.id,
    preencherFaixa(template.texto, faixa ?? 'o horário combinado')
      .replace('{nome}', primeiroNome(selecionada.nome)),
  )

  const enviarModelo = (modelo) => enviar(
    selecionada.id,
    preencherModelo(modelo, [primeiroNome(selecionada.nome), novidade?.nome ?? 'a novidade da casa']),
    { modelo: true },
  )

  return (
    <>
      <header className={css.cabecalho}>
        <span className={css.identidade}>
          <Avatar
            nome={selecionada.nome}
            foto={fotoDoCliente(canal, selecionada.cliente)}
            tamanho="grande"
          />
          <h2 tabIndex={-1} ref={cabecalhoRef} data-foco-reserva>
            {selecionada.nome}
            {/* Canal, histórico e janela em uma linha de dado. A janela era uma
                pílula colorida disputando com o nome; agora é texto. */}
            <small>
              <Icone nome={canal.icone} className={css['canal_' + canal.icone]} /> {canal.nome}
              {' · '}{selecionada.cliente.pedidos} {selecionada.cliente.pedidos === 1 ? 'pedido' : 'pedidos'}
              {' · '}<span title={canal.explicacao[0]}>
                {/* Rodada 12 (issue #16): "23 h 57 min na janela" não dizia de
                    que janela. Com prazo correndo, diz o que o prazo permite. */}
                {janela.restam > 0 && janela.textoLivre ? `escreve livre por ${janela.rotuloCurto}` : janela.rotulo}
              </span>
            </small>
          </h2>
        </span>
        <div className={css.direita} data-ancora-assistente>
          {/* Assumir e devolver ao automático moram na BarraModo, um lugar só.
              Aqui em cima fica o que é estado da conversa: bloqueio, encerrar
              e reabrir. */}
          {bloqueada && (
            <Pilula tom="perigo">
              <Icone nome="bloqueio" /> Bloqueado
            </Pilula>
          )}
          {/* Rodada 5, seção 5 (passo zero): "Encerrar" abre a modal do
              resumo (vazia até a F5 construir o conteúdo); com a conversa já
              encerrada, dá lugar ao "Reabrir conversa" que já existia. */}
          {!encerrada && !bloqueada && (
            <Botao variante="secundario" icone="log-out" onClick={() => abrirEncerramento(selecionada.id)}>
              Encerrar
            </Botao>
          )}
          {encerrada && !bloqueada && podeReabrir && (
            <Botao variante="primario" className={css.acaoToque} onClick={() => reabrir(selecionada.id)}>
              Reabrir conversa
            </Botao>
          )}
        </div>
      </header>

      {/* Uma área rolável só: barra de modo, conversa e painel do agente rolam
          juntos, e o composer fica preso no pé da coluna em qualquer largura. */}
      <div className={css.rolagemCentro}>
        <BarraModo
          conversa={selecionada}
          pausado={pausado}
          bloqueada={bloqueada}
          aoAssumir={() => assumirAtendimento(selecionada.id)}
          aoDevolver={() => devolverAutomatico(selecionada.id)}
          aoEncerrar={() => abrirEncerramento(selecionada.id)}
        />

        <Thread
          mensagens={selecionada.mensagens}
          chaveRolagem={selecionada.id}
          fronteiras={fronteirasDoFio(selecionada.atendimentos)}
          agora={agora}
          aoAbrirDefinicaoAutomatica={(regra) => aoAbrirBiblioteca(itemDaBibliotecaPelaRegra(regra))}
          aoReenviar={reenviar ? (mensagemId) => reenviar(selecionada.id, mensagemId) : undefined}
          digitando={digitando}
          notas={selecionada.cliente?.notas ?? []}
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
        rascunho={rascunho}
        aoMudarRascunho={(texto) => definirRascunho(selecionada.id, texto)}
        aoEnviar={() => enviar(selecionada.id, rascunho.trim())}
        aoEnviarModelo={enviarModelo}
        aoAplicarTemplate={aplicarTemplate}
        aoAbrirNota={aoAbrirNota}
        aoAbrirGaleria={aoAbrirGaleria}
        aoAbrirBiblioteca={aoAbrirBiblioteca}
        aoReabrir={podeReabrir ? () => reabrir(selecionada.id) : null}
      />
    </>
  )
}
