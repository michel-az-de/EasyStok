/* FMA Gestão: protótipo estático do ERP interno da FMA Software (issue #2387).
   Dados fictícios em memória, persistidos só no navegador.
   Spec: docs/plan/fma-erp/00-spec-dominio.md. Banca: docs/plan/fma-erp/01-banca.md */
(function () {
  "use strict";

  var CHAVE = "fma-gestao-prototipo-v2";
  var HOJE = new Date();
  HOJE.setHours(0, 0, 0, 0);

  // ---------- utilidades ----------
  function dias(n) { var d = new Date(HOJE); d.setDate(d.getDate() + n); return iso(d); }
  function iso(d) { return d.getFullYear() + "-" + String(d.getMonth() + 1).padStart(2, "0") + "-" + String(d.getDate()).padStart(2, "0"); }
  function esc(s) { return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]; }); }
  function brl(v) { return (v || 0).toLocaleString("pt-BR", { style: "currency", currency: "BRL" }); }
  function data(s) { if (!s) return "sem data"; var p = s.split("-"); return p[2] + "/" + p[1] + "/" + p[0]; }
  function mes(s) { var p = s.split("-"); return p[1] + "/" + p[0]; }
  function diasAte(s) { return Math.round((new Date(s + "T00:00:00") - HOJE) / 86400000); }
  function prazo(s) {
    var n = diasAte(s);
    if (n < 0) return '<span class="selo erro">vencida há ' + (-n) + " d</span>";
    if (n === 0) return '<span class="selo alerta">hoje</span>';
    if (n <= 3) return '<span class="selo alerta">em ' + n + " d</span>";
    return '<span class="selo">em ' + n + " d</span>";
  }
  function soma(arr, f) { return arr.reduce(function (t, x) { return t + f(x); }, 0); }
  function por(arr, id) { for (var i = 0; i < arr.length; i++) if (arr[i].id === id) return arr[i]; return null; }
  function r2(v) { return Math.round(v * 100) / 100; }
  // Aceita "1.500,50", "1500,50", "1500.50" e "1500". Ambíguo ou inválido devolve NaN.
  function lerValor(txt) {
    var s = String(txt || "").replace(/[R$\s]/g, "");
    if (!s) return NaN;
    if (s.indexOf(",") >= 0) { if (!/^\d{1,3}(\.\d{3})*,\d{1,2}$|^\d+,\d{1,2}$/.test(s)) return NaN; s = s.replace(/\./g, "").replace(",", "."); }
    else if (/^\d+\.\d{1,2}$/.test(s)) { /* ponto decimal */ }
    else if (/^\d{1,3}(\.\d{3})+$/.test(s)) s = s.replace(/\./g, "");
    else if (!/^\d+$/.test(s)) return NaN;
    var v = Number(s); return isFinite(v) ? r2(v) : NaN;
  }
  function valorEmCampo(v) { return v.toFixed(2).replace(".", ","); }

  var CLASSE_STATUS = {
    Novo: "", Qualificado: "alerta", Proposta: "ativo", Ganho: "ok", Perdido: "erro",
    Rascunho: "", Enviada: "alerta", Aceita: "ok", Recusada: "erro", Expirada: "erro", Substituida: "",
    AguardandoAssinatura: "alerta", Vigente: "ativo", Encerrado: "ok", Cancelado: "erro",
    Planejada: "", EmExecucao: "ativo", Entregue: "alerta", Aprovada: "alerta", Faturada: "ok", Paga: "ok",
    Aberta: "alerta", Pendente: "alerta", Feito: "ok", Anexada: "ok", Cancelada: "erro", Ativo: "ok"
  };
  var ROTULO = { AguardandoAssinatura: "Aguardando assinatura", EmExecucao: "Em execução", Substituida: "Substituída" };
  function selo(s) { return '<span class="selo ' + (CLASSE_STATUS[s] || "") + '">' + esc(ROTULO[s] || s) + "</span>"; }

  // ---------- dados fictícios ----------
  function semente() {
    return {
      seq: 100,
      leads: [
        { id: "L1", empresa: "Alfa Logística", contato: "Contato A", origem: "Indicação", interesse: "Painel de rastreio de entregas", status: "Proposta", criadoEm: dias(-20) },
        { id: "L2", empresa: "Beta Clínica", contato: "Contato B", origem: "LinkedIn", interesse: "Agenda online integrada", status: "Qualificado", criadoEm: dias(-9) },
        { id: "L3", empresa: "Gama Varejo", contato: "Contato C", origem: "Site", interesse: "Integração de pedidos", status: "Novo", criadoEm: dias(-2) },
        { id: "L4", empresa: "Delta Educação", contato: "Contato D", origem: "Evento", interesse: "Portal do aluno", status: "Ganho", criadoEm: dias(-60) },
        { id: "L5", empresa: "Épsilon Serviços", contato: "Contato E", origem: "Site", interesse: "App de campo", status: "Perdido", criadoEm: dias(-45) }
      ],
      propostas: [
        { id: "P1", leadId: "L1", versao: 1, status: "Enviada", validade: dias(10), enviadaEm: dias(-4),
          etapas: [
            { escopo: "Descoberta e protótipo navegável", valor: 1000 },
            { escopo: "Etapa 1: rastreio e mapa", valor: 2000 },
            { escopo: "Etapa 2: painel e relatórios", valor: 2000 }
          ],
          custos: [{ descricao: "Licença de mapas", valor: 300 }] },
        { id: "P2", leadId: "L4", versao: 2, status: "Aceita", validade: dias(-40), enviadaEm: dias(-50),
          etapas: [
            { escopo: "Descoberta e protótipo navegável", valor: 1000 },
            { escopo: "Etapa 1: matrícula e login", valor: 2000 },
            { escopo: "Etapa 2: notas e frequência", valor: 2000 }
          ],
          custos: [] }
      ],
      clientes: [
        { id: "K1", nome: "Delta Educação", tipo: "PJ", documento: "CNPJ fictício", leadId: "L4", status: "Ativo", desde: dias(-37) }
      ],
      contratos: [
        { id: "C1", propostaId: "P2", leadId: "L4", clienteId: "K1", status: "Vigente", assinadoEm: dias(-37), anexo: "contrato-delta-assinado.pdf",
          etapas: [
            { id: "E1", escopo: "Descoberta e protótipo navegável", valor: 1000, status: "Paga", aprovadaEm: dias(-20), receberId: "R1", notaId: "N1" },
            { id: "E2", escopo: "Etapa 1: matrícula e login", valor: 2000, status: "Aprovada", aprovadaEm: dias(-3), receberId: "R2", notaId: null },
            { id: "E3", escopo: "Etapa 2: notas e frequência", valor: 2000, status: "EmExecucao", aprovadaEm: null, receberId: null, notaId: null }
          ],
          eventos: [
            { em: dias(-38), texto: "Contrato criado a partir da proposta P2 v2 aceita" },
            { em: dias(-37), texto: "Assinatura registrada (contrato-delta-assinado.pdf); cliente Delta Educação ativo" },
            { em: dias(-20), texto: "Etapa E1 aprovada pelo cliente; parcela R1 emitida" },
            { em: dias(-18), texto: "NFS-e 101 anexada à etapa E1" },
            { em: dias(-12), texto: "Recebimento da parcela R1 registrado" },
            { em: dias(-3), texto: "Etapa E2 aprovada pelo cliente; parcela R2 emitida" }
          ] }
      ],
      receber: [
        { id: "R1", descricao: "Delta: Descoberta e protótipo", clienteId: "K1", contratoId: "C1", etapaId: "E1", bruto: 1000, retencoes: 0, valor: 1000, valorPago: 1000, vencimento: dias(-12), status: "Paga" },
        { id: "R2", descricao: "Delta: Etapa 1", clienteId: "K1", contratoId: "C1", etapaId: "E2", bruto: 2000, retencoes: 0, valor: 2000, valorPago: null, vencimento: dias(12), status: "Aberta" }
      ],
      pagar: [
        { id: "G1", descricao: "Contabilidade mensal", contratoId: null, valor: 400, vencimento: dias(5), status: "Aberta", categoria: "Administrativo" },
        { id: "G2", descricao: "Hospedagem de homologação", contratoId: "C1", valor: 150, vencimento: dias(2), status: "Aberta", categoria: "Custo de projeto" },
        { id: "G3", descricao: "Imposto do mês", contratoId: null, valor: 250, vencimento: dias(-1), status: "Aberta", categoria: "Tributos" }
      ],
      notas: [
        { id: "N1", numero: "101", contratoId: "C1", etapaId: "E1", receberId: "R1", valor: 1000, retencoes: 0, competencia: dias(-20).slice(0, 7), xml: "nfse-101.xml", pdf: "nfse-101.pdf", status: "Anexada", substitui: null }
      ],
      lembretes: [
        { id: "M1", texto: "Follow-up da proposta P1 (Alfa Logística)", alvo: "#/propostas/P1", chave: "followup:P1", quando: dias(0), status: "Pendente" },
        { id: "M2", texto: "Emitir e anexar NFS-e da etapa E2 (Delta Educação)", alvo: "#/contratos/C1", chave: "nfse:E2", quando: dias(0), status: "Pendente" },
        { id: "M3", texto: "Parcela R2 vence em 3 dias: confirmar com o cliente", alvo: "#/receber", chave: "vencimento:R2", quando: dias(9), status: "Pendente" }
      ]
    };
  }

  var st;
  function carregar() {
    try { var bruto = localStorage.getItem(CHAVE); if (bruto) { st = JSON.parse(bruto); return; } } catch (e) { /* sem storage */ }
    st = semente();
  }
  function salvar() { try { localStorage.setItem(CHAVE, JSON.stringify(st)); } catch (e) { /* sem storage */ } }
  function novoId(prefixo) { st.seq += 1; return prefixo + st.seq; }
  function avisar(msg) {
    var t = document.getElementById("toast");
    t.textContent = msg; t.classList.add("visivel");
    clearTimeout(avisar._t); avisar._t = setTimeout(function () { t.classList.remove("visivel"); }, 4200);
  }
  // Lembretes têm chave estável: o fluxo os abre e os fecha pela chave, sem depender do texto.
  function lembrar(chave, texto, alvo, quando) {
    var ja = st.lembretes.filter(function (m) { return m.chave === chave && m.status === "Pendente"; })[0];
    if (ja) return;
    st.lembretes.push({ id: novoId("M"), chave: chave, texto: texto, alvo: alvo, quando: quando, status: "Pendente" });
  }
  function fechar(chave) { st.lembretes.forEach(function (m) { if (m.chave === chave && m.status === "Pendente") m.status = "Feito"; }); }
  function evento(contrato, texto) { contrato.eventos.push({ em: dias(0), texto: texto }); }
  function totalProposta(p) { return soma(p.etapas, function (e) { return e.valor; }); }
  function leadDe(p) { return por(st.leads, p.leadId) || { empresa: "?" }; }
  // Antes da assinatura o contrato ainda não tem cliente: mostra o nome do lead.
  function clienteDe(c) { return por(st.clientes, c.clienteId) || { nome: (por(st.leads, c.leadId) || { empresa: "?" }).empresa }; }

  // ---------- regras do domínio (spec seção 3) ----------
  var Regras = {
    criarLead: function (dados) {
      st.leads.push({ id: novoId("L"), empresa: dados.empresa, contato: dados.contato || "não informado", origem: dados.origem || "Outro", interesse: dados.interesse || "a qualificar", status: "Novo", criadoEm: dias(0) });
      return "Lead " + dados.empresa + " criado.";
    },
    qualificarLead: function (l) { if (l.status !== "Novo") return; l.status = "Qualificado"; return "Lead qualificado."; },
    perderLead: function (l) { if (l.status === "Ganho" || l.status === "Perdido") return; l.status = "Perdido"; return "Lead marcado como perdido. Dá para reabrir."; },
    reabrirLead: function (l) { if (l.status !== "Perdido") return; l.status = "Qualificado"; return "Lead reaberto como qualificado."; },
    criarProposta: function (l) {
      if (l.status !== "Qualificado") return;
      var p = { id: novoId("P"), leadId: l.id, versao: 1, status: "Rascunho", validade: dias(15), enviadaEm: null, etapas: [], custos: [] };
      st.propostas.push(p); l.status = "Proposta";
      location.hash = "#/propostas/" + p.id;
      return "Proposta " + p.id + " criada em rascunho. Inclua as etapas.";
    },
    removerEtapa: function (p, i) { if (p.status !== "Rascunho") return; p.etapas.splice(i, 1); return "Etapa removida."; },
    removerCusto: function (p, i) { if (p.status !== "Rascunho") return; p.custos.splice(i, 1); return "Custo removido."; },
    enviarProposta: function (p) {
      if (p.status !== "Rascunho") return;
      if (!p.etapas.length) return "Inclua ao menos uma etapa antes de enviar.";
      p.status = "Enviada"; p.enviadaEm = dias(0);
      lembrar("followup:" + p.id, "Follow-up da proposta " + p.id + " (" + leadDe(p).empresa + ")", "#/propostas/" + p.id, dias(3));
      return "Proposta enviada. Lembrete de follow-up criado para daqui a 3 dias.";
    },
    recusarProposta: function (p) {
      if (p.status !== "Enviada") return;
      p.status = "Recusada"; leadDe(p).status = "Perdido"; fechar("followup:" + p.id);
      return "Proposta recusada; lead marcado como perdido.";
    },
    // Invariante 1: proposta enviada não muda; ajuste gera versão nova.
    novaVersao: function (p) {
      if (["Enviada", "Expirada", "Recusada"].indexOf(p.status) < 0) return;
      var n = { id: novoId("P"), leadId: p.leadId, versao: p.versao + 1, status: "Rascunho", validade: dias(15), enviadaEm: null,
        etapas: p.etapas.map(function (e) { return { escopo: e.escopo, valor: e.valor }; }),
        custos: p.custos.map(function (k) { return { descricao: k.descricao, valor: k.valor }; }) };
      if (p.status === "Enviada") p.status = "Substituida";
      fechar("followup:" + p.id);
      st.propostas.push(n); leadDe(p).status = "Proposta";
      location.hash = "#/propostas/" + n.id;
      return "Versão " + n.versao + " criada em rascunho a partir da " + p.id + ".";
    },
    // Invariante 2: contrato só nasce de proposta aceita.
    aceitarProposta: function (p) {
      if (p.status !== "Enviada") return;
      if (diasAte(p.validade) < 0) { p.status = "Expirada"; fechar("followup:" + p.id); return "Proposta expirada: gere uma nova versão."; }
      p.status = "Aceita"; fechar("followup:" + p.id);
      var c = { id: novoId("C"), propostaId: p.id, leadId: p.leadId, clienteId: null, status: "AguardandoAssinatura", assinadoEm: null, anexo: null,
        etapas: p.etapas.map(function (e) { return { id: novoId("E"), escopo: e.escopo, valor: e.valor, status: "Planejada", aprovadaEm: null, receberId: null, notaId: null }; }),
        eventos: [] };
      evento(c, "Contrato criado a partir da proposta " + p.id + " v" + p.versao + " aceita");
      st.contratos.push(c);
      lembrar("assinatura:" + c.id, "Colher assinatura do contrato " + c.id + " (" + leadDe(p).empresa + ")", "#/contratos/" + c.id, dias(2));
      location.hash = "#/contratos/" + c.id;
      return "Proposta aceita: contrato " + c.id + " aguardando assinatura.";
    },
    // O lead vira cliente na assinatura; custos previstos viram contas a pagar.
    assinar: function (c) {
      if (c.status !== "AguardandoAssinatura") return;
      var lead = por(st.leads, c.leadId); var p = por(st.propostas, c.propostaId);
      var cli = st.clientes.filter(function (k) { return k.leadId === c.leadId; })[0];
      if (!cli) { cli = { id: novoId("K"), nome: lead.empresa, tipo: "PJ", documento: "a preencher", leadId: lead.id, status: "Ativo", desde: dias(0) }; st.clientes.push(cli); }
      c.clienteId = cli.id; lead.status = "Ganho";
      c.status = "Vigente"; c.assinadoEm = dias(0); c.anexo = "contrato-" + c.id.toLowerCase() + "-assinado.pdf";
      evento(c, "Assinatura registrada (" + c.anexo + "); cliente " + cli.nome + " ativo");
      (p ? p.custos : []).forEach(function (k) {
        st.pagar.push({ id: novoId("G"), descricao: k.descricao, contratoId: c.id, valor: k.valor, vencimento: dias(30), status: "Aberta", categoria: "Custo de projeto" });
        evento(c, "Custo previsto lançado em contas a pagar: " + k.descricao);
      });
      fechar("assinatura:" + c.id);
      return "Contrato vigente e " + cli.nome + " agora é cliente.";
    },
    cancelarContrato: function (c) {
      if (c.status !== "AguardandoAssinatura") return;
      c.status = "Cancelado"; evento(c, "Contrato cancelado antes da assinatura"); fechar("assinatura:" + c.id);
      var l = por(st.leads, c.leadId); if (l) l.status = "Qualificado";
      return "Contrato cancelado; lead volta a qualificado.";
    },
    iniciar: function (c, e) {
      if (c.status !== "Vigente" || e.status !== "Planejada") return;
      e.status = "EmExecucao"; evento(c, "Etapa " + e.id + " iniciada"); return "Etapa em execução.";
    },
    entregar: function (c, e) {
      if (e.status !== "EmExecucao") return;
      e.status = "Entregue"; evento(c, "Etapa " + e.id + " entregue ao cliente para aprovação");
      lembrar("aprovacao:" + e.id, "Cobrar aprovação da etapa " + e.id + " (" + clienteDe(c).nome + ")", "#/contratos/" + c.id, dias(3));
      return "Etapa entregue. Lembrete de aprovação criado.";
    },
    pedirAjuste: function (c, e) {
      if (e.status !== "Entregue") return;
      e.status = "EmExecucao"; fechar("aprovacao:" + e.id);
      evento(c, "Cliente pediu ajuste na etapa " + e.id + "; volta para execução");
      return "Etapa voltou para execução.";
    },
    // Invariantes 3 e 4: cobrança nasce na aprovação; aprovação gera os lembretes de NFS-e e de vencimento.
    aprovar: function (c, e) {
      if (e.status !== "Entregue") return;
      e.status = "Aprovada"; e.aprovadaEm = dias(0);
      var r = { id: novoId("R"), descricao: clienteDe(c).nome + ": " + e.escopo, clienteId: c.clienteId, contratoId: c.id, etapaId: e.id, bruto: e.valor, retencoes: 0, valor: e.valor, valorPago: null, vencimento: dias(15), status: "Aberta" };
      st.receber.push(r); e.receberId = r.id;
      evento(c, "Etapa " + e.id + " aprovada pelo cliente; parcela " + r.id + " emitida");
      fechar("aprovacao:" + e.id);
      lembrar("nfse:" + e.id, "Emitir e anexar NFS-e da etapa " + e.id + " (" + clienteDe(c).nome + ")", "#/contratos/" + c.id, dias(0));
      lembrar("vencimento:" + r.id, "Parcela " + r.id + " vence em 3 dias: confirmar com o cliente", "#/receber", dias(12));
      return "Etapa aprovada: parcela " + r.id + " de " + brl(e.valor) + " emitida e lembretes de NFS-e e vencimento criados.";
    },
    // Invariante 5: valor do serviço da nota = valor bruto da etapa. Retenções reduzem o líquido a receber.
    anexarNota: function (c, e, d) {
      if (e.notaId) return;
      if (e.status !== "Aprovada" && e.status !== "Paga") return;
      if (!d.numero) return { erro: "numero", msg: "Informe o número da NFS-e." };
      if (!(d.valor > 0)) return { erro: "valor", msg: "Valor inválido. Use o formato 1.500,00." };
      if (Math.abs(d.valor - e.valor) > 0.004) return { erro: "valor", msg: "Valor do serviço na nota (" + brl(d.valor) + ") diferente do valor bruto da etapa (" + brl(e.valor) + "). Corrija a nota no emissor ou cancele e emita outra antes de anexar." };
      if (!(d.retencoes >= 0) || d.retencoes >= d.valor) return { erro: "retencoes", msg: "Retenções devem ser zero ou menores que o valor da nota." };
      if (!/^\d{4}-\d{2}$/.test(d.competencia || "")) return { erro: "competencia", msg: "Informe a competência (mês da prestação)." };
      if (!d.xml || !d.pdf) return { erro: "arquivo", msg: "Anexe o XML e o PDF da NFS-e." };
      var r = por(st.receber, e.receberId);
      var anterior = st.notas.filter(function (x) { return x.etapaId === e.id && x.status === "Cancelada"; }).pop();
      var n = { id: novoId("N"), numero: d.numero, contratoId: c.id, etapaId: e.id, receberId: e.receberId, valor: d.valor, retencoes: d.retencoes, competencia: d.competencia, xml: d.xml, pdf: d.pdf, status: "Anexada", substitui: anterior ? anterior.numero : null };
      st.notas.push(n); e.notaId = n.id;
      if (r && r.status === "Aberta") { r.retencoes = d.retencoes; r.valor = r2(r.bruto - d.retencoes); }
      e.status = r && r.status === "Paga" ? "Paga" : "Faturada";
      evento(c, "NFS-e " + d.numero + " anexada à etapa " + e.id + (anterior ? " (substitui a " + anterior.numero + ")" : "") + (d.retencoes ? "; retenções de " + brl(d.retencoes) : ""));
      fechar("nfse:" + e.id);
      lembrar("imposto:" + d.competencia, "Provisionar imposto da competência " + mes(d.competencia + "-01") + " (regime e alíquota com o contador)", "#/pagar", dias(5));
      encerrarSeConcluido(c);
      return "NFS-e " + d.numero + " anexada. " + (d.retencoes ? "Líquido a receber: " + brl(r2(e.valor - d.retencoes)) + ". " : "") + "Lembrete de imposto da competência criado.";
    },
    cancelarNota: function (n) {
      if (n.status !== "Anexada") return;
      var c = por(st.contratos, n.contratoId); var e = por(c.etapas, n.etapaId); var r = por(st.receber, n.receberId);
      n.status = "Cancelada"; e.notaId = null;
      if (e.status === "Faturada") e.status = "Aprovada";
      if (r && r.status === "Aberta") { r.retencoes = 0; r.valor = r.bruto; }
      if (c.status === "Encerrado") c.status = "Vigente";
      evento(c, "NFS-e " + n.numero + " cancelada; etapa " + e.id + " aguarda nova nota");
      lembrar("nfse:" + e.id, "Emitir NFS-e substituta da etapa " + e.id + " (" + clienteDe(c).nome + ")", "#/contratos/" + c.id, dias(0));
      return "NFS-e " + n.numero + " cancelada. Lembrete para emitir a substituta criado.";
    },
    receber: function (r) {
      if (r.status !== "Aberta") return;
      r.status = "Paga"; r.valorPago = r.valor; fechar("vencimento:" + r.id);
      var c = por(st.contratos, r.contratoId);
      if (c) {
        var e = por(c.etapas, r.etapaId);
        evento(c, "Recebimento de " + brl(r.valor) + " da parcela " + r.id + " registrado");
        if (e && e.status === "Faturada") e.status = "Paga";
        encerrarSeConcluido(c);
        if (e && !e.notaId) return "Recebimento registrado. Atenção: a etapa " + e.id + " ainda não tem NFS-e anexada.";
      }
      return "Recebimento de " + brl(r.valor) + " registrado.";
    },
    criarPagar: function (d) {
      st.pagar.push({ id: novoId("G"), descricao: d.descricao, contratoId: null, valor: d.valor, vencimento: d.vencimento, status: "Aberta", categoria: d.categoria });
      return "Conta a pagar criada.";
    },
    pagar: function (g) { if (g.status !== "Aberta") return; g.status = "Paga"; return "Pagamento de " + brl(g.valor) + " registrado."; },
    concluirLembrete: function (m) { if (m.status !== "Pendente") return; m.status = "Feito"; return "Lembrete concluído."; }
  };
  function encerrarSeConcluido(c) {
    if (c.status === "Vigente" && c.etapas.every(function (e) { return e.status === "Paga" && e.notaId; })) {
      c.status = "Encerrado"; evento(c, "Todas as etapas pagas e com NFS-e: contrato encerrado");
    }
  }

  // Ações que criam ou desfazem compromisso pedem confirmação.
  var CONFIRMAR = {
    aceitarProposta: "Registrar o aceite cria o contrato. Continuar?",
    recusarProposta: "Registrar a recusa marca o lead como perdido. Continuar?",
    perderLead: "Marcar este lead como perdido?",
    cancelarContrato: "Cancelar este contrato antes da assinatura?",
    cancelarNota: "Cancelar esta NFS-e? Confirme antes que ela já foi cancelada no emissor.",
    restaurar: "Isso apaga tudo o que você fez aqui e volta aos dados de exemplo. Continuar?"
  };

  // ---------- telas ----------
  function topo(titulo, sub, acao, migalha) {
    return (migalha ? '<p class="migalha">' + migalha + "</p>" : "") +
      '<div class="topo"><div><h1 tabindex="-1">' + esc(titulo) + "</h1>" + (sub ? "<p>" + sub + "</p>" : "") + "</div>" + (acao || "") + "</div>";
  }
  function botao(texto, acao, id, estilo, rotulo) {
    return '<button type="button" class="btn ' + (estilo || "contorno") + ' pequeno" data-acao="' + acao + '" data-id="' + esc(id) + '"' + (rotulo ? ' aria-label="' + esc(rotulo) + '"' : "") + ">" + esc(texto) + "</button>";
  }
  function campo(id, rotulo, extra, dica) {
    return '<div><label for="' + id + '">' + rotulo + "</label><input id=\"" + id + '" ' + extra + ' aria-describedby="' + id + '-erro">' + (dica ? '<p class="dica">' + dica + "</p>" : "") + '<p id="' + id + '-erro" class="erro-campo" role="alert" hidden></p></div>';
  }
  function tabela(cab, linhas) {
    return '<div class="tabela-rolagem" tabindex="0" role="region" aria-label="Tabela, role para o lado se precisar"><table><thead><tr>' + cab.map(function (h) {
      return '<th' + (h.charAt(0) === "#" ? ' class="num">' + h.slice(1) : ">" + h) + "</th>";
    }).join("") + "</tr></thead><tbody>" + linhas.join("") + "</tbody></table></div>";
  }

  var Telas = {
    painel: function () {
      var aReceber = st.receber.filter(function (r) { return r.status === "Aberta"; });
      var aPagar = st.pagar.filter(function (g) { return g.status === "Aberta"; });
      var abertas = st.propostas.filter(function (p) { return p.status === "Enviada" || p.status === "Rascunho"; });
      var lemb = st.lembretes.filter(function (m) { return m.status === "Pendente"; }).sort(function (a, b) { return a.quando < b.quando ? -1 : 1; });
      var semNota = pendentesNota();
      var etapasFunil = ["Novo", "Qualificado", "Proposta", "Ganho", "Perdido"];
      return topo("Painel", "Hoje, " + HOJE.toLocaleDateString("pt-BR", { weekday: "long", day: "numeric", month: "long" }) + ".") +
        '<section class="grade kpis" aria-label="Indicadores">' +
          kpi("A receber em aberto", brl(soma(aReceber, function (r) { return r.valor; })), aReceber.length + " parcela(s)", "#/receber") +
          kpi("A pagar em aberto", brl(soma(aPagar, function (g) { return g.valor; })), aPagar.length + " conta(s)", "#/pagar") +
          kpi("Propostas abertas", String(abertas.length), brl(soma(abertas, totalProposta)) + " em jogo", "#/propostas") +
          kpi("NFS-e pendentes", String(semNota.length), semNota.length ? "etapa aprovada sem nota" : "tudo anexado", "#/notas") +
        "</section>" +
        '<section class="card bloco"><h2>Funil de leads</h2><div class="funil">' +
          etapasFunil.map(function (s) { return "<div><strong>" + st.leads.filter(function (l) { return l.status === s; }).length + "</strong><span>" + s + "</span></div>"; }).join("") +
        "</div></section>" +
        '<div class="grade duas">' +
          '<section class="card"><h2>Lembretes</h2>' + (lemb.length ? '<ul class="lista">' + lemb.slice(0, 6).map(function (m) {
            return '<li><div><a href="' + esc(m.alvo) + '">' + esc(m.texto) + '</a><div class="sub">' + data(m.quando) + "</div></div>" + prazo(m.quando) + "</li>";
          }).join("") + "</ul>" : '<p class="vazio">Nenhum lembrete pendente.</p>') + "</section>" +
          '<section class="card"><h2>Vencimentos próximos</h2>' + vencimentos(aReceber, aPagar) + "</section>" +
        "</div>";
    },
    leads: function () {
      return topo("Leads", "Quem demonstrou interesse. Lead qualificado vira proposta; vira cliente na assinatura do contrato.") +
        '<section class="card bloco"><h2>Novo lead</h2><form class="form-grade" data-form="lead" novalidate>' +
          campo("l-empresa", "Empresa", 'name="empresa" required maxlength="80"') +
          campo("l-contato", "Contato", 'name="contato" maxlength="60"') +
          campo("l-interesse", "Interesse", 'name="interesse" maxlength="120"') +
          '<div><label for="l-origem">Origem</label><select id="l-origem" name="origem"><option>Indicação</option><option>Site</option><option>LinkedIn</option><option>Evento</option><option>Outro</option></select></div>' +
          '<div class="form-acao"><button class="btn secundario" type="submit">Criar lead</button></div></form></section>' +
        '<section class="card">' + tabela(["Empresa", "Interesse", "Origem", "Status", "Ações"], st.leads.map(function (l) {
          var a = "";
          if (l.status === "Novo") a += botao("Qualificar", "qualificarLead", l.id, "contorno", "Qualificar " + l.empresa);
          if (l.status === "Qualificado") a += botao("Criar proposta", "criarProposta", l.id, "secundario", "Criar proposta para " + l.empresa);
          if (l.status === "Proposta") { var p = st.propostas.filter(function (x) { return x.leadId === l.id; }).pop(); if (p) a += '<a class="btn contorno pequeno" href="#/propostas/' + p.id + '" aria-label="Ver proposta de ' + esc(l.empresa) + '">Ver proposta</a>'; }
          if (l.status === "Perdido") a += botao("Reabrir", "reabrirLead", l.id, "contorno", "Reabrir " + l.empresa);
          else if (l.status !== "Ganho") a += botao("Marcar como perdido", "perderLead", l.id, "perigo", "Marcar " + l.empresa + " como perdido");
          return "<tr><td><strong>" + esc(l.empresa) + '</strong><div class="dica">' + esc(l.contato) + " · desde " + data(l.criadoEm) + "</div></td><td>" + esc(l.interesse) + "</td><td>" + esc(l.origem) + "</td><td>" + selo(l.status) + '</td><td><div class="acoes">' + a + "</div></td></tr>";
        })) + "</section>";
    },
    propostas: function (id) {
      if (id) return detalheProposta(por(st.propostas, id));
      return topo("Propostas", "Escopo por etapa, com pagamento vinculado à etapa aprovada.") +
        '<section class="card">' + tabela(["Proposta", "Lead", "#Total", "Validade", "Status"], st.propostas.map(function (p) {
          return '<tr><td><a href="#/propostas/' + p.id + '"><strong>' + p.id + "</strong> v" + p.versao + "</a></td><td>" + esc(leadDe(p).empresa) + '</td><td class="num">' + brl(totalProposta(p)) + "</td><td>" + data(p.validade) + "</td><td>" + selo(p.status) + "</td></tr>";
        })) + "</section>";
    },
    contratos: function (id) {
      if (id) return detalheContrato(por(st.contratos, id));
      return topo("Contratos", "Nascem de proposta aceita. Cada etapa aprovada gera cobrança e NFS-e.") +
        '<section class="card">' + tabela(["Contrato", "Cliente", "Andamento", "#Valor", "Status"], st.contratos.map(function (c) {
          var pagas = c.etapas.filter(function (e) { return e.status === "Paga"; }).length;
          return '<tr><td><a href="#/contratos/' + c.id + '"><strong>' + c.id + "</strong></a></td><td>" + esc(clienteDe(c).nome) + "</td><td>" + pagas + " de " + c.etapas.length + ' etapas pagas</td><td class="num">' + brl(soma(c.etapas, function (e) { return e.valor; })) + "</td><td>" + selo(c.status) + "</td></tr>";
        })) + "</section>";
    },
    clientes: function () {
      return topo("Clientes", "Lead vira cliente quando o contrato é assinado. Documento real só no sistema definitivo.") +
        '<section class="card">' + tabela(["Cliente", "Tipo", "Documento", "Contratos", "#Recebido", "Status"], st.clientes.map(function (k) {
          var cs = st.contratos.filter(function (c) { return c.clienteId === k.id; });
          var rec = soma(st.receber.filter(function (r) { return r.clienteId === k.id && r.status === "Paga"; }), function (r) { return r.valorPago || 0; });
          return "<tr><td><strong>" + esc(k.nome) + '</strong><div class="dica">cliente desde ' + data(k.desde) + "</div></td><td>" + k.tipo + "</td><td>" + esc(k.documento) + "</td><td>" + cs.map(function (c) { return '<a href="#/contratos/' + c.id + '">' + c.id + "</a>"; }).join(", ") + '</td><td class="num">' + brl(rec) + "</td><td>" + selo(k.status) + "</td></tr>";
        })) + "</section>";
    },
    receber: function () {
      return topo("Contas a receber", "Uma parcela por etapa aprovada. Valor líquido = bruto menos retenções da NFS-e.") +
        '<section class="card">' + tabela(["Parcela", "Origem", "NFS-e", "Vencimento", "#Bruto", "#Líquido", "Status", "Ações"], st.receber.map(function (r) {
          var n = st.notas.filter(function (x) { return x.receberId === r.id && x.status === "Anexada"; })[0];
          return "<tr><td><strong>" + r.id + "</strong> " + esc(r.descricao) + '</td><td><a href="#/contratos/' + r.contratoId + '">' + r.contratoId + "</a> · " + r.etapaId + "</td><td>" + (n ? selo("Anexada") + " " + esc(n.numero) : '<span class="selo erro">sem nota</span>') + "</td><td>" + data(r.vencimento) + (r.status === "Aberta" ? " " + prazo(r.vencimento) : "") + '</td><td class="num">' + brl(r.bruto) + '</td><td class="num">' + brl(r.valor) + "</td><td>" + selo(r.status) + "</td><td>" + (r.status === "Aberta" ? botao("Registrar recebimento", "receber", r.id, "secundario", "Registrar recebimento da parcela " + r.id) : "") + "</td></tr>";
        })) + "</section>";
    },
    pagar: function () {
      return topo("Contas a pagar", "Custos gerais e custos previstos de contrato.") +
        '<section class="card bloco"><h2>Nova conta a pagar</h2><form class="form-grade" data-form="pagar" novalidate>' +
          campo("g-desc", "Descrição", 'name="descricao" required maxlength="80"') +
          campo("g-valor", "Valor (R$)", 'name="valor" inputmode="decimal" required', "Ex.: 1.500,00") +
          campo("g-venc", "Vencimento", 'name="vencimento" type="date" required value="' + dias(10) + '"') +
          '<div><label for="g-cat">Categoria</label><select id="g-cat" name="categoria"><option>Administrativo</option><option>Custo de projeto</option><option>Tributos</option><option>Ferramentas</option></select></div>' +
          '<div class="form-acao"><button class="btn secundario" type="submit">Criar conta</button></div></form></section>' +
        '<section class="card">' + tabela(["Conta", "Categoria", "Contrato", "Vencimento", "#Valor", "Status", "Ações"], st.pagar.map(function (g) {
          return "<tr><td><strong>" + g.id + "</strong> " + esc(g.descricao) + "</td><td>" + esc(g.categoria) + "</td><td>" + (g.contratoId ? '<a href="#/contratos/' + g.contratoId + '">' + g.contratoId + "</a>" : "geral") + "</td><td>" + data(g.vencimento) + (g.status === "Aberta" ? " " + prazo(g.vencimento) : "") + '</td><td class="num">' + brl(g.valor) + "</td><td>" + selo(g.status) + "</td><td>" + (g.status === "Aberta" ? botao("Registrar pagamento", "pagar", g.id, "secundario", "Registrar pagamento de " + g.descricao) : "") + "</td></tr>";
        })) + "</section>";
    },
    notas: function () {
      var pend = pendentesNota();
      return topo("NFS-e", "Emitida no emissor nacional ou no portal do município e anexada aqui, com XML e PDF.") +
        (pend.length ? '<section class="card bloco"><h2>Pendentes de emissão</h2><ul class="lista">' + pend.map(function (x) {
          return "<li><div><strong>" + x.e.id + "</strong> " + esc(x.e.escopo) + '<div class="sub">' + esc(clienteDe(x.c).nome) + " · " + brl(x.e.valor) + " · aprovada em " + data(x.e.aprovadaEm) + '</div></div><a class="btn secundario pequeno" href="#/contratos/' + x.c.id + '" aria-label="Anexar NFS-e da etapa ' + x.e.id + '">Anexar</a></li>';
        }).join("") + "</ul></section>" : "") +
        '<section class="card"><h2>Notas registradas</h2>' + tabela(["Número", "Contrato e etapa", "Competência", "Arquivos", "#Serviço", "#Retenções", "Status", "Ações"], st.notas.map(function (n) {
          return "<tr><td><strong>" + esc(n.numero) + "</strong>" + (n.substitui ? '<div class="dica">substitui ' + esc(n.substitui) + "</div>" : "") + '</td><td><a href="#/contratos/' + n.contratoId + '">' + n.contratoId + "</a> · " + n.etapaId + "</td><td>" + mes(n.competencia + "-01") + "</td><td>" + esc(n.xml) + ", " + esc(n.pdf) + '</td><td class="num">' + brl(n.valor) + '</td><td class="num">' + brl(n.retencoes) + "</td><td>" + selo(n.status) + "</td><td>" + (n.status === "Anexada" ? botao("Cancelar", "cancelarNota", n.id, "perigo", "Cancelar NFS-e " + n.numero) : "") + "</td></tr>";
        })) + "</section>";
    },
    lembretes: function () {
      var ord = st.lembretes.slice().sort(function (a, b) { return (a.status === b.status ? 0 : a.status === "Pendente" ? -1 : 1) || (a.quando < b.quando ? -1 : 1); });
      return topo("Lembretes", "Gerados pelo fluxo: follow-up, assinatura, aprovação, NFS-e, vencimento e imposto. Fecham sozinhos quando o passo acontece.") +
        '<section class="card"><ul class="lista">' + ord.map(function (m) {
          return '<li><div><a href="' + esc(m.alvo) + '">' + esc(m.texto) + '</a><div class="sub">' + data(m.quando) + '</div></div><div class="acoes">' + (m.status === "Pendente" ? prazo(m.quando) + " " + botao("Concluir", "concluirLembrete", m.id, "contorno", "Concluir: " + m.texto) : selo("Feito")) + "</div></li>";
        }).join("") + "</ul></section>";
    },
    blog: function () {
      return topo("Blog", "Estado levantado em 12/11/2026. O ERP só acompanha; os artigos vivem no Levante.") +
        '<div class="grade duas">' +
        '<section class="card"><h2>Levante (felipemichel.com)</h2><ul class="lista">' +
          '<li><div>Plataforma pessoal e blog técnico<div class="sub">.NET, Next.js e MongoDB; artigos geridos no /admin</div></div><span class="selo ativo">existe</span></li>' +
          '<li><div>Último commit no repositório<div class="sub">13/08/2026</div></div><span class="selo alerta">parado</span></li>' +
          '<li><div>Cutover do domínio (D0)<div class="sub">falta e-mail transacional, redirect do www e flags de indexação</div></div><span class="selo alerta">em curso</span></li>' +
          '<li><div>Artigos publicados<div class="sub">ficam no banco do Levante; não contados neste levantamento</div></div><span class="selo">a medir</span></li>' +
        "</ul></section>" +
        '<section class="card"><h2>Blog da FMA Software</h2><ul class="lista">' +
          '<li><div>Site ou blog da empresa<div class="sub">nenhum repositório, domínio ou plano editorial encontrado</div></div><span class="selo erro">não existe</span></li>' +
          '<li><div>Decisão pendente<div class="sub">blog da FMA dentro do Levante ou blog próprio da empresa</div></div><span class="selo alerta">Felipe</span></li>' +
        "</ul></section></div>";
    }
  };

  function pendentesNota() {
    var out = [];
    st.contratos.forEach(function (c) { c.etapas.forEach(function (e) { if ((e.status === "Aprovada" || e.status === "Paga") && !e.notaId) out.push({ c: c, e: e }); }); });
    return out;
  }
  function kpi(rotulo, valor, det, href) { return '<a class="card kpi" href="' + href + '"><span class="rotulo">' + rotulo + '</span><span class="valor">' + valor + '</span><span class="det">' + det + "</span></a>"; }
  function vencimentos(aReceber, aPagar) {
    var itens = aReceber.map(function (r) { return { t: "Receber", d: r.descricao, v: r.valor, q: r.vencimento, h: "#/receber" }; })
      .concat(aPagar.map(function (g) { return { t: "Pagar", d: g.descricao, v: g.valor, q: g.vencimento, h: "#/pagar" }; }))
      .sort(function (a, b) { return a.q < b.q ? -1 : 1; }).slice(0, 6);
    if (!itens.length) return '<p class="vazio">Nada em aberto.</p>';
    return '<ul class="lista">' + itens.map(function (i) {
      return '<li><div><a href="' + i.h + '">' + esc(i.d) + '</a><div class="sub">' + i.t + " · " + data(i.q) + '</div></div><div class="acoes"><strong>' + (i.t === "Pagar" ? "−" : "+") + brl(i.v) + "</strong> " + prazo(i.q) + "</div></li>";
    }).join("") + "</ul>";
  }
  function esteira(passos, atual) {
    var idx = passos.indexOf(atual);
    return '<ol class="esteira" aria-label="Situação">' + passos.map(function (p, i) {
      var cls = i < idx ? "feito" : i === idx ? "atual" : "";
      var sr = i < idx ? " (concluído)" : i === idx ? " (atual)" : "";
      return '<li class="' + cls + '"' + (i === idx ? ' aria-current="step"' : "") + ">" + esc(ROTULO[p] || p) + '<span class="sr">' + sr + "</span></li>";
    }).join("") + "</ol>";
  }

  function detalheProposta(p) {
    if (!p) return topo("Proposta não encontrada", "", "", '<a href="#/propostas">Propostas</a>');
    var lead = leadDe(p);
    var contrato = st.contratos.filter(function (c) { return c.propostaId === p.id && c.status !== "Cancelado"; })[0];
    var acoes = "";
    if (p.status === "Rascunho") acoes = botao("Enviar ao cliente", "enviarProposta", p.id, "primario");
    if (p.status === "Enviada") acoes = botao("Registrar aceite", "aceitarProposta", p.id, "primario") + botao("Nova versão", "novaVersao", p.id, "contorno") + botao("Registrar recusa", "recusarProposta", p.id, "perigo");
    if (p.status === "Expirada" || p.status === "Recusada") acoes = botao("Gerar nova versão", "novaVersao", p.id, "primario");
    if (contrato) acoes = '<a class="btn secundario" href="#/contratos/' + contrato.id + '">Ver contrato ' + contrato.id + "</a>";
    var rasc = p.status === "Rascunho";
    var fora = ["Recusada", "Expirada", "Substituida"].indexOf(p.status) >= 0;
    return topo("Proposta " + p.id + " v" + p.versao, esc(lead.empresa) + " · validade " + data(p.validade), '<div class="acoes">' + acoes + "</div>", '<a href="#/propostas">Propostas</a> / ' + p.id) +
      (fora ? "<p>" + selo(p.status) + "</p>" : esteira(["Rascunho", "Enviada", "Aceita"], p.status)) +
      '<div class="grade duas"><section class="card"><h2>Etapas e valores</h2>' +
        (p.etapas.length ? tabela(["Etapa", "Escopo", "#Valor"].concat(rasc ? [""] : []), p.etapas.map(function (e, i) {
          return "<tr><td>" + (i + 1) + "</td><td>" + esc(e.escopo) + '</td><td class="num">' + brl(e.valor) + "</td>" + (rasc ? "<td>" + botao("Remover", "removerEtapa", p.id + "|" + i, "contorno", "Remover etapa " + (i + 1)) + "</td>" : "") + "</tr>";
        }).concat(['<tr><td></td><td><strong>Total</strong></td><td class="num"><strong>' + brl(totalProposta(p)) + "</strong></td>" + (rasc ? "<td></td>" : "") + "</tr>"])) : '<p class="vazio">Nenhuma etapa ainda.</p>') +
        (rasc ? '<form class="form-grade" data-form="etapa" data-id="' + p.id + '" novalidate>' +
          campo("f-escopo", "Escopo da etapa", 'name="escopo" required maxlength="120"') +
          campo("f-valor", "Valor (R$)", 'name="valor" inputmode="decimal" required', "Ex.: 1.500,00") +
          '<div class="form-acao"><button class="btn secundario" type="submit">Incluir etapa</button></div></form>' : "") +
        '<p class="dica">Pagamento por etapa aprovada. Proposta enviada não muda: ajuste gera nova versão.</p>' +
      "</section>" +
      '<section class="card"><h2>Custos previstos</h2>' +
        (p.custos.length ? '<ul class="lista">' + p.custos.map(function (k, i) { return "<li><span>" + esc(k.descricao) + '</span><span class="acoes"><strong>' + brl(k.valor) + "</strong>" + (rasc ? botao("Remover", "removerCusto", p.id + "|" + i, "contorno", "Remover custo " + k.descricao) : "") + "</span></li>"; }).join("") + "</ul>" : '<p class="vazio">Nenhum custo previsto.</p>') +
        (rasc ? '<form class="form-grade" data-form="custo" data-id="' + p.id + '" novalidate>' +
          campo("k-desc", "Descrição do custo", 'name="descricao" required maxlength="80"') +
          campo("k-valor", "Valor (R$)", 'name="valor" inputmode="decimal" required') +
          '<div class="form-acao"><button class="btn contorno" type="submit">Incluir custo</button></div></form>' : "") +
        '<p class="dica">Na assinatura do contrato, cada custo vira conta a pagar ligada a ele.</p>' +
      "</section></div>";
  }

  function detalheContrato(c) {
    if (!c) return topo("Contrato não encontrado", "", "", '<a href="#/contratos">Contratos</a>');
    var cli = clienteDe(c);
    var acao = c.status === "AguardandoAssinatura" ? botao("Registrar assinatura", "assinar", c.id, "primario") + botao("Cancelar contrato", "cancelarContrato", c.id, "perigo") : "";
    var passosEtapa = ["Planejada", "EmExecucao", "Entregue", "Aprovada", "Faturada", "Paga"];
    return topo("Contrato " + c.id, esc(cli.nome) + ' · proposta <a href="#/propostas/' + c.propostaId + '">' + c.propostaId + "</a>" + (c.anexo ? " · " + esc(c.anexo) : ""), '<div class="acoes">' + selo(c.status) + acao + "</div>", '<a href="#/contratos">Contratos</a> / ' + c.id) +
      '<div class="grade contrato"><section class="card"><h2>Etapas</h2>' +
        c.etapas.map(function (e) {
          var r = e.receberId ? por(st.receber, e.receberId) : null;
          var n = e.notaId ? por(st.notas, e.notaId) : null;
          var a = "";
          if (c.status === "Vigente" && e.status === "Planejada") a = botao("Iniciar", "iniciar", c.id + "|" + e.id, "contorno", "Iniciar etapa " + e.id);
          if (e.status === "EmExecucao") a = botao("Marcar entregue", "entregar", c.id + "|" + e.id, "contorno", "Marcar etapa " + e.id + " como entregue");
          if (e.status === "Entregue") a = botao("Registrar aprovação do cliente", "aprovar", c.id + "|" + e.id, "primario", "Registrar aprovação da etapa " + e.id) + botao("Cliente pediu ajuste", "pedirAjuste", c.id + "|" + e.id, "contorno", "Cliente pediu ajuste na etapa " + e.id);
          var precisaNota = (e.status === "Aprovada" || e.status === "Paga") && !e.notaId;
          var pagaSemNota = e.status === "Paga" && !e.notaId;
          return '<article class="etapa"><div class="etapa-cab"><h3>' + e.id + " · " + esc(e.escopo) + "</h3><strong>" + brl(e.valor) + "</strong></div>" +
            esteira(passosEtapa, pagaSemNota ? "Aprovada" : e.status) +
            '<p class="etapa-info">' + (r ? "Parcela " + r.id + ": " + r.status + (r.status === "Aberta" ? ", vence " + data(r.vencimento) : "") + (r.retencoes ? ", líquido " + brl(r.valor) : "") : "Sem cobrança até a aprovação.") + " · " + (n ? "NFS-e " + esc(n.numero) + " anexada" : precisaNota ? '<span class="selo erro">NFS-e pendente</span>' : "NFS-e após aprovação") + "</p>" +
            (a ? '<div class="acoes">' + a + "</div>" : "") +
            (precisaNota ? formNota(c, e) : "") +
          "</article>";
        }).join("") +
      "</section>" +
      '<section class="card"><h2>Linha do tempo</h2><ol class="linha-tempo">' + c.eventos.slice().reverse().map(function (ev) {
        return "<li><time>" + data(ev.em) + "</time>" + esc(ev.texto) + "</li>";
      }).join("") + "</ol></section></div>";
  }
  function formNota(c, e) {
    var p = "n-" + e.id;
    var comp = (e.aprovadaEm || dias(0)).slice(0, 7);
    return '<form class="form-grade nota" data-form="nota" data-id="' + c.id + "|" + e.id + '" novalidate>' +
      '<p class="form-titulo">Anexar NFS-e emitida</p>' +
      campo(p + "-num", "Número da NFS-e", 'name="numero" required maxlength="20"') +
      campo(p + "-val", "Valor do serviço (R$)", 'name="valor" inputmode="decimal" required value="' + valorEmCampo(e.valor) + '"', "Igual ao bruto da etapa") +
      campo(p + "-ret", "Retenções (R$)", 'name="retencoes" inputmode="decimal" value="0,00"', "ISS, IRRF, CSRF retidos pelo tomador") +
      campo(p + "-comp", "Competência", 'name="competencia" type="month" required value="' + comp + '"', "Mês da prestação") +
      campo(p + "-arq", "XML e PDF", 'name="arquivo" type="file" accept=".xml,.pdf" multiple required', "Selecione os dois arquivos") +
      '<div class="form-acao"><button class="btn primario" type="submit">Anexar NFS-e</button></div></form>';
  }

  // ---------- roteamento e eventos ----------
  function rota() {
    var partes = (location.hash || "#/painel").replace(/^#\//, "").split("/");
    return { nome: Telas[partes[0]] ? partes[0] : "painel", id: partes[1] || null };
  }
  function render(focar) {
    var r = rota();
    var main = document.getElementById("conteudo");
    main.innerHTML = Telas[r.nome](r.id);
    document.querySelectorAll("nav a").forEach(function (a) {
      if (a.getAttribute("data-rota") === r.nome) a.setAttribute("aria-current", "page"); else a.removeAttribute("aria-current");
    });
    var h1 = main.querySelector("h1");
    document.title = (h1 ? h1.textContent + " · " : "") + "FMA Gestão";
    if (focar && h1) h1.focus();
  }
  function executar(acao, id) {
    if (CONFIRMAR[acao] && !window.confirm(CONFIRMAR[acao])) return;
    var msg;
    if (acao === "restaurar") { st = semente(); salvar(); location.hash = "#/painel"; render(true); avisar("Dados de exemplo restaurados."); return; }
    var mapa = { qualificarLead: "leads", perderLead: "leads", reabrirLead: "leads", criarProposta: "leads", enviarProposta: "propostas", aceitarProposta: "propostas", recusarProposta: "propostas", novaVersao: "propostas", receber: "receber", pagar: "pagar", concluirLembrete: "lembretes", cancelarNota: "notas" };
    var antes = location.hash;
    if (mapa[acao]) {
      msg = Regras[acao](por(st[mapa[acao]], id));
    } else if (acao === "removerEtapa" || acao === "removerCusto") {
      var pr = id.split("|"); msg = Regras[acao](por(st.propostas, pr[0]), Number(pr[1]));
    } else {
      var ids = id.split("|"); var c = por(st.contratos, ids[0]);
      msg = ids[1] ? Regras[acao](c, por(c.etapas, ids[1])) : Regras[acao](c);
    }
    salvar();
    if (location.hash === antes) render(true); // se a regra navegou, o hashchange renderiza
    if (msg) avisar(msg);
  }
  function erroCampo(f, nome, msg) {
    var el = f.elements[nome]; el.setAttribute("aria-invalid", "true");
    var p = document.getElementById(el.id + "-erro"); p.textContent = msg; p.hidden = false; el.focus();
  }
  function limparErros(f) {
    f.querySelectorAll("[aria-invalid]").forEach(function (x) { x.removeAttribute("aria-invalid"); });
    f.querySelectorAll(".erro-campo").forEach(function (x) { x.hidden = true; x.textContent = ""; });
  }
  function depois(msg, focoId) { salvar(); render(); avisar(msg); var el = focoId && document.getElementById(focoId); if (el) el.focus(); }

  document.addEventListener("click", function (ev) {
    var b = ev.target.closest("[data-acao]");
    if (b) executar(b.getAttribute("data-acao"), b.getAttribute("data-id"));
    if (ev.target.closest(".pular")) { ev.preventDefault(); var h1 = document.querySelector("main h1"); if (h1) h1.focus(); }
    if (ev.target.closest("nav a")) { document.getElementById("nav").classList.remove("aberto"); document.querySelector(".menu-toggle").setAttribute("aria-expanded", "false"); }
  });
  document.querySelector(".menu-toggle").addEventListener("click", function () {
    var nav = document.getElementById("nav"); var aberto = nav.classList.toggle("aberto");
    this.setAttribute("aria-expanded", String(aberto));
  });
  document.addEventListener("submit", function (ev) {
    var f = ev.target; ev.preventDefault(); limparErros(f);
    var tipo = f.getAttribute("data-form"); var v;
    if (tipo === "lead") {
      var empresa = f.empresa.value.trim();
      if (!empresa) return erroCampo(f, "empresa", "Informe o nome da empresa.");
      return depois(Regras.criarLead({ empresa: empresa, contato: f.contato.value.trim(), interesse: f.interesse.value.trim(), origem: f.origem.value }), "l-empresa");
    }
    if (tipo === "etapa" || tipo === "custo") {
      var p = por(st.propostas, f.getAttribute("data-id"));
      var nomeTxt = tipo === "etapa" ? "escopo" : "descricao";
      var txt = f[nomeTxt].value.trim(); v = lerValor(f.valor.value);
      if (!txt) return erroCampo(f, nomeTxt, tipo === "etapa" ? "Descreva o escopo da etapa." : "Descreva o custo.");
      if (!(v > 0)) return erroCampo(f, "valor", "Informe um valor maior que zero, por exemplo 1.500,00.");
      if (tipo === "etapa") p.etapas.push({ escopo: txt, valor: v }); else p.custos.push({ descricao: txt, valor: v });
      return depois(tipo === "etapa" ? "Etapa incluída." : "Custo incluído.", tipo === "etapa" ? "f-escopo" : "k-desc");
    }
    if (tipo === "pagar") {
      var desc = f.descricao.value.trim(); v = lerValor(f.valor.value);
      if (!desc) return erroCampo(f, "descricao", "Descreva a conta.");
      if (!(v > 0)) return erroCampo(f, "valor", "Informe um valor maior que zero, por exemplo 1.500,00.");
      if (!f.vencimento.value) return erroCampo(f, "vencimento", "Informe o vencimento.");
      return depois(Regras.criarPagar({ descricao: desc, valor: v, vencimento: f.vencimento.value, categoria: f.categoria.value }), "g-desc");
    }
    if (tipo === "nota") {
      var ids = f.getAttribute("data-id").split("|"); var c = por(st.contratos, ids[0]); var e = por(c.etapas, ids[1]);
      var arqs = Array.prototype.slice.call(f.arquivo.files || []);
      var xml = arqs.filter(function (a) { return /\.xml$/i.test(a.name); })[0];
      var pdf = arqs.filter(function (a) { return /\.pdf$/i.test(a.name); })[0];
      var ret = f.retencoes.value.trim() ? lerValor(f.retencoes.value) : 0;
      var res = Regras.anexarNota(c, e, { numero: f.numero.value.trim(), valor: lerValor(f.valor.value), retencoes: ret, competencia: f.competencia.value, xml: xml && xml.name, pdf: pdf && pdf.name });
      if (res && res.erro) return erroCampo(f, res.erro === "arquivo" ? "arquivo" : res.erro, res.msg);
      salvar(); render(true); if (res) avisar(res);
    }
  });
  window.addEventListener("hashchange", function () { if (/^#\//.test(location.hash) || !location.hash) render(true); });
  document.getElementById("restaurar").addEventListener("click", function () { executar("restaurar", ""); });

  // Exposto só para o teste de regras (tests/regras.test.cjs roda este arquivo num DOM falso).
  window.__fmaGestao = { Regras: Regras, lerValor: lerValor, estado: function () { return st; }, semente: semente };

  carregar();
  render();
})();
