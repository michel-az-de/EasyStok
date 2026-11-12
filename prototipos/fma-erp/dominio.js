/* FMA Gestão: domínio do protótipo (issue #2387). Sem DOM: roda no navegador e no Node (tests/regras.test.cjs).
   Integrações (e-mail, WhatsApp, IA) são SIMULADAS: registram atividade, nada sai da máquina.
   Spec: docs/plan/fma-erp/00-spec-dominio.md */
(function (raiz) {
  "use strict";

  var CHAVE = "fma-gestao-prototipo-v4";
  var HOJE = new Date(); HOJE.setHours(0, 0, 0, 0);

  // ---------- utilidades ----------
  function dias(n) { var d = new Date(HOJE); d.setDate(d.getDate() + n); return iso(d); }
  function iso(d) { return d.getFullYear() + "-" + String(d.getMonth() + 1).padStart(2, "0") + "-" + String(d.getDate()).padStart(2, "0"); }
  function brl(v) { return (v || 0).toLocaleString("pt-BR", { style: "currency", currency: "BRL" }); }
  function data(s) { if (!s) return "sem data"; var p = s.split("-"); return p[2] + "/" + p[1] + "/" + p[0]; }
  function mes(s) { var p = s.split("-"); return p[1] + "/" + p[0]; }
  function diasAte(s) { return Math.round((new Date(s + "T00:00:00") - HOJE) / 86400000); }
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

  // ---------- dados fictícios ----------
  function semente() {
    return {
      seq: 100,
      leads: [
        { id: "L1", empresa: "Alfa Logística", contato: "Marina", origem: "Indicação", interesse: "Painel de rastreio de entregas", status: "Proposta", criadoEm: dias(-20), conversaId: "W3" },
        { id: "L2", empresa: "Beta Clínica", contato: "Rafael", origem: "LinkedIn", interesse: "Agenda online integrada", status: "Qualificado", criadoEm: dias(-9), conversaId: null },
        { id: "L4", empresa: "Delta Educação", contato: "Joana", origem: "Evento", interesse: "Portal do aluno", status: "Ganho", criadoEm: dias(-60), conversaId: "M2" },
        { id: "L5", empresa: "Épsilon Serviços", contato: "Caio", origem: "Site", interesse: "App de campo", status: "Perdido", criadoEm: dias(-45), conversaId: null }
      ],
      propostas: [
        { id: "P1", leadId: "L1", versao: 1, status: "Enviada", validade: dias(10), enviadaEm: dias(-4), ia: false,
          etapas: [
            { escopo: "Descoberta e protótipo navegável", valor: 1000 },
            { escopo: "Etapa 1: rastreio e mapa", valor: 2000 },
            { escopo: "Etapa 2: painel e relatórios", valor: 2000 }
          ],
          custos: [{ descricao: "Licença de mapas", valor: 300 }] },
        { id: "P2", leadId: "L4", versao: 2, status: "Aceita", validade: dias(-40), enviadaEm: dias(-50), ia: false,
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
            { em: dias(-37), texto: "Assinatura registrada; Delta Educação virou cliente" },
            { em: dias(-20), texto: "Etapa E1 aprovada; parcela R1 emitida" },
            { em: dias(-18), texto: "NFS-e 101 anexada à etapa E1" },
            { em: dias(-12), texto: "Recebimento da parcela R1 registrado" },
            { em: dias(-3), texto: "Etapa E2 aprovada; parcela R2 emitida" }
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
        { id: "M3", texto: "Parcela R2 vence em 3 dias: confirmar com o cliente", alvo: "#/contas", chave: "vencimento:R2", quando: dias(9), status: "Pendente" }
      ],
      // Caixa de entrada unificada. Textos fictícios; a "ia" é o que o modelo devolveria (pré-calculado no protótipo).
      conversas: [
        { id: "W1", canal: "whatsapp", nome: "Gama Varejo", contato: "Bruno", leadId: null, naoLida: true, atualizadaEm: dias(0),
          mensagens: [
            { de: "eles", em: dias(-1), texto: "Oi Felipe, tudo bem? Peguei seu contato com a Marina da Alfa." },
            { de: "eles", em: dias(-1), texto: "A gente tem 3 lojas e o pedido do site não conversa com o estoque. Vendemos o que não temos quase toda semana." },
            { de: "eles", em: dias(0), texto: "Dá pra integrar o site com o estoque das lojas até março? Queria ver valores." }
          ],
          ia: { resumo: ["Varejo com 3 lojas, indicado pela Alfa Logística", "Dor: site vende item sem estoque", "Prazo desejado: março", "Pediu valores: está pronto para proposta"],
            intencao: "Pedido de proposta", urgencia: "alta",
            resposta: "Oi Bruno, tudo ótimo! Obrigado pelo contato e pela indicação da Marina.\n\nDá sim. Proponho começar com uma descoberta curta para mapear o site e o estoque das 3 lojas, e depois entregar a integração em duas etapas, cada uma paga só quando vocês aprovarem.\n\nTe mando a proposta por aqui ainda hoje. Pode ser?",
            etapas: [
              { escopo: "Descoberta: mapa do site, estoque e fluxo das 3 lojas", valor: 1200 },
              { escopo: "Etapa 1: estoque unificado e bloqueio de venda sem saldo", valor: 3500 },
              { escopo: "Etapa 2: sincronização em tempo real e painel de rupturas", valor: 2800 }
            ] } },
        { id: "M2", canal: "email", nome: "Delta Educação", contato: "Joana", leadId: "L4", naoLida: true, atualizadaEm: dias(0),
          assunto: "Etapa 1 aprovada: e a nota?",
          mensagens: [
            { de: "eles", em: dias(-3), texto: "Felipe, testamos matrícula e login com a coordenação. Está aprovado!" },
            { de: "eles", em: dias(0), texto: "O financeiro pediu a NFS-e para lançar o pagamento. Consegue mandar esta semana?" }
          ],
          ia: { resumo: ["Cliente aprovou a etapa E2 há 3 dias", "Financeiro do cliente aguarda a NFS-e", "Pagamento da parcela R2 depende da nota"],
            intencao: "Pedido de nota fiscal", urgencia: "alta",
            resposta: "Oi Joana, que ótima notícia, obrigado pelo retorno da coordenação!\n\nEmito a NFS-e da Etapa 1 hoje e envio por aqui com o XML e o PDF. O vencimento da parcela segue o combinado no contrato.\n\nAbraço,\nFelipe" } },
        { id: "W3", canal: "whatsapp", nome: "Alfa Logística", contato: "Marina", leadId: "L1", naoLida: false, atualizadaEm: dias(-1),
          mensagens: [
            { de: "nos", em: dias(-4), texto: "Marina, segue a proposta P1 com as 3 etapas. Qualquer dúvida me chama." },
            { de: "eles", em: dias(-1), texto: "Recebi! A diretoria gostou. Dá pra dividir a etapa 2 em duas entregas menores?" }
          ],
          ia: { resumo: ["Diretoria gostou da proposta P1", "Pediu para dividir a Etapa 2 em duas", "Sinal forte de fechamento"],
            intencao: "Ajuste de proposta", urgencia: "média",
            resposta: "Oi Marina, que bom! Dá sim. Separo a Etapa 2 em painel e relatórios, cada uma com a própria aprovação e pagamento. Te mando a versão 2 da proposta ainda hoje." } },
        { id: "M4", canal: "email", nome: "Contabilidade", contato: "Escritório", leadId: null, naoLida: false, atualizadaEm: dias(-2),
          assunto: "Fechamento de outubro",
          mensagens: [
            { de: "eles", em: dias(-2), texto: "Bom dia! Para fechar outubro precisamos das NFS-e emitidas no mês e dos comprovantes de recebimento." }
          ],
          ia: { resumo: ["Contador pede NFS-e e comprovantes de outubro", "Há 1 nota de outubro anexada (101)"],
            intencao: "Pedido de documentos", urgencia: "baixa",
            resposta: "Bom dia! Segue a NFS-e 101 (XML e PDF) e o comprovante do recebimento da parcela R1. Qualquer coisa me avisem." } }
      ],
      atividades: [
        { em: dias(-12), canal: "whatsapp", texto: "Agradecimento enviado à Delta pelo pagamento da R1", alvo: "#/contratos/C1" },
        { em: dias(-4), canal: "email", texto: "Proposta P1 enviada por e-mail para Alfa Logística", alvo: "#/propostas/P1" },
        { em: dias(-3), canal: "whatsapp", texto: "Aviso de parcela R2 emitida enviado à Delta", alvo: "#/contas" }
      ],
      // Conteúdo: o centro da v4. Publicação real só por API oficial (Metricool); aqui é simulada.
      posts: [
        { id: "PO1", formato: "artigo", titulo: "Pagamento por etapa aprovada: por que cobro assim", redes: ["linkedin", "blog"], status: "Publicado", publicadoEm: dias(-6), agendadoPara: dias(-6),
          texto: "Todo projeto de software que deu errado comigo tinha uma coisa em comum: o cliente pagava antes de ver.\n\nHoje eu divido o trabalho em etapas pequenas. Cada uma termina numa entrega que você testa. Só depois de aprovar, a parcela é cobrada e a NFS-e sai.\n\nParece detalhe de contrato. Na prática muda a conversa inteira: você para de comprar promessa e começa a comprar entrega.",
          metricas: { alcance: 1840, reacoes: 63, comentarios: 9 } },
        { id: "PO2", formato: "video", titulo: "Protótipo navegável primeiro: por que começo assim", redes: ["linkedin", "instagram"], status: "Agendado", agendadoPara: dias(1), publicadoEm: null,
          texto: "Antes de escrever a primeira linha do sistema, eu entrego um protótipo que você clica. Em 60 segundos mostro um de verdade e o que ele evita: retrabalho, escopo que cresce sozinho e a frase \"não era isso que eu pedi\".", metricas: null },
        { id: "PO3", formato: "carrossel", titulo: "3 sinais de que o sistema legado está cobrando juros", redes: ["linkedin", "instagram"], status: "Rascunho", agendadoPara: null, publicadoEm: null,
          texto: "1. Toda mudança pequena leva semanas.\n2. Só uma pessoa sabe mexer.\n3. A planilha paralela virou o sistema de verdade.", metricas: null },
        { id: "PO4", formato: "post", titulo: "IA no código, revisão humana na entrega", redes: ["linkedin"], status: "Ideia", agendadoPara: null, publicadoEm: null, texto: "",
          iaTexto: "Uso IA para escrever código mais rápido. Não uso IA para decidir o que vai para o seu sistema.\n\nCada entrega passa por teste e por revisão minha antes de chegar até você. A velocidade é da ferramenta; a responsabilidade continua sendo de quem assina.", metricas: null },
        { id: "PO5", formato: "artigo", titulo: "O que sistema financeiro ensina sobre projeto pequeno", redes: ["linkedin", "blog"], status: "Ideia", agendadoPara: null, publicadoEm: null, texto: "",
          iaTexto: "Em sistema financeiro, um centavo errado é incidente. Esse rigor parece exagero para um projeto pequeno, mas três hábitos valem para qualquer tamanho: teste antes de mudar, registro de quem fez o quê, e nada vai para produção sem alguém olhar.", metricas: null }
      ],
      // Comentários e mensagens que vieram dos posts. A IA marca a intenção; o Felipe decide.
      interacoes: [
        { id: "I1", postId: "PO1", rede: "linkedin", tipo: "comentário", nome: "Patrícia", empresa: "Zeta Contábil", em: dias(-5), status: "Nova", leadId: null,
          texto: "Faz sentido para um sistema interno pequeno? Hoje controlamos tudo em planilha.",
          ia: { intencao: "Interesse comercial", interesse: "Sair da planilha para um sistema interno" } },
        { id: "I2", postId: "PO1", rede: "linkedin", tipo: "mensagem", nome: "Otávio", empresa: "Eta Transportes", em: dias(-2), status: "Nova", leadId: null,
          texto: "Li seu artigo. Temos um app de motorista que trava toda semana. Dá para conversar?",
          ia: { intencao: "Pedido de conversa", interesse: "Estabilizar o app dos motoristas" } },
        { id: "I3", postId: "PO1", rede: "linkedin", tipo: "comentário", nome: "Luiza", empresa: "", em: dias(-6), status: "Nova", leadId: null,
          texto: "Ótimo texto, parabéns!", ia: { intencao: "Sem intenção comercial", interesse: "" } }
      ],
      // Mensagens de prospecção que o próprio Felipe envia. O sistema só registra e conta (até 5 por dia).
      abordagens: [
        { em: dias(0), leadId: "L2", texto: "Rafael, vi que a Beta Clínica abriu a segunda unidade." },
        { em: dias(-1), leadId: "L1", texto: "Marina, separei a etapa 2 como a diretoria pediu." }
      ]
    };
  }

  var st;
  function carregar() {
    try { var bruto = raiz.localStorage && raiz.localStorage.getItem(CHAVE); if (bruto) { st = JSON.parse(bruto); return st; } } catch (e) { /* sem storage */ }
    st = semente(); return st;
  }
  function salvar() { try { if (raiz.localStorage) raiz.localStorage.setItem(CHAVE, JSON.stringify(st)); } catch (e) { /* sem storage */ } }
  function restaurar() { st = semente(); salvar(); return st; }
  function novoId(prefixo) { st.seq += 1; return prefixo + st.seq; }
  function navegar(h) { if (api.navegar) api.navegar(h); }

  // Lembretes têm chave estável: o fluxo os abre e fecha pela chave.
  function lembrar(chave, texto, alvo, quando) {
    if (st.lembretes.some(function (m) { return m.chave === chave && m.status === "Pendente"; })) return;
    st.lembretes.push({ id: novoId("M"), chave: chave, texto: texto, alvo: alvo, quando: quando, status: "Pendente" });
  }
  function fechar(chave) { st.lembretes.forEach(function (m) { if (m.chave === chave && m.status === "Pendente") m.status = "Feito"; }); }
  function evento(c, texto) { c.eventos.push({ em: dias(0), texto: texto }); }
  // Atividade de integração simulada: o que o sistema real mandaria por e-mail, WhatsApp ou IA.
  function atividade(canal, texto, alvo) { st.atividades.push({ em: dias(0), canal: canal, texto: texto, alvo: alvo || "" }); }
  function totalProposta(p) { return soma(p.etapas, function (e) { return e.valor; }); }
  function leadDe(p) { return por(st.leads, p.leadId) || { empresa: "?" }; }
  function clienteDe(c) { return por(st.clientes, c.clienteId) || { nome: (por(st.leads, c.leadId) || { empresa: "?" }).empresa }; }
  function conversaDoLead(leadId) { return st.conversas.filter(function (c) { return c.leadId === leadId; })[0] || null; }

  // ---------- regras (spec seção 3) ----------
  var Regras = {
    criarLead: function (d) {
      var l = { id: novoId("L"), empresa: d.empresa, contato: d.contato || "não informado", origem: d.origem || "Outro", interesse: d.interesse || "a qualificar", status: "Novo", criadoEm: dias(0), conversaId: d.conversaId || null };
      st.leads.push(l); return l;
    },
    qualificarLead: function (l) { if (l.status !== "Novo") return; l.status = "Qualificado"; return "Lead qualificado."; },
    perderLead: function (l) { if (l.status === "Ganho" || l.status === "Perdido") return; l.status = "Perdido"; return "Lead marcado como perdido."; },
    reabrirLead: function (l) { if (l.status !== "Perdido") return; l.status = "Qualificado"; return "Lead reaberto."; },
    criarProposta: function (l, etapas) {
      if (l.status === "Novo") l.status = "Qualificado";
      if (l.status !== "Qualificado") return;
      var p = { id: novoId("P"), leadId: l.id, versao: 1, status: "Rascunho", validade: dias(15), enviadaEm: null, ia: !!etapas, etapas: (etapas || []).map(function (e) { return { escopo: e.escopo, valor: e.valor }; }), custos: [] };
      st.propostas.push(p); l.status = "Proposta";
      navegar("#/propostas/" + p.id);
      return "Proposta " + p.id + " criada em rascunho.";
    },
    removerEtapa: function (p, i) { if (p.status !== "Rascunho") return; p.etapas.splice(i, 1); return "Etapa removida."; },
    removerCusto: function (p, i) { if (p.status !== "Rascunho") return; p.custos.splice(i, 1); return "Custo removido."; },
    enviarProposta: function (p) {
      if (p.status !== "Rascunho") return;
      if (!p.etapas.length) return "Inclua ao menos uma etapa antes de enviar.";
      p.status = "Enviada"; p.enviadaEm = dias(0);
      atividade("email", "Proposta " + p.id + " enviada em PDF para " + leadDe(p).empresa, "#/propostas/" + p.id);
      lembrar("followup:" + p.id, "Follow-up da proposta " + p.id + " (" + leadDe(p).empresa + ")", "#/propostas/" + p.id, dias(3));
      atividade("ia", "Follow-up da " + p.id + " agendado; a IA rascunha a mensagem em 3 dias", "#/propostas/" + p.id);
      return "Proposta enviada a " + leadDe(p).empresa + ". Follow-up agendado para daqui a 3 dias.";
    },
    recusarProposta: function (p) {
      if (p.status !== "Enviada") return;
      p.status = "Recusada"; leadDe(p).status = "Perdido"; fechar("followup:" + p.id);
      return "Proposta recusada; lead marcado como perdido.";
    },
    // Invariante 1: proposta enviada não muda; ajuste gera versão nova.
    novaVersao: function (p) {
      if (["Enviada", "Expirada", "Recusada"].indexOf(p.status) < 0) return;
      var n = { id: novoId("P"), leadId: p.leadId, versao: p.versao + 1, status: "Rascunho", validade: dias(15), enviadaEm: null, ia: false,
        etapas: p.etapas.map(function (e) { return { escopo: e.escopo, valor: e.valor }; }),
        custos: p.custos.map(function (k) { return { descricao: k.descricao, valor: k.valor }; }) };
      if (p.status === "Enviada") p.status = "Substituida";
      fechar("followup:" + p.id);
      st.propostas.push(n); leadDe(p).status = "Proposta";
      navegar("#/propostas/" + n.id);
      return "Versão " + n.versao + " criada a partir da " + p.id + ".";
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
      atividade("email", "Contrato " + c.id + " enviado para assinatura a " + leadDe(p).empresa, "#/contratos/" + c.id);
      navegar("#/contratos/" + c.id);
      return "Proposta aceita: contrato " + c.id + " enviado para assinatura.";
    },
    // O lead vira cliente na assinatura; custos previstos viram contas a pagar.
    assinar: function (c) {
      if (c.status !== "AguardandoAssinatura") return;
      var lead = por(st.leads, c.leadId); var p = por(st.propostas, c.propostaId);
      var cli = st.clientes.filter(function (k) { return k.leadId === c.leadId; })[0];
      if (!cli) { cli = { id: novoId("K"), nome: lead.empresa, tipo: "PJ", documento: "a preencher", leadId: lead.id, status: "Ativo", desde: dias(0) }; st.clientes.push(cli); }
      c.clienteId = cli.id; lead.status = "Ganho";
      c.status = "Vigente"; c.assinadoEm = dias(0); c.anexo = "contrato-" + c.id.toLowerCase() + "-assinado.pdf";
      evento(c, "Assinatura registrada; " + cli.nome + " virou cliente");
      (p ? p.custos : []).forEach(function (k) {
        st.pagar.push({ id: novoId("G"), descricao: k.descricao, contratoId: c.id, valor: k.valor, vencimento: dias(30), status: "Aberta", categoria: "Custo de projeto" });
        evento(c, "Custo previsto lançado em contas a pagar: " + k.descricao);
      });
      fechar("assinatura:" + c.id);
      atividade("whatsapp", "Boas-vindas e cronograma enviados a " + cli.nome, "#/contratos/" + c.id);
      return "Contrato vigente. " + cli.nome + " agora é cliente.";
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
      e.status = "Entregue"; evento(c, "Etapa " + e.id + " entregue para aprovação");
      lembrar("aprovacao:" + e.id, "Cobrar aprovação da etapa " + e.id + " (" + clienteDe(c).nome + ")", "#/contratos/" + c.id, dias(3));
      atividade("email", "Pedido de aprovação da etapa " + e.id + " enviado a " + clienteDe(c).nome, "#/contratos/" + c.id);
      return "Etapa entregue. Pedido de aprovação enviado ao cliente.";
    },
    pedirAjuste: function (c, e) {
      if (e.status !== "Entregue") return;
      e.status = "EmExecucao"; fechar("aprovacao:" + e.id);
      evento(c, "Cliente pediu ajuste na etapa " + e.id);
      return "Etapa voltou para execução.";
    },
    // Invariantes 3 e 4: cobrança nasce na aprovação; aprovação gera NFS-e e vencimento.
    aprovar: function (c, e) {
      if (e.status !== "Entregue") return;
      e.status = "Aprovada"; e.aprovadaEm = dias(0);
      var r = { id: novoId("R"), descricao: clienteDe(c).nome + ": " + e.escopo, clienteId: c.clienteId, contratoId: c.id, etapaId: e.id, bruto: e.valor, retencoes: 0, valor: e.valor, valorPago: null, vencimento: dias(15), status: "Aberta" };
      st.receber.push(r); e.receberId = r.id;
      evento(c, "Etapa " + e.id + " aprovada; parcela " + r.id + " emitida");
      fechar("aprovacao:" + e.id);
      lembrar("nfse:" + e.id, "Emitir e anexar NFS-e da etapa " + e.id + " (" + clienteDe(c).nome + ")", "#/contratos/" + c.id, dias(0));
      lembrar("vencimento:" + r.id, "Parcela " + r.id + " vence em 3 dias: confirmar com o cliente", "#/contas", dias(12));
      atividade("whatsapp", "Parcela " + r.id + " de " + brl(e.valor) + " avisada a " + clienteDe(c).nome + " com Pix", "#/contas");
      return "Etapa aprovada: parcela " + r.id + " de " + brl(e.valor) + " emitida e cliente avisado.";
    },
    // Invariante 5: serviço da nota = bruto da etapa; retenções reduzem o líquido.
    anexarNota: function (c, e, d) {
      if (e.notaId) return;
      if (e.status !== "Aprovada" && e.status !== "Paga") return;
      if (!d.numero) return { erro: "numero", msg: "Informe o número da NFS-e." };
      if (!(d.valor > 0)) return { erro: "valor", msg: "Valor inválido. Use o formato 1.500,00." };
      if (Math.abs(d.valor - e.valor) > 0.004) return { erro: "valor", msg: "Valor do serviço na nota (" + brl(d.valor) + ") diferente do bruto da etapa (" + brl(e.valor) + "). Corrija no emissor ou cancele e emita outra." };
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
      lembrar("imposto:" + d.competencia, "Provisionar imposto da competência " + mes(d.competencia + "-01"), "#/contas", dias(5));
      atividade("email", "NFS-e " + d.numero + " (XML e PDF) enviada a " + clienteDe(c).nome, "#/contratos/" + c.id);
      encerrarSeConcluido(c);
      return "NFS-e " + d.numero + " anexada e enviada ao cliente." + (d.retencoes ? " Líquido a receber: " + brl(r2(e.valor - d.retencoes)) + "." : "");
    },
    cancelarNota: function (n) {
      if (n.status !== "Anexada") return;
      var c = por(st.contratos, n.contratoId); var e = por(c.etapas, n.etapaId); var r = por(st.receber, n.receberId);
      n.status = "Cancelada"; e.notaId = null;
      if (e.status === "Faturada") e.status = "Aprovada";
      if (r && r.status === "Aberta") { r.retencoes = 0; r.valor = r.bruto; }
      if (c.status === "Encerrado") c.status = "Vigente";
      evento(c, "NFS-e " + n.numero + " cancelada; etapa " + e.id + " aguarda nova nota");
      lembrar("nfse:" + e.id, "Emitir NFS-e substituta da etapa " + e.id, "#/contratos/" + c.id, dias(0));
      return "NFS-e " + n.numero + " cancelada. Lembrete da substituta criado.";
    },
    receber: function (r) {
      if (r.status !== "Aberta") return;
      r.status = "Paga"; r.valorPago = r.valor; fechar("vencimento:" + r.id);
      var c = por(st.contratos, r.contratoId);
      atividade("whatsapp", "Agradecimento pelo pagamento da " + r.id + " enviado", "#/contas");
      if (c) {
        var e = por(c.etapas, r.etapaId);
        evento(c, "Recebimento de " + brl(r.valor) + " da parcela " + r.id);
        if (e && e.status === "Faturada") e.status = "Paga";
        encerrarSeConcluido(c);
        if (e && !e.notaId) return "Recebido. Atenção: a etapa " + e.id + " ainda não tem NFS-e.";
      }
      return "Recebimento de " + brl(r.valor) + " registrado.";
    },
    cobrar: function (r) {
      if (r.status !== "Aberta") return;
      atividade("whatsapp", "Lembrete da parcela " + r.id + " (" + brl(r.valor) + ") com Pix enviado", "#/contas");
      return "Lembrete com Pix enviado pelo WhatsApp (simulado).";
    },
    criarPagar: function (d) {
      st.pagar.push({ id: novoId("G"), descricao: d.descricao, contratoId: null, valor: d.valor, vencimento: d.vencimento, status: "Aberta", categoria: d.categoria });
      return "Conta a pagar criada.";
    },
    pagar: function (g) { if (g.status !== "Aberta") return; g.status = "Paga"; return "Pagamento de " + brl(g.valor) + " registrado."; },
    concluirLembrete: function (m) { if (m.status !== "Pendente") return; m.status = "Feito"; return "Lembrete concluído."; },

    // ---------- caixa de entrada e IA (simuladas) ----------
    responder: function (conv, texto) {
      if (!texto) return;
      conv.mensagens.push({ de: "nos", em: dias(0), texto: texto }); conv.naoLida = false; conv.atualizadaEm = dias(0);
      atividade(conv.canal, "Resposta enviada a " + conv.nome, "#/conversa/" + conv.id);
      return "Enviado por " + (conv.canal === "whatsapp" ? "WhatsApp" : "e-mail") + " (simulado).";
    },
    // Conversa vira lead e proposta com as etapas que a IA extraiu. Valores são sugestão: o Felipe revisa antes de enviar.
    propostaDaConversa: function (conv) {
      var l = conv.leadId ? por(st.leads, conv.leadId) : null;
      if (!l) { l = Regras.criarLead({ empresa: conv.nome, contato: conv.contato, origem: conv.canal === "whatsapp" ? "WhatsApp" : "E-mail", interesse: (conv.ia && conv.ia.resumo[1]) || "a qualificar", conversaId: conv.id }); conv.leadId = l.id; }
      var aberta = st.propostas.filter(function (p) { return p.leadId === l.id && (p.status === "Rascunho" || p.status === "Enviada"); })[0];
      if (aberta) { navegar("#/propostas/" + aberta.id); return "Já existe a proposta " + aberta.id + " para " + l.empresa + "."; }
      if (l.status === "Perdido") l.status = "Qualificado";
      if (l.status === "Ganho") return "Este contato já é cliente.";
      atividade("ia", "IA montou a proposta de " + l.empresa + " a partir da conversa", "#/conversa/" + conv.id);
      return Regras.criarProposta(l, conv.ia && conv.ia.etapas);
    },

    // ---------- conteúdo ----------
    criarPost: function (d) {
      var p = { id: novoId("PO"), formato: d.formato || "post", titulo: d.titulo, redes: d.redes && d.redes.length ? d.redes : ["linkedin"], status: "Ideia", agendadoPara: null, publicadoEm: null, texto: "", iaTexto: null, metricas: null };
      st.posts.push(p); return p;
    },
    // A IA escreve o rascunho a partir do título. No protótipo o texto vem pronto (iaTexto) ou de um molde.
    rascunharPost: function (p) {
      if (p.status !== "Ideia") return;
      p.texto = p.iaTexto || ("Rascunho sobre \"" + p.titulo + "\": comece pelo problema do cliente, mostre um caso e feche com o próximo passo.");
      p.status = "Rascunho"; atividade("ia", "IA rascunhou \"" + p.titulo + "\"", "#/conteudo/" + p.id);
      return "Rascunho pronto. Revise o texto antes de aprovar.";
    },
    salvarTexto: function (p, texto) { if (p.status !== "Rascunho" && p.status !== "Aprovado") return; p.texto = texto; if (p.status === "Aprovado") p.status = "Rascunho"; return "Texto salvo."; },
    aprovarPost: function (p) {
      if (p.status !== "Rascunho") return;
      if (!String(p.texto || "").trim()) return "Escreva o texto antes de aprovar.";
      p.status = "Aprovado"; return "Aprovado. Agora escolha quando publicar.";
    },
    // Instagram não publica só texto; agendamento nunca no passado.
    agendarPost: function (p, d) {
      if (p.status !== "Aprovado") return { erro: "status", msg: "Aprove o texto antes de agendar." };
      var redes = d.redes || [];
      if (!redes.length) return { erro: "redes", msg: "Escolha ao menos uma rede." };
      if (!d.quando) return { erro: "quando", msg: "Escolha o dia." };
      if (d.quando < dias(0)) return { erro: "quando", msg: "Escolha hoje ou um dia futuro." };
      if (redes.indexOf("instagram") >= 0 && (p.formato === "post" || p.formato === "artigo")) return { erro: "redes", msg: "O Instagram não publica só texto: use vídeo ou carrossel, ou tire o Instagram." };
      p.redes = redes; p.agendadoPara = d.quando; p.status = "Agendado";
      atividade("metricool", "\"" + p.titulo + "\" agendado para " + data(d.quando), "#/conteudo/" + p.id);
      return "Agendado para " + data(d.quando) + " (simulado pelo Metricool).";
    },
    desagendar: function (p) { if (p.status !== "Agendado") return; p.status = "Aprovado"; p.agendadoPara = null; return "Agendamento cancelado."; },
    publicarAgora: function (p) {
      if (p.status !== "Agendado") return;
      p.status = "Publicado"; p.publicadoEm = dias(0); p.agendadoPara = dias(0); p.metricas = { alcance: 0, reacoes: 0, comentarios: 0 };
      atividade("metricool", "\"" + p.titulo + "\" publicado em " + p.redes.join(", "), "#/conteudo/" + p.id);
      return "Publicado (simulado).";
    },
    // ---------- contatos ----------
    virarLead: function (i) {
      if (i.status !== "Nova") return;
      var post = por(st.posts, i.postId);
      var l = Regras.criarLead({ empresa: i.empresa || i.nome, contato: i.nome, origem: "LinkedIn · " + (post ? post.titulo : "post"), interesse: i.ia.interesse || "a qualificar" });
      i.status = "Lead"; i.leadId = l.id;
      return i.nome + " virou lead.";
    },
    ignorar: function (i) { if (i.status !== "Nova") return; i.status = "Ignorada"; return "Interação arquivada."; },
    abordar: function (l, texto) {
      if (!String(texto || "").trim()) return { erro: "texto", msg: "Escreva a mensagem." };
      if (Consultas.abordagensHoje() >= LIMITE_DIA) return { erro: "limite", msg: "Você já mandou " + LIMITE_DIA + " mensagens hoje. Volte amanhã." };
      st.abordagens.push({ em: dias(0), leadId: l.id, texto: texto });
      if (l.status === "Novo") l.status = "Qualificado";
      return "Registrado: " + Consultas.abordagensHoje() + " de " + LIMITE_DIA + " hoje. Envie você mesmo pelo LinkedIn.";
    }
  };
  var LIMITE_DIA = 5;
  function encerrarSeConcluido(c) {
    if (c.status === "Vigente" && c.etapas.every(function (e) { return e.status === "Paga" && e.notaId; })) {
      c.status = "Encerrado"; evento(c, "Todas as etapas pagas e com NFS-e: contrato encerrado");
    }
  }

  // ---------- consultas ----------
  var Consultas = {
    pendentesNota: function () {
      var out = [];
      st.contratos.forEach(function (c) { c.etapas.forEach(function (e) { if ((e.status === "Aprovada" || e.status === "Paga") && !e.notaId) out.push({ c: c, e: e }); }); });
      return out;
    },
    aReceber: function () { return st.receber.filter(function (r) { return r.status === "Aberta"; }); },
    aPagar: function () { return st.pagar.filter(function (g) { return g.status === "Aberta"; }); },
    abordagensHoje: function () { return st.abordagens.filter(function (a) { return a.em === dias(0); }).length; },
    limiteDia: function () { return LIMITE_DIA; },
    leadsGerados: function (postId) { return st.interacoes.filter(function (i) { return i.postId === postId && i.status === "Lead"; }).length; },
    // O que pede o Felipe hoje, no máximo 5 itens, em ordem de urgência.
    hoje: function () {
      var itens = [];
      st.interacoes.filter(function (i) { return i.status === "Nova" && i.ia.intencao !== "Sem intenção comercial"; }).forEach(function (i) {
        itens.push({ area: "contatos", titulo: i.nome + (i.empresa ? " (" + i.empresa + ")" : "") + " " + (i.tipo === "mensagem" ? "mandou mensagem" : "comentou"), det: "“" + i.texto + "”", alvo: "#/contatos", acao: "Responder", urgencia: "alta" });
      });
      st.conversas.filter(function (c) { return c.naoLida; }).forEach(function (c) {
        itens.push({ area: "contatos", titulo: c.nome + ": " + (c.ia ? c.ia.intencao.toLowerCase() : "mensagem nova"), det: c.ia ? c.ia.resumo[c.ia.resumo.length - 1] : "", alvo: "#/conversa/" + c.id, acao: "Abrir", urgencia: "alta" });
      });
      Consultas.pendentesNota().forEach(function (x) {
        itens.push({ area: "contratos", titulo: "Anexar NFS-e da " + x.e.id + " (" + clienteDe(x.c).nome + ")", det: brl(x.e.valor) + " aprovado; o cliente espera a nota para pagar", alvo: "#/contratos/" + x.c.id, acao: "Anexar", urgencia: "alta" });
      });
      st.posts.filter(function (p) { return p.status === "Agendado" && diasAte(p.agendadoPara) <= 1; }).forEach(function (p) {
        itens.push({ area: "conteudo", titulo: "“" + p.titulo + "” sai " + (diasAte(p.agendadoPara) === 0 ? "hoje" : "amanhã"), det: p.redes.join(" e "), alvo: "#/conteudo/" + p.id, acao: "Revisar", urgencia: "média" });
      });
      Consultas.aPagar().filter(function (g) { return diasAte(g.vencimento) < 0; }).forEach(function (g) {
        itens.push({ area: "contas", titulo: g.descricao + " vencida", det: brl(g.valor) + " desde " + data(g.vencimento), alvo: "#/contas", acao: "Pagar", urgencia: "alta" });
      });
      var ordem = { alta: 0, "média": 1, baixa: 2 };
      return itens.sort(function (a, b) { return ordem[a.urgencia] - ordem[b.urgencia]; }).slice(0, 5);
    }
  };

  var api = {
    carregar: carregar, salvar: salvar, restaurar: restaurar, estado: function () { return st; }, semente: semente,
    Regras: Regras, Consultas: Consultas,
    util: { dias: dias, brl: brl, data: data, mes: mes, diasAte: diasAte, soma: soma, por: por, lerValor: lerValor, totalProposta: totalProposta, leadDe: leadDe, clienteDe: clienteDe, conversaDoLead: conversaDoLead },
    lerValor: lerValor, navegar: null
  };
  raiz.FMA = api;
})(typeof window !== "undefined" ? window : globalThis);
