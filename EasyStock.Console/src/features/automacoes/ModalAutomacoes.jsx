import { useId } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { automacoesEditaveis } from '../../dominio/acessoModulos'
import { analisarAutomatica, bloqueioDaAutomatica, contextoDePrevia } from '../../dominio/automacao'
import { NOME_RESPOSTAS_AUTOMATICAS } from '../../dominio/modulos'
import { ROTULO_DO_SOM, listaDeEventosDeSom } from '../../dominio/automatico'
import { canalDaConversa } from '../../dominio/canal'
import { DIAS_DA_SEMANA, chaveDoDiaNaLoja, descreverProximaAbertura } from '../../dominio/funcionamento'
import { faixaDaJanela } from '../../dominio/entrega'
import { permissaoDeEscrita } from '../../dominio/janela'
import { EditorDeAutomatica } from '../../componentes/EditorDeAutomatica'
import css from './automacoes.module.css'

const AVISOS_DE_SOM = listaDeEventosDeSom().map((e) => ROTULO_DO_SOM[e]).join(', ')

// #1474, modo API: o EasyStok só preenche {nome}, {pedido} e {faixa}; a prévia não inventa o
// resto ({abre}, {linkCardapio} saem escritos assim para o cliente) e a tela avisa.
// #2734 (T7): a prévia usa a troca do servidor (`previaDoServidor`): o que ele deixaria escrito aparece marcado.
const EXEMPLOS_DA_API = { nome: 'Maria', pedido: '12', faixa: 'amanhã às 14:00' }
const valoresDaPreviaApi = (conversa, faixa) => ({
  nome: conversa?.nome ? conversa.nome.split(' ')[0] : EXEMPLOS_DA_API.nome,
  pedido: conversa?.pedido?.numero ? String(conversa.pedido.numero) : EXEMPLOS_DA_API.pedido,
  faixa: faixa ?? EXEMPLOS_DA_API.faixa,
})

function Regra({
  regra, contexto, valoresApi, aoAlternar, aoEditar, nomeConversa, podeEnviar, aoEnviar, fonteApi, avisoApi, editavel,
}) {
  const idBloqueio = useId()
  const naoLiga = !regra.ativa && fonteApi && Boolean(bloqueioDaAutomatica(regra.texto))

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
            disabled={!editavel || naoLiga}
            aria-describedby={naoLiga ? idBloqueio : undefined}
            onChange={() => aoAlternar(regra.id)}
          />
          {regra.ativa ? 'ligada' : 'desligada'}
        </label>
      </div>
      <p className={css.descricao}>{regra.descricao}</p>

      {/* #2783: o mesmo editor de Respostas (texto, variáveis, prévia, erro do servidor). */}
      <EditorDeAutomatica
        regra={regra} editavel={editavel} avisoApi={avisoApi} idBloqueio={idBloqueio}
        analisar={(texto) => analisarAutomatica({ regra, texto, fonteApi, editavel, valores: valoresApi, contexto })}
        aoSalvar={(texto) => aoEditar(regra.id, { texto })}
      >
        {({ previa }) => (!fonteApi && (
          /* Achado 1, P0 (banca 10): a tela que o dono já achou (trilho,
            rótulo por extenso) não deixava enviar; quem enviava de fato era
            outro ícone, escondido dentro da conversa. Mesma ação `enviar` de
            useAcoes() que features/respostas/ModalBiblioteca.jsx já usa.
            #1474: no modo API este envio não está ligado (ENVIOS_NAO_LIGADOS.automatica)
            e o modal fechava como se tivesse enviado; some até ligar. */
          <Botao
            icone="enviar"
            className={css.toque}
            disabled={!podeEnviar}
            title={!podeEnviar ? 'Selecione uma conversa que permita enviar texto.' : undefined}
            onClick={() => aoEnviar(regra, previa)}
          >
            {nomeConversa ? `Enviar para ${nomeConversa.split(' ')[0]}` : 'Enviar'}
          </Botao>
        ))}
      </EditorDeAutomatica>
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
export function DiaDeFuncionamento({ dia, valor, aoEditar, hoje, obrigatorio = false }) {
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
            required={obrigatorio}
            value={valor.abre ?? ''}
            onChange={(e) => aoEditar({ abre: e.target.value })}
          />
          <CampoTexto
            rotulo={`Fecha, ${dia.rotulo}`}
            rotuloOculto
            tipo="time"
            required={obrigatorio}
            value={valor.fecha ?? ''}
            onChange={(e) => aoEditar({ fecha: e.target.value })}
          />
        </span>
      )}
    </li>
  )
}

export function ModalAutomacoes({ regras, aoAlternar, aoFechar, horariosApi }) {
  const {
    selecionada, agora, som, audioBloqueado, funcionamento, aberta, lojaAberta, permissaoNotificacao,
    fonteApi, sincronizacao,
  } = useAtendimento()
  const {
    editarRegra, alternarSom, ouvirAmostraDeSom, editarFuncionamento, alternarLoja, pedirNotificacaoDoNavegador,
    enviar, voltarAoHorario,
  } = useAcoes()
  const { janelas, canais } = useCatalogo()
  const { acoes } = useAcessoModulos()
  // #1966: `PUT automacoes/{gatilho}` exige Gerente; o Operador vê as regras sem ligar, desligar ou editar.
  const editavel = automacoesEditaveis({ fonteApi, acoes })
  // Abrir e fechar a loja na mão é da gerência (`controlarLoja`), como em Horários e mensagens.
  const podeControlarLoja = !fonteApi || acoes.controlarLoja === true

  const faixa = faixaDaJanela(janelas, selecionada?.pedido?.janela)
  // `abre` só a regra "Fora do horário" usa (VARIAVEIS, dominio/automacao.js);
  // as outras ignoram a chave que não citam no texto.
  const contexto = { ...contextoDePrevia(selecionada, faixa), abre: descreverProximaAbertura(agora, funcionamento) }
  const valoresApi = valoresDaPreviaApi(selecionada, selecionada?.pedido ? faixa : null)
  // Achado 8: mesmo relógio de `agora` que já decide Aberta/Fechada aqui do
  // lado, lido como dia civil da loja e não do navegador (#2742).
  const chaveDeHoje = chaveDoDiaNaLoja(agora)

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
      titulo={NOME_RESPOSTAS_AUTOMATICAS}
      descricao="Mensagens, horários e avisos da loja."
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
            ? 'Segue os horários da semana.'
            : `${lojaAberta ? 'Aberta' : 'Fechada'} manualmente, sem seguir os horários da semana.`}
        </p>
        {podeControlarLoja && (
          <div className={css.acoesLoja}>
            <Botao className={css.toque} onClick={() => alternarLoja(agora)}>
              {aberta ? 'Fechar loja agora' : 'Abrir loja agora'}
            </Botao>
            {/* #1474: o mesmo "Voltar a seguir o horário" de Horários e mensagens (só no modo API). */}
            {voltarAoHorario && lojaAberta != null && (
              <Botao className={css.toque} onClick={voltarAoHorario}>Voltar a seguir o horário</Botao>
            )}
          </div>
        )}
        {fonteApi ? horariosApi : <ul className={css.diasFuncionamento}>
          {DIAS_DA_SEMANA.map((dia) => (
            <DiaDeFuncionamento
              key={dia.chave}
              dia={dia}
              valor={funcionamento[dia.chave]}
              aoEditar={(campos) => editarFuncionamento(dia.chave, campos)}
              hoje={dia.chave === chaveDeHoje}
            />
          ))}
        </ul>}
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
          Avisa sobre {AVISOS_DE_SOM}.
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
          Avisa sobre conversas em "Precisa de você", mesmo fora desta aba.
        </p>
        {permissaoNotificacao === 'default' && (
          <Botao className={css.toque} icone="globo" onClick={pedirNotificacaoDoNavegador}>
            Ativar notificação do navegador
          </Botao>
        )}
        {permissaoNotificacao === 'denied' && (
          <p className={css.descricao}>Para ativar, permita notificações nas configurações do navegador.</p>
        )}
      </div>

      {!editavel && <p className={css.descricao}>Somente proprietário ou gerente pode alterar as respostas automáticas.</p>}
      <ul className={css.lista}>
        {regras.map((regra) => (
          <Regra
            key={regra.id}
            regra={regra}
            contexto={contexto}
            valoresApi={valoresApi}
            aoAlternar={aoAlternar}
            aoEditar={editarRegra}
            nomeConversa={selecionada?.nome}
            podeEnviar={podeEnviar}
            aoEnviar={enviarRegra}
            fonteApi={fonteApi}
            avisoApi={sincronizacao?.aviso ?? null}
            editavel={editavel}
          />
        ))}
      </ul>
    </Modal>
  )
}
