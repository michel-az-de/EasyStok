import { Botao } from './Botao'
import { CampoSelecao, CampoTexto } from './Campo'

export function EscolhaJanelaPedido({ data, mudarData, leitura, escolhida, escolher,
  avisar, mudarAviso, erro, aviso, enviando, salvar, recarregar }) {
  return (
    <div>
      {leitura?.atual && <p>Agendamento atual: <strong>{leitura.atual.rotulo}</strong></p>}
      {leitura && !leitura.atual && <p>Este pedido ainda não tem vaga de entrega.</p>}
      <CampoTexto rotulo="Dia da nova entrega (opcional)" tipo="date" value={data}
        disabled={enviando} onChange={(e) => mudarData(e.target.value)} />
      {!leitura && !erro && <p role="status">Consultando as vagas…</p>}
      {leitura && !leitura.lojaDisponivel && <p>A empresa não tem vitrine ativa para entrega.</p>}
      {leitura?.janelas.length === 0 && <p>Nenhuma vaga no prazo de preparo. Escolha outro dia.</p>}
      {leitura?.janelas.length > 0 && (
        <CampoSelecao rotulo="Nova janela de entrega" value={escolhida} disabled={enviando}
          onChange={(e) => escolher(e.target.value)} opcoes={[
            { valor: '', rotulo: 'Escolha uma janela' },
            ...leitura.janelas.filter((j) => j.id !== leitura.atual?.id)
              .map((j) => ({ valor: j.id, rotulo: `${j.rotulo} · ${j.vagas} ${j.vagas === 1 ? 'vaga livre' : 'vagas livres'}` })),
          ]} />
      )}
      <p><label><input type="checkbox" checked={avisar} disabled={enviando}
        onChange={(e) => mudarAviso(e.target.checked)} /> Solicitar aviso por WhatsApp</label></p>
      {avisar && <p>O envio depende do canal e das preferências do cliente. Fora das 24 horas, exige modelo aprovado.</p>}
      <Botao variante="primario" disabled={enviando || !leitura || !escolhida || escolhida === leitura.atual?.id} onClick={salvar}>
        {enviando ? 'Alterando…' : 'Confirmar agendamento'}
      </Botao>
      <Botao variante="texto" disabled={enviando} onClick={recarregar}>Atualizar vagas</Botao>
      {erro && <p role="alert">{erro}</p>}
      {aviso && <p role="status">{aviso}</p>}
    </div>
  )
}
