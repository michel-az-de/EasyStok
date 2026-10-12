import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import {
  VARIAVEIS, VARIAVEIS_DA_API, avisoDaAutomaticaDeEntrada, contextoDePrevia, textoDaRegra, variaveisForaDaApi,
} from '../../dominio/automacao'
import { ROTULO_DO_SOM, listaDeEventosDeSom } from '../../dominio/automatico'
import { canalDaConversa } from '../../dominio/canal'
import { DIAS_DA_SEMANA, descreverProximaAbertura } from '../../dominio/funcionamento'
import { faixaDaJanela } from '../../dominio/entrega'
import { permissaoDeEscrita } from '../../dominio/janela'
import css from './automacoes.module.css'

const AVISOS_DE_SOM = listaDeEventosDeSom().map((e) => ROTULO_DO_SOM[e]).join(', ')

// #1474, modo API: o EasyStok só preenche {nome}, {pedido} e {faixa}; a prévia não inventa o
// resto ({abre}, {linkCardapio} saem escritos assim para o cliente) e a tela avisa.
const CHAVES_DA_API = new Set(VARIAVEIS_DA_API.map((v) => v.chave))
const soDaApi = (contexto) => Object.fromEntries(Object.entries(contexto).filter(([chave]) => CHAVES_DA_API.has(chave)))

function Regra({
  regra, contexto, aoAlternar, aoEditar, nomeConversa, podeEnviar, aoEnviar, fonteApi, avisoApi,
}) {
  const [rascunho, setRascunho] = useState(null)
  // ocioso | salvando | salvo | erro: o resultado aparece aqui dentro, não atrás do modal.
  const [gravacao, setGravacao] = useState('ocioso')

  const valor = rascunho ?? { texto: regra.texto }
  const editando = valor.texto !== regra.texto
  const previa = textoDaRegra({ ...regra, texto: valor.texto }, fonteApi ? soDaApi(contexto) : contexto)
  const variaveis = fonteApi ? VARIAVEIS_DA_API : VARIAVEIS
  const foraDaApi = fonteApi ? variaveisForaDaApi(valor.texto) : []
  const avisoEntrada = fonteApi ? avisoDaAutomaticaDeEntrada(regra) : null

  const mudar = (campo, novo) => {
    setGravacao('ocioso')
    setRascunho({ ...valor, [campo]: novo })
  }

  // Espera a API: só limpa o rascunho quando gravou. A ação do modo API devolve `false` quando
  // recusou (o motivo vai para o aviso da faixa); a da demonstração não devolve nada.
  const salvar = async () => {
    setGravacao('salvando')
    let ok
    try {
      ok = await aoEditar(regra.id, { texto: valor.texto.trim() })
    } catch {
      ok = false
    }
    if (ok === false) {
      setGravacao('erro')
      return
    }
    setRascunho(null)
    setGravacao('salvo')
  }

  return (
    <li className={`${css.regra} ${regra.ativa ? '' : css.desligada}`}>
      <div className={css.topo}>
        <strong>{regra.nome}</strong>
        {/* Chave com rótulo de estado. Ligado e desligado não podem ser
            parecidos: o rótulo diz, a cor só acompanha. */}
        <label className={css.chave}>
          <input
            type="checkbox"
            aria-label={`Regra ${regra.nome}, ${regra.ativa ? 'ligada' : 'desligada'}`}
            checked={regra.ativa}
            onChange={() => aoAlternar(regra.id)}
          />
          {regra.ativa ? 'ligada' : 'desligada'}
        </label>
      </div>
      <p className={css.descricao}>{regra.descricao}</p>

      <CampoArea
        rotulo={`Texto de ${regra.nome}`}
        value={valor.texto}
        onChange={(e) => mudar('texto', e.target.value)}
      />
      <p className={css.variaveis}>
        <span>Variáveis:</span>
        {variaveis.map((v) => <span key={v.chave} className={css.tokenVariavel}>{`{${v.chave}}`}</span>)}
      </p>
      {foraDaApi.length > 0 && (
        <p className={css.alerta} role="alert">
          {foraDaApi.map((v) => `{${v}}`).join(', ')} não é preenchida pelo EasyStok e sai escrita assim para o cliente.
        </p>
      )}
      {avisoEntrada && <p className={css.alerta}>{avisoEntrada}</p>}

      {/* Prévia em bolha: é o que o cliente lê agora, com os dados desta conversa. */}
      <p className={css.rotuloPrevia}>O cliente recebe assim agora:</p>
      <p className={css.bolha}>{previa}</p>

      {editando && gravacao !== 'erro' && (
        <p className={css.editando}>
          <Pilula tom="aviso" fina>editando, não salvo</Pilula>
        </p>
      )}
      {gravacao === 'erro' && (
        <p className={css.alerta} role="alert">
          Não salvou. {avisoApi ?? 'Tente de novo.'} O texto continua aqui.
        </p>
      )}
      {gravacao === 'salvo' && !editando && (
        <output className={css.editando}><Pilula tom="ok" fina>Salvo</Pilula></output>
      )}

      <div className={css.acoes}>
        <Botao variante="primario" className={css.toque} disabled={!editando || gravacao === 'salvando'} onClick={salvar}>
          {gravacao === 'salvando' ? 'Salvando…' : 'Salvar texto'}
        </Botao>
        <Botao className={css.toque} disabled={!editando || gravacao === 'salvando'} onClick={() => { setRascunho(null); setGravacao('ocioso') }}>
          Desfazer
        </Botao>
        {/* Achado 1, P0 (banca 10): a tela que o dono já achou (trilho,
            rótulo por extenso) não deixava enviar; quem enviava de fato era
            outro ícone, escondido dentro da conversa. Mesma ação `enviar` de
            useAcoes() que features/respostas/ModalBiblioteca.jsx já usa.
            #1474: no modo API este envio não está ligado (ENVIOS_NAO_LIGADOS.automatica)
            e o modal fechava como se tivesse enviado; some até ligar. */}
        {!fonteApi && (
          <Botao
            icone="enviar"
            className={css.toque}
            disabled={!podeEnviar}
            title={!podeEnviar ? 'Abra uma conversa (com janela aberta para texto livre) para enviar.' : undefined}
            onClick={() => aoEnviar(regra, previa)}
          >
            {nomeConversa ? `Enviar para ${nomeConversa.split(' ')[0]}` : 'Enviar'}
          </Botao>
        )}
      </div>
    </li>
  )
}

// Uma linha por dia da semana (pedido do dono, 24/09/2026): fechado o dia
// todo tira os dois campos de hora, sem apagar o que estava neles (ela pode
// desmarcar e achar o horário de antes do jeito que deixou).
//
// #1474: a chave era "Fechado o dia todo" (desligada = aberto), dupla negação: com a
// semana toda aberta a tela parecia toda fechada. Agora é "Abre neste dia" (ligada =
// abre); o dado gravado continua `fechado`, só a leitura da chave inverteu.
//
// Achado 8 (banca capricho R10): as sete linhas competiam em pé de igualdade,
// sem nada que dissesse qual vale hoje. `hoje` já vem calculado do relógio da
// tela (ModalAutomacoes), esta função só decora a linha.
function DiaDeFuncionamento({ dia, valor, aoEditar, hoje }) {
  return (
    <li className={`${css.diaFuncionamento} ${hoje ? css.diaFuncionamentoHoje : ''}`}>
      <strong>{dia.rotulo}</strong>
      {hoje && <span className={css.marcaHoje}>Hoje</span>}
      <label className={css.chave}>
        <input
          type="checkbox"
          aria-label={`${dia.rotulo}, abre neste dia`}
          checked={!valor.fechado}
          onChange={(e) => aoEditar({
            fechado: !e.target.checked,
            ...(e.target.checked && !valor.abre ? { abre: '08:00', fecha: '18:00' } : {}),
          })}
        />
        Abre neste dia
      </label>
      {valor.fechado && <span className={css.diaFechado}>Fechado</span>}
      {!valor.fechado && (
        <span className={css.horariosDoDia}>
          <CampoTexto
            rotulo={`Abre, ${dia.rotulo}`}
            rotuloOculto
            tipo="time"
            value={valor.abre ?? ''}
            onChange={(e) => aoEditar({ abre: e.target.value })}
          />
          <CampoTexto
            rotulo={`Fecha, ${dia.rotulo}`}
            rotuloOculto
            tipo="time"
            value={valor.fecha ?? ''}
            onChange={(e) => aoEditar({ fecha: e.target.value })}
          />
        </span>
      )}
    </li>
  )
}

export function ModalAutomacoes({ regras, aoAlternar, aoFechar }) {
  const {
    selecionada, agora, som, audioBloqueado, funcionamento, aberta, lojaAberta, permissaoNotificacao,
    fonteApi, sincronizacao,
  } = useAtendimento()
  const {
    editarRegra, alternarSom, ouvirAmostraDeSom, editarFuncionamento, alternarLoja, pedirNotificacaoDoNavegador,
    enviar, voltarAoHorario,
  } = useAcoes()
  const { janelas, canais } = useCatalogo()

  const faixa = faixaDaJanela(janelas, selecionada?.pedido?.janela)
  // `abre` só a regra "Fora do horário" usa (VARIAVEIS, dominio/automacao.js);
  // as outras ignoram a chave que não citam no texto.
  const contexto = { ...contextoDePrevia(selecionada, faixa), abre: descreverProximaAbertura(agora, funcionamento) }
  // Achado 8: mesmo relógio de `agora` que já decide Aberta/Fechada aqui do
  // lado, só lido como dia da semana (0-6, igual Date#getDay) para achar a
  // chave em DIAS_DA_SEMANA.
  const chaveDeHoje = DIAS_DA_SEMANA[new Date(agora).getDay()].chave

  // Mesma conta de canal/janela do composer e da biblioteca (Respostas):
  // enviar daqui respeita a mesma regra de janela fechada, nunca um atalho
  // que fura o que o resto do app trava.
  const canal = selecionada ? canalDaConversa(canais, selecionada) : null
  const { pode: podeEnviar } = selecionada
    ? permissaoDeEscrita(selecionada, agora, canal)
    : { pode: false }

  function enviarRegra(regra, previa) {
    enviar(selecionada.id, previa, { automatica: true, regra: regra.id })
    aoFechar()
  }

  return (
    <Modal
      titulo="Mensagens automáticas"
      descricao="O que a casa fala sozinha, em nome dela."
      aoFechar={aoFechar}
      rodape={<Botao className={css.toque} onClick={aoFechar}>Fechar</Botao>}
    >
      {/* Horário de funcionamento e estado da loja (pedido do dono,
          24/09/2026): o controle do topo já mostra Aberta/Fechada sempre;
          aqui é onde ela configura o horário por dia e força a exceção do
          dia, sem precisar abrir outra tela. */}
      <div className={css.funcionamento}>
        <div className={css.topo}>
          <strong>Horário de funcionamento</strong>
          <Pilula tom={aberta ? 'ok' : 'perigo'}>{aberta ? 'Aberta agora' : 'Fechada agora'}</Pilula>
        </div>
        <p className={css.descricao}>
          {lojaAberta == null
            ? 'Segue o horário configurado abaixo.'
            : `Forçada ${lojaAberta ? 'aberta' : 'fechada'} na mão, por cima do horário.`}
        </p>
        <div className={css.acoesLoja}>
          <Botao className={css.toque} onClick={() => alternarLoja(agora)}>
            {aberta ? 'Fechar loja agora' : 'Abrir loja agora'}
          </Botao>
          {/* #1474: o mesmo "Voltar a seguir o horário" de Horários e mensagens (só no modo API). */}
          {voltarAoHorario && lojaAberta != null && (
            <Botao className={css.toque} onClick={voltarAoHorario}>Voltar a seguir o horário</Botao>
          )}
        </div>
        <ul className={css.diasFuncionamento}>
          {DIAS_DA_SEMANA.map((dia) => (
            <DiaDeFuncionamento
              key={dia.chave}
              dia={dia}
              valor={funcionamento[dia.chave]}
              aoEditar={(campos) => editarFuncionamento(dia.chave, campos)}
              hoje={dia.chave === chaveDeHoje}
            />
          ))}
        </ul>
      </div>

      {/* Som da cozinha: liga e desliga aqui, porque é aqui que mora o que a
          casa faz sozinha. Sons sintetizados em infra/som.js, sem arquivo. */}
      <div className={css.som}>
        <label className={css.chave}>
          <input
            type="checkbox"
            aria-label={`Som da cozinha, ${som ? 'ligado' : 'desligado'}`}
            checked={som}
            onChange={(e) => alternarSom(e.target.checked)}
          />
          Som da cozinha {som ? 'ligado' : 'desligado'}
        </label>
        <p className={css.descricao}>
          Avisa em {AVISOS_DE_SOM}.
        </p>
        {som && audioBloqueado && (
          <output className={css.avisoAudio}>
            Toque em qualquer lugar da tela para ligar o som.
          </output>
        )}
        <div className={css.amostras}>
          {listaDeEventosDeSom().map((evento) => (
            <Botao
              key={evento}
              variante="texto"
              icone="play"
              className={css.amostra}
              onClick={() => ouvirAmostraDeSom(evento)}
            >
              Ouvir: {ROTULO_DO_SOM[evento]}
            </Botao>
          ))}
        </div>
      </div>

      {/* Notificação do navegador (rodada 10, item 6): canal fora da aba
          para os motivos de "Precisa de você" (US-013/UC-02, "todos os
          dispositivos logados"; pedido literal do dono, "não tem
          notificações de sistema"). Permissão só por este clique dela,
          nunca pedida sozinha. */}
      <div className={css.som}>
        <div className={css.topo}>
          <strong>Notificação do navegador</strong>
          <Pilula tom={permissaoNotificacao === 'granted' ? 'ok' : 'neutro'}>
            {permissaoNotificacao === 'granted' && 'Ativada'}
            {permissaoNotificacao === 'denied' && 'Bloqueada'}
            {permissaoNotificacao === 'default' && 'Desativada'}
            {permissaoNotificacao === 'indisponivel' && 'Sem suporte'}
          </Pilula>
        </div>
        <p className={css.descricao}>
          Avisa "Precisa de você" mesmo com a aba minimizada ou outra janela em foco.
        </p>
        {permissaoNotificacao === 'default' && (
          <Botao className={css.toque} icone="globo" onClick={pedirNotificacaoDoNavegador}>
            Ativar notificação do navegador
          </Botao>
        )}
        {permissaoNotificacao === 'denied' && (
          <p className={css.descricao}>Bloqueada nas configurações do navegador.</p>
        )}
      </div>

      <ul className={css.lista}>
        {regras.map((regra) => (
          <Regra
            key={regra.id}
            regra={regra}
            contexto={contexto}
            aoAlternar={aoAlternar}
            aoEditar={editarRegra}
            nomeConversa={selecionada?.nome}
            podeEnviar={podeEnviar}
            aoEnviar={enviarRegra}
            fonteApi={fonteApi}
            avisoApi={sincronizacao?.aviso ?? null}
          />
        ))}
      </ul>
    </Modal>
  )
}
