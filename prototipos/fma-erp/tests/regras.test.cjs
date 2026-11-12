// Teste das regras do protótipo FMA Gestão sem navegador: node tests/regras.test.cjs
"use strict";
const path = require("path");
const assert = require("assert");

require(path.join(__dirname, "..", "dominio.js"));
const FMA = globalThis.FMA;
FMA.carregar();
const { Regras: R, lerValor, estado } = FMA;
const st = estado();
const por = (arr, id) => arr.find(x => x.id === id);
const pend = chave => st.lembretes.some(m => m.chave === chave && m.status === "Pendente");
let ok = 0;
function t(nome, fn) { fn(); ok++; console.log("ok - " + nome); }

t("lerValor aceita formatos BR e recusa ambíguo", () => {
  assert.strictEqual(lerValor("1.500,50"), 1500.5);
  assert.strictEqual(lerValor("1500,50"), 1500.5);
  assert.strictEqual(lerValor("1500.50"), 1500.5);
  assert.strictEqual(lerValor("1.500"), 1500);
  assert.ok(Number.isNaN(lerValor("1,5,0")));
  assert.ok(Number.isNaN(lerValor("abc")));
});

t("fluxo completo lead → contrato → NFS-e com retenção → recebimento → encerrado", () => {
  const l = por(st.leads, "L2");
  R.criarProposta(l);
  const p = st.propostas[st.propostas.length - 1];
  assert.strictEqual(R.enviarProposta(p), "Inclua ao menos uma etapa antes de enviar.");
  p.etapas.push({ escopo: "Etapa única", valor: 3000 });
  p.custos.push({ descricao: "Licença", valor: 200 });
  R.enviarProposta(p);
  assert.ok(pend("followup:" + p.id));
  R.aceitarProposta(p);
  assert.ok(!pend("followup:" + p.id), "follow-up fecha no aceite");
  const c = st.contratos[st.contratos.length - 1];
  assert.strictEqual(c.clienteId, null, "cliente só nasce na assinatura");
  const pagarAntes = st.pagar.length;
  R.assinar(c);
  assert.ok(c.clienteId && l.status === "Ganho");
  assert.strictEqual(st.pagar.length, pagarAntes + 1, "custo previsto vira conta a pagar");
  const e = c.etapas[0];
  R.iniciar(c, e); R.entregar(c, e); R.pedirAjuste(c, e);
  assert.strictEqual(e.status, "EmExecucao");
  R.entregar(c, e); R.aprovar(c, e);
  const r = por(st.receber, e.receberId);
  assert.ok(pend("nfse:" + e.id) && pend("vencimento:" + r.id));
  const base = { numero: "900", valor: 3000, retencoes: 0, competencia: "2026-11", xml: "a.xml", pdf: "a.pdf" };
  assert.strictEqual(R.anexarNota(c, e, { ...base, valor: 2999 }).erro, "valor");
  assert.strictEqual(R.anexarNota(c, e, { ...base, pdf: undefined }).erro, "arquivo");
  assert.strictEqual(R.anexarNota(c, e, { ...base, retencoes: 3000 }).erro, "retencoes");
  R.anexarNota(c, e, { ...base, retencoes: 165 });
  assert.strictEqual(r.valor, 2835, "líquido = bruto - retenções");
  assert.ok(!pend("nfse:" + e.id) && pend("imposto:2026-11"));
  const n = por(st.notas, e.notaId);
  R.cancelarNota(n);
  assert.strictEqual(e.status, "Aprovada"); assert.strictEqual(r.valor, 3000); assert.ok(pend("nfse:" + e.id));
  R.anexarNota(c, e, { ...base, numero: "901" });
  assert.strictEqual(por(st.notas, e.notaId).substitui, "900");
  R.receber(r);
  assert.strictEqual(r.valorPago, 3000); assert.strictEqual(e.status, "Paga"); assert.strictEqual(c.status, "Encerrado");
  assert.ok(!pend("vencimento:" + r.id));
});

t("nova versão substitui a enviada e copia etapas", () => {
  const p1 = por(st.propostas, "P1");
  R.novaVersao(p1);
  const v2 = st.propostas[st.propostas.length - 1];
  assert.strictEqual(p1.status, "Substituida"); assert.strictEqual(v2.versao, 2); assert.strictEqual(v2.etapas.length, 3);
  assert.ok(!pend("followup:P1"));
});

t("cancelar contrato antes da assinatura devolve lead a qualificado", () => {
  const l = R.criarLead({ empresa: "Teste" }); R.qualificarLead(l); R.criarProposta(l);
  const p = st.propostas[st.propostas.length - 1]; p.etapas.push({ escopo: "x", valor: 100 });
  R.enviarProposta(p); R.aceitarProposta(p);
  const c = st.contratos[st.contratos.length - 1];
  R.cancelarContrato(c);
  assert.strictEqual(c.status, "Cancelado"); assert.strictEqual(l.status, "Qualificado"); assert.ok(!pend("assinatura:" + c.id));
});

t("conversa vira lead e proposta com etapas da IA; segunda vez reaproveita", () => {
  const conv = por(st.conversas, "W1");
  R.propostaDaConversa(conv);
  const l = por(st.leads, conv.leadId);
  const p = st.propostas[st.propostas.length - 1];
  assert.strictEqual(l.empresa, "Gama Varejo"); assert.strictEqual(l.status, "Proposta");
  assert.strictEqual(p.status, "Rascunho"); assert.strictEqual(p.etapas.length, 3); assert.ok(p.ia);
  const n = st.propostas.length;
  assert.ok(/Já existe/.test(R.propostaDaConversa(conv))); assert.strictEqual(st.propostas.length, n);
});

t("post: ideia → rascunho da IA → aprovado → agendado → publicado", () => {
  const p = R.criarPost({ titulo: "Teste", formato: "video", redes: ["linkedin"] });
  assert.strictEqual(R.aprovarPost(p), undefined, "ideia não aprova direto");
  R.rascunharPost(p);
  assert.strictEqual(p.status, "Rascunho"); assert.ok(p.texto.length > 0);
  R.salvarTexto(p, "  ");
  assert.strictEqual(R.aprovarPost(p), "Escreva o texto antes de aprovar.");
  R.salvarTexto(p, "Texto final"); R.aprovarPost(p);
  assert.strictEqual(R.agendarPost(p, { redes: [], quando: FMA.util.dias(1) }).erro, "redes");
  assert.strictEqual(R.agendarPost(p, { redes: ["linkedin"], quando: FMA.util.dias(-1) }).erro, "quando");
  R.agendarPost(p, { redes: ["linkedin", "instagram"], quando: FMA.util.dias(2) });
  assert.strictEqual(p.status, "Agendado");
  R.publicarAgora(p);
  assert.strictEqual(p.status, "Publicado"); assert.ok(p.metricas);
});

t("Instagram recusa post só de texto", () => {
  const p = R.criarPost({ titulo: "Só texto", formato: "post" });
  R.rascunharPost(p); R.aprovarPost(p);
  const r = R.agendarPost(p, { redes: ["instagram"], quando: FMA.util.dias(1) });
  assert.strictEqual(r.erro, "redes"); assert.strictEqual(p.status, "Aprovado");
});

t("comentário do post vira lead com a origem do post; ignorada não vira", () => {
  const i = por(st.interacoes, "I1");
  R.virarLead(i);
  const l = por(st.leads, i.leadId);
  assert.strictEqual(i.status, "Lead"); assert.ok(/Pagamento por etapa/.test(l.origem));
  assert.strictEqual(FMA.Consultas.leadsGerados("PO1"), 1);
  const i3 = por(st.interacoes, "I3"); R.ignorar(i3);
  assert.strictEqual(R.virarLead(i3), undefined);
});

t("abordagem manual respeita o limite de 5 por dia", () => {
  const l = por(st.leads, "L2");
  assert.strictEqual(R.abordar(l, " ").erro, "texto");
  while (FMA.Consultas.abordagensHoje() < 5) assert.ok(typeof R.abordar(l, "oi") === "string");
  const r = R.abordar(l, "sexta");
  assert.strictEqual(r.erro, "limite"); assert.strictEqual(FMA.Consultas.abordagensHoje(), 5);
});

t("hoje traz no máximo 5 itens, urgentes primeiro", () => {
  const h = FMA.Consultas.hoje();
  assert.ok(h.length > 0 && h.length <= 5); assert.strictEqual(h[0].urgencia, "alta");
});

console.log(ok + " testes passaram");
