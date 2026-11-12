/* FMA Gestão: interface do protótipo (issue #2387). Regras e dados em dominio.js.
   Spec: docs/plan/fma-erp/00-spec-dominio.md. Banca: docs/plan/fma-erp/01-banca.md */
(function () {
  "use strict";
  var F = window.FMA, U = F.util, R = F.Regras, Q = F.Consultas;
  var brl = U.brl, data = U.data, por = U.por, soma = U.soma;
  var st = F.carregar();
  var reduzir = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  function esc(s) { return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]; }); }
  function ic(n, cls) { return '<svg class="ic ' + (cls || "") + '" aria-hidden="true"><use href="#i-' + n + '"/></svg>'; }
  function iniciais(nome) { return String(nome).split(/[\s:]+/).filter(Boolean).slice(0, 2).map(function (p) { return p.charAt(0); }).join("").toUpperCase(); }
  function quando(s) {
    var n = U.diasAte(s);
    if (n === 0) return "hoje"; if (n === -1) return "ontem"; if (n === 1) return "amanhã";
    return n < 0 ? "há " + (-n) + " dias" : "em " + n + " dias";
  }
  function prazo(s) {
    var n = U.diasAte(s);
    return '<span class="pilula ' + (n < 0 ? "erro" : n <= 3 ? "alerta" : "") + '">' + (n < 0 ? "venceu " : "vence ") + quando(s) + "</span>";
  }
  var CANAL = { whatsapp: { n: "WhatsApp", i: "whatsapp" }, email: { n: "E-mail", i: "email" }, ia: { n: "IA", i: "ia" }, sistema: { n: "Sistema", i: "sistema" } };
  function canal(c) { var k = CANAL[c] || CANAL.sistema; return '<span class="canal ' + (CANAL[c] ? c : "sistema") + '" title="' + k.n + '">' + ic(k.i) + '<span class="sr">' + k.n + "</span></span>"; }
  var ROT = { AguardandoAssinatura: "Aguardando assinatura", EmExecucao: "Em execução", Substituida: "Substituída" };
  var TOM = { Enviada: "alerta", Aceita: "ok", Recusada: "erro", Expirada: "erro", AguardandoAssinatura: "alerta", Vigente: "info", Encerrado: "ok", Cancelado: "erro",
    EmExecucao: "info", Entregue: "alerta", Aprovada: "alerta", Faturada: "ok", Paga: "ok", Aberta: "alerta", Anexada: "ok", Cancelada: "erro" };
  function pilula(s) { return '<span class="pilula ' + (TOM[s] || "") + '">' + esc(ROT[s] || s) + "</span>"; }
  function botao(txt, acao, id, estilo, extra) {
    return '<button type="button" class="btn ' + (estilo || "suave") + '" data-acao="' + acao + '" data-id="' + esc(id || "") + '"' + (extra || "") + ">" + txt + "</button>";
  }
  function cabeca(titulo, sub, direita, volta) {
    return '<header class="cabeca">' + (volta ? '<a class="voltar" href="' + volta[0] + '">' + ic("voltar") + esc(volta[1]) + "</a>" : "") +
      '<div class="cabeca-linha"><div><h1 tabindex="-1">' + titulo + "</h1>" + (sub ? '<p class="sub">' + sub + "</p>" : "") + "</div>" +
      (direita ? '<div class="cabeca-acoes">' + direita + "</div>" : "") + "</div></header>";
  }
  function numero(rot, val, det, href) { return '<a class="numero" href="' + href + '"><span>' + rot + "</span><strong>" + val + "</strong><small>" + det + "</small></a>"; }

  // Gráfico de caixa projetado (SVG puro).
  var gid = 0;
  function grafico(pontos, alto) {
    var W = 640, H = alto || 200, P = 10, id = "gArea" + (++gid);
    var vals = pontos.map(function (p) { return p.v; });
    var max = Math.max.apply(null, vals.concat([0])), min = Math.min.apply(null, vals.concat([0]));
    if (max === min) max = min + 1;
    var x = function (i) { return P + i * (W - 2 * P) / (pontos.length - 1); };
    var y = function (v) { return P + (max - v) * (H - 2 * P) / (max - min); };
    var linha = pontos.map(function (p, i) { return (i ? "L" : "M") + x(i).toFixed(1) + " " + y(p.v).toFixed(1); }).join(" ");
    var area = linha + " L" + x(pontos.length - 1).toFixed(1) + " " + y(min).toFixed(1) + " L" + x(0).toFixed(1) + " " + y(min).toFixed(1) + " Z";
    var ultimo = pontos[pontos.length - 1];
    var marcas = pontos.map(function (p, i) {
      return i && p.v !== pontos[i - 1].v ? '<circle cx="' + x(i).toFixed(1) + '" cy="' + y(p.v).toFixed(1) + '" r="4" class="marca-ponto"><title>' + data(p.d) + ": " + brl(p.v) + "</title></circle>" : "";
    }).join("");
    return '<figure class="grafico"><svg viewBox="0 0 ' + W + " " + H + '" preserveAspectRatio="none" style="height:' + H + 'px" role="img" aria-label="Saldo projetado em 30 dias: ' + esc(brl(ultimo.v)) + '">' +
      '<defs><linearGradient id="' + id + '" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#D4A215" stop-opacity=".38"/><stop offset="1" stop-color="#D4A215" stop-opacity="0"/></linearGradient></defs>' +
      '<line x1="' + P + '" x2="' + (W - P) + '" y1="' + y(0).toFixed(1) + '" y2="' + y(0).toFixed(1) + '" class="zero"/>' +
      '<path d="' + area + '" fill="url(#' + id + ')"/><path d="' + linha + '" class="traco"/>' + marcas +
      '<circle cx="' + x(pontos.length - 1).toFixed(1) + '" cy="' + y(ultimo.v).toFixed(1) + '" r="6" class="ponto-final"/></svg>' +
      '<figcaption><span>hoje</span><span>+15 dias</span><span>+30 dias: <strong>' + brl(ultimo.v) + "</strong></span></figcaption></figure>";
  }

  var Telas = {};

  // Hoje: o que a IA leu e o que pede o Felipe, em ordem de urgência.
  Telas.hoje = function () {
    var b = Q.briefing(), ar = Q.aReceber(), proj = Q.projecao(30);
    var abertas = st.propostas.filter(function (p) { return p.status === "Enviada" || p.status === "Rascunho"; });
    var h = new Date().getHours(), saud = h < 12 ? "Bom dia" : h < 18 ? "Boa tarde" : "Boa noite";
    var atv = st.atividades.slice(-6).reverse();
    var lemb = st.lembretes.filter(function (m) { return m.status === "Pendente"; }).sort(function (a, c) { return a.quando < c.quando ? -1 : 1; }).slice(0, 4);
    return '<section class="hero">' +
        '<div class="hero-texto"><p class="hero-data">' + esc(new Date().toLocaleDateString("pt-BR", { weekday: "long", day: "numeric", month: "long" })) + "</p>" +
        '<h1 tabindex="-1">' + saud + ", Felipe.</h1>" +
        '<p class="hero-ia">' + ic("ia") + "<span>Li " + st.conversas.length + " conversas, o caixa e os contratos. <strong>" + b.length + (b.length === 1 ? " coisa pede" : " coisas pedem") + " você hoje.</strong></span></p></div>" +
        '<div class="hero-numeros">' +
          numero("A receber", brl(soma(ar, function (r) { return r.valor; })), ar.length + " parcela(s)", "#/financeiro") +
          numero("Caixa em 30 dias", brl(proj[proj.length - 1].v), "projetado", "#/financeiro") +
          numero("Em negociação", brl(soma(abertas, U.totalProposta)), abertas.length + " proposta(s)", "#/funil") +
        "</div></section>" +
      '<div class="grade-hoje">' +
        '<section class="painel"><div class="painel-topo"><h2>Pede você</h2><span class="dica">por urgência</span></div>' +
          (b.length ? '<ol class="fila">' + b.map(function (x, i) {
            return '<li class="fila-item' + (x.urgencia === "alta" ? " urgente" : "") + '" style="--i:' + i + '">' + canal(x.canal) +
              '<div class="fila-texto"><strong>' + esc(x.titulo) + "</strong><span>" + esc(x.det) + "</span></div>" +
              '<a class="btn ' + (i === 0 ? "primario" : "suave") + '" href="' + x.alvo + '">' + esc(x.acao) + ic("seta") + "</a></li>";
          }).join("") + "</ol>" : '<div class="vazio">' + ic("check") + "<p>Nada pendente. Bom momento para prospectar.</p></div>") +
        "</section>" +
        '<div class="coluna">' +
          '<section class="painel"><div class="painel-topo"><h2>Caixa, próximos 30 dias</h2><a href="#/financeiro" class="link">Detalhes</a></div>' + grafico(proj, 170) + "</section>" +
          '<section class="painel"><div class="painel-topo"><h2>Feito sozinho</h2><a href="#/integracoes" class="link">Automações</a></div><ul class="feed">' +
            atv.map(function (a) { return "<li>" + canal(a.canal) + '<div><a href="' + esc(a.alvo || "#/hoje") + '">' + esc(a.texto) + "</a><small>" + quando(a.em) + "</small></div></li>"; }).join("") +
          "</ul></section>" +
          (lemb.length ? '<section class="painel"><div class="painel-topo"><h2>Agenda</h2></div><ul class="agenda">' + lemb.map(function (m) {
            return '<li><a href="' + esc(m.alvo) + '">' + esc(m.texto) + "</a>" + prazo(m.quando) + botao(ic("check") + '<span class="sr">Concluir: ' + esc(m.texto) + "</span>", "concluirLembrete", m.id, "icone") + "</li>";
          }).join("") + "</ul></section>" : "") +
        "</div></div>";
  };

  // Conversas: WhatsApp e e-mail numa caixa só, com a leitura da IA no topo da conversa.
  var filtroInbox = "todos";
  Telas.inbox = function (id) {
    var lista = st.conversas.slice().sort(function (a, b) { return (b.naoLida - a.naoLida) || (a.atualizadaEm < b.atualizadaEm ? 1 : -1); })
      .filter(function (c) { return filtroInbox === "todos" || c.canal === filtroInbox; });
    var atual = id ? por(st.conversas, id) : null;
    if (atual && atual.naoLida) { atual.naoLida = false; F.salvar(); }
    return '<div class="inbox' + (atual ? " com-conversa" : "") + '">' +
      '<section class="inbox-lista" aria-label="Lista de conversas"><div class="inbox-topo"><h1 tabindex="-1">Conversas</h1>' +
        '<div class="segmentos" role="group" aria-label="Filtrar por canal">' + ["todos", "whatsapp", "email"].map(function (k) {
          return '<button type="button" data-filtro="' + k + '" aria-pressed="' + (filtroInbox === k) + '">' + ({ todos: "Todas", whatsapp: "WhatsApp", email: "E-mail" })[k] + "</button>";
        }).join("") + "</div></div><ul>" +
        lista.map(function (c) {
          var ult = c.mensagens[c.mensagens.length - 1], ativa = atual && atual.id === c.id;
          return '<li><a href="#/inbox/' + c.id + '" class="conversa' + (c.naoLida ? " nao-lida" : "") + (ativa ? " ativa" : "") + '"' + (ativa ? ' aria-current="true"' : "") + ">" +
            '<span class="avatar">' + iniciais(c.nome) + canal(c.canal) + "</span>" +
            '<span class="conversa-texto"><span class="conversa-linha"><strong>' + esc(c.nome) + "</strong><small>" + quando(c.atualizadaEm) + "</small></span>" +
            (c.assunto ? '<span class="assunto">' + esc(c.assunto) + "</span>" : "") +
            '<span class="previa">' + (ult.de === "nos" ? "Você: " : "") + esc(ult.texto) + "</span>" +
            (c.ia ? '<span class="intencao">' + ic("ia") + esc(c.ia.intencao) + "</span>" : "") + "</span>" +
            (c.naoLida ? '<span class="ponto-novo"><span class="sr">não lida</span></span>' : "") + "</a></li>";
        }).join("") + "</ul></section>" +
      '<section class="inbox-conversa" aria-label="Conversa aberta">' + (atual ? conversa(atual) : '<div class="vazio grande">' + ic("inbox") + "<p>Escolha uma conversa. A IA já leu todas e marcou o que cada uma pede.</p></div>") + "</section></div>";
  };
  function conversa(c) {
    var lead = c.leadId ? por(st.leads, c.leadId) : null;
    var cliente = lead && st.clientes.filter(function (k) { return k.leadId === lead.id; })[0];
    var vinculo = cliente ? '<a class="pilula ok" href="#/clientes">Cliente</a>' : lead ? '<a class="pilula alerta" href="#/funil">Lead · ' + esc(lead.status) + "</a>" : '<span class="pilula">Contato novo</span>';
    var prop = lead && st.propostas.filter(function (p) { return p.leadId === lead.id; }).pop();
    var acoesIa = "";
    if (!cliente && c.ia && c.ia.etapas && !prop) acoesIa += botao(ic("ia") + "Montar proposta com IA", "propostaDaConversa", c.id, "primario");
    if (prop) acoesIa += '<a class="btn suave" href="#/propostas/' + prop.id + '">' + ic("doc") + "Proposta " + prop.id + "</a>";
    if (cliente && cliente.id) { var ct = st.contratos.filter(function (k) { return k.clienteId === cliente.id; }).pop(); if (ct) acoesIa += '<a class="btn suave" href="#/contratos/' + ct.id + '">' + ic("doc") + "Contrato " + ct.id + "</a>"; }
    if (c.ia) acoesIa += botao(ic("ia") + "Rascunhar resposta", "rascunhar", c.id, acoesIa ? "suave" : "primario");
    return '<header class="conversa-topo"><a class="voltar so-celular" href="#/inbox">' + ic("voltar") + "Conversas</a>" +
        '<div class="conversa-quem"><span class="avatar grande">' + iniciais(c.nome) + '</span><div class="conversa-nome"><h2>' + esc(c.nome) + "</h2><p>" + canal(c.canal) + esc(c.contato) + (c.assunto ? " · " + esc(c.assunto) : "") + "</p></div>" + vinculo + "</div></header>" +
      '<div class="conversa-rolagem">' +
      (c.ia ? '<div class="leitura-ia"><div class="leitura-topo"><span class="orbe pequena">' + ic("ia") + "</span><strong>Leitura da IA</strong><span class=\"intencao\">" + esc(c.ia.intencao) + '</span><span class="pilula ' + (c.ia.urgencia === "alta" ? "erro" : c.ia.urgencia === "média" ? "alerta" : "") + '">urgência ' + esc(c.ia.urgencia) + "</span></div>" +
        "<ul>" + c.ia.resumo.map(function (r) { return "<li>" + esc(r) + "</li>"; }).join("") + "</ul>" +
        '<div class="acoes">' + acoesIa + "</div></div>" : "") +
      '<ol class="mensagens">' + c.mensagens.map(function (m) {
        return '<li class="msg ' + (m.de === "nos" ? "nos" : "eles") + '"><p>' + esc(m.texto).replace(/\n/g, "<br>") + "</p><small>" + (m.de === "nos" ? "Você · " : "") + quando(m.em) + "</small></li>";
      }).join("") + "</ol></div>" +
      '<form class="compor" data-form="responder" data-id="' + c.id + '"><label class="sr" for="resposta">Responder por ' + CANAL[c.canal].n + "</label>" +
        '<textarea id="resposta" name="texto" rows="3" placeholder="Responder por ' + CANAL[c.canal].n + '…"></textarea>' +
        '<div class="compor-acoes"><span class="dica">' + ic(CANAL[c.canal].i) + "Envio simulado</span><button class=\"btn primario\" type=\"submit\">" + ic("enviar") + "Enviar</button></div></form>";
  }

  // Negócios: arrastar o card para a coluna seguinte executa a regra. "Avançar" faz o mesmo pelo teclado.
  var COLUNAS = [["lead", "Leads"], ["proposta", "Proposta"], ["assinatura", "Assinatura"], ["execucao", "Execução"], ["concluido", "Concluído"]];
  var DESTINO = { lead: "proposta", proposta: "assinatura", contrato: "execucao" };
  Telas.funil = function () {
    var cards = Q.funil();
    return cabeca("Negócios", "Arraste para avançar. Cada passo dispara o seguinte: proposta, contrato, cobrança e nota.", botao(ic("mais") + "Novo lead", "abrirNovoLead", "", "primario")) +
      '<div class="kanban">' + COLUNAS.map(function (col, ci) {
        var cs = cards.filter(function (c) { return c.col === col[0]; });
        var total = soma(cs, function (c) { return c.valor || 0; });
        return '<section class="coluna-k" data-col="' + col[0] + '" aria-label="' + col[1] + '"><header><h2>' + col[1] + '</h2><span class="contagem">' + cs.length + "</span>" + (total ? "<small>" + brl(total) + "</small>" : "") + "</header>" +
          '<ul class="zona">' + cs.map(function (c) {
            var href = c.tipo === "lead" ? (c.lead.conversaId ? "#/inbox/" + c.lead.conversaId : "#/funil") : c.tipo === "proposta" ? "#/propostas/" + c.id : "#/contratos/" + c.id;
            var pode = (c.tipo === "lead") || (c.tipo === "proposta" && c.p.status === "Enviada") || (c.tipo === "contrato" && c.col === "assinatura");
            var rotulo = c.tipo === "proposta" && c.p.status === "Rascunho" ? '<span class="pilula">rascunho</span>' : "";
            return '<li class="card-k" draggable="' + pode + '" data-tipo="' + c.tipo + '" data-id="' + c.id + '">' +
              '<a href="' + href + '" class="card-k-corpo" draggable="false"><span class="avatar pequeno">' + iniciais(c.nome) + "</span><span><strong>" + esc(c.nome) + "</strong><small>" + esc(c.sub) + "</small></span></a>" +
              '<div class="card-k-pe">' + (c.valor ? "<b>" + brl(c.valor) + "</b>" : '<span class="dica">sem valor</span>') + rotulo +
              (pode ? botao(ic("seta") + '<span class="sr">Avançar ' + esc(c.nome) + " para " + COLUNAS[ci + 1][1] + "</span>", "avancar", c.tipo + "|" + c.id, "icone", ' title="Avançar para ' + COLUNAS[ci + 1][1] + '"') : "") + "</div></li>";
          }).join("") + (cs.length ? "" : '<li class="zona-vazia">Nada aqui</li>') + "</ul></section>";
      }).join("") + "</div>";
  };
  function avancar(tipo, id) {
    if (tipo === "lead") return R.criarProposta(por(st.leads, id));
    if (tipo === "proposta") {
      var p = por(st.propostas, id);
      if (p.status !== "Enviada") return "Envie a proposta antes de registrar o aceite.";
      return confirmar("Cliente aceitou a " + p.id + "?", "Cria o contrato e envia para assinatura de " + U.leadDe(p).empresa + ".", "Registrar aceite").then(function (ok) { return ok ? R.aceitarProposta(p) : null; });
    }
    if (tipo === "contrato") {
      var c = por(st.contratos, id);
      return c.status === "AguardandoAssinatura" ? R.assinar(c) : "Abra o contrato para avançar as etapas.";
    }
  }

  // Proposta: documento em papel à esquerda, edição e o e-mail da IA à direita.
  Telas.propostas = function (id) {
    var p = por(st.propostas, id);
    if (!p) { setTimeout(function () { location.hash = "#/funil"; }); return ""; }
    var lead = U.leadDe(p), rasc = p.status === "Rascunho";
    var contrato = st.contratos.filter(function (c) { return c.propostaId === p.id && c.status !== "Cancelado"; })[0];
    var acoes = "";
    if (rasc) acoes = botao(ic("enviar") + "Enviar por e-mail", "enviarProposta", p.id, "primario");
    if (p.status === "Enviada") acoes = botao("Nova versão", "novaVersao", p.id, "suave") + botao("Recusada", "recusarProposta", p.id, "suave perigo") + botao(ic("check") + "Cliente aceitou", "aceitarProposta", p.id, "primario");
    if (p.status === "Expirada" || p.status === "Recusada") acoes = botao("Gerar nova versão", "novaVersao", p.id, "primario");
    if (contrato) acoes = '<a class="btn primario" href="#/contratos/' + contrato.id + '">Contrato ' + contrato.id + ic("seta") + "</a>";
    var conv = lead.conversaId ? por(st.conversas, lead.conversaId) : null;
    var lado = "";
    if (rasc) {
      lado = '<section class="painel"><h2>Etapas</h2>' + (p.ia ? '<p class="aviso-ia">' + ic("ia") + "<span>A IA montou estas etapas a partir da conversa. Os valores são sugestão: revise antes de enviar.</span></p>" : "") +
        '<ul class="editaveis">' + p.etapas.map(function (e, i) { return "<li><span>" + esc(e.escopo) + "</span><b>" + brl(e.valor) + "</b>" + botao(ic("fechar") + '<span class="sr">Remover etapa ' + (i + 1) + "</span>", "removerEtapa", p.id + "|" + i, "icone") + "</li>"; }).join("") + "</ul>" +
        '<form class="form-linha" data-form="etapa" data-id="' + p.id + '" novalidate>' + campo("f-escopo", "Nova etapa", 'name="escopo" maxlength="120" placeholder="Ex.: relatórios"') + campo("f-valor", "Valor", 'name="valor" inputmode="decimal" placeholder="1.500,00"') + '<button class="btn suave" type="submit">' + ic("mais") + "Incluir</button></form>" +
        '<h3>Custos previstos</h3><ul class="editaveis">' + p.custos.map(function (k, i) { return "<li><span>" + esc(k.descricao) + "</span><b>" + brl(k.valor) + "</b>" + botao(ic("fechar") + '<span class="sr">Remover custo ' + esc(k.descricao) + "</span>", "removerCusto", p.id + "|" + i, "icone") + "</li>"; }).join("") + "</ul>" +
        '<form class="form-linha" data-form="custo" data-id="' + p.id + '" novalidate>' + campo("k-desc", "Novo custo", 'name="descricao" maxlength="80" placeholder="Ex.: licença"') + campo("k-valor", "Valor", 'name="valor" inputmode="decimal" placeholder="300,00"') + '<button class="btn suave" type="submit">' + ic("mais") + "Incluir</button></form></section>" +
        '<section class="painel"><h2>' + ic("email") + 'E-mail que vai junto</h2><div class="email-previa"><p><b>Para:</b> ' + esc(lead.contato) + " · " + esc(lead.empresa) + "</p><p><b>Assunto:</b> Proposta FMA Software: " + esc(lead.interesse) + "</p><hr><p>Oi " + esc(lead.contato) + ",</p><p>Segue a proposta em PDF, com o trabalho dividido em " + p.etapas.length + " etapa(s). Você só paga cada etapa depois de aprovar a entrega.</p><p>Fico à disposição para ajustar o que precisar.</p><p>Felipe</p></div><p class=\"dica\">" + ic("ia") + "Rascunho da IA, editável no sistema real.</p></section>";
    } else if (p.status === "Enviada") {
      lado = '<section class="painel"><h2>' + ic("ia") + "Follow-up pronto</h2><p class=\"dica\">Enviada " + quando(p.enviadaEm) + ". Sem resposta em 3 dias, a IA manda este texto pelo canal da conversa.</p>" +
        '<blockquote class="rascunho">Oi ' + esc(lead.contato) + ", conseguiu ver a proposta? Se ajudar, explico as etapas numa chamada de 15 minutos.</blockquote>" +
        (conv ? '<a class="btn suave" href="#/inbox/' + conv.id + '">' + ic(CANAL[conv.canal].i) + "Abrir conversa</a>" : "") + "</section>";
    } else {
      lado = '<section class="painel"><h2>Situação</h2><p>' + pilula(p.status) + "</p>" + (contrato ? '<p class="dica">Virou o contrato ' + contrato.id + ".</p>" : "") + "</section>";
    }
    return cabeca("Proposta " + p.id + ' <span class="versao">v' + p.versao + "</span>", esc(lead.empresa) + " · válida até " + data(p.validade) + " · " + pilula(p.status), acoes, ["#/funil", "Negócios"]) +
      '<div class="grade-proposta"><article class="papel" aria-label="Prévia do documento">' +
        '<header><img src="assets/fma-horizontal-reversa.svg" alt="FMA Software" width="150" height="34"><div><small>Proposta comercial</small><strong>' + p.id + " · v" + p.versao + "</strong></div></header>" +
        '<div class="papel-corpo"><p class="papel-para">Para <strong>' + esc(lead.empresa) + "</strong><br>" + esc(lead.interesse) + "</p>" +
        '<ol class="papel-etapas">' + (p.etapas.length ? p.etapas.map(function (e, i) { return '<li><span class="n">' + (i + 1) + "</span><span>" + esc(e.escopo) + "</span><b>" + brl(e.valor) + "</b></li>"; }).join("") : '<li class="dica">Nenhuma etapa ainda.</li>') + "</ol>" +
        '<div class="papel-total"><span>Total</span><strong>' + brl(U.totalProposta(p)) + "</strong></div>" +
        '<p class="papel-cond">Pagamento por etapa aprovada: cada parcela só é cobrada depois que você aprova a entrega. NFS-e a cada pagamento.</p></div>' +
      '</article><div class="coluna">' + lado + "</div></div>";
  };

  // Contrato: jornada das etapas, com a NFS-e anexada no ponto em que ela é exigida.
  var PASSOS = ["Planejada", "EmExecucao", "Entregue", "Aprovada", "Faturada", "Paga"];
  Telas.contratos = function (id) {
    var c = por(st.contratos, id);
    if (!c) { setTimeout(function () { location.hash = "#/funil"; }); return ""; }
    var cli = U.clienteDe(c);
    var total = soma(c.etapas, function (e) { return e.valor; });
    var pago = soma(c.etapas.filter(function (e) { return e.status === "Paga"; }), function (e) { return e.valor; });
    var acao = c.status === "AguardandoAssinatura" ? botao("Cancelar", "cancelarContrato", c.id, "suave perigo") + botao(ic("check") + "Assinatura recebida", "assinar", c.id, "primario") : "";
    var linha = c.eventos.map(function (e) { return { em: e.em, texto: e.texto, canal: "sistema" }; })
      .concat(st.atividades.filter(function (a) { return a.alvo && a.alvo.indexOf("/" + c.id) >= 0; }))
      .sort(function (a, b) { return a.em < b.em ? 1 : a.em > b.em ? -1 : 0; });
    return cabeca("Contrato " + c.id, '<a href="#/clientes">' + esc(cli.nome) + '</a> · proposta <a href="#/propostas/' + c.propostaId + '">' + c.propostaId + "</a> · " + pilula(c.status), acao, ["#/funil", "Negócios"]) +
      '<div class="progresso-contrato"><div><span>Recebido</span><strong>' + brl(pago) + "</strong><small>de " + brl(total) + '</small></div><div class="barra-prog" role="progressbar" aria-label="Recebido do contrato" aria-valuemin="0" aria-valuemax="' + total + '" aria-valuenow="' + pago + '" aria-valuetext="' + esc(brl(pago) + " de " + brl(total)) + '"><span style="width:' + (total ? pago / total * 100 : 0) + '%"></span></div></div>' +
      '<div class="grade-contrato"><section aria-label="Etapas"><ol class="jornada">' + c.etapas.map(function (e) {
        var idx = PASSOS.indexOf(e.status);
        var r = e.receberId ? por(st.receber, e.receberId) : null;
        var precisaNota = (e.status === "Aprovada" || e.status === "Paga") && !e.notaId;
        var a = "";
        if (c.status === "Vigente" && e.status === "Planejada") a = botao("Começar etapa", "iniciar", c.id + "|" + e.id, "suave");
        if (e.status === "EmExecucao") a = botao(ic("enviar") + "Entregar para aprovação", "entregar", c.id + "|" + e.id, "primario");
        if (e.status === "Entregue") a = botao("Cliente pediu ajuste", "pedirAjuste", c.id + "|" + e.id, "suave") + botao(ic("check") + "Cliente aprovou", "aprovar", c.id + "|" + e.id, "primario");
        return '<li class="etapa-j' + (e.status === "Paga" ? " feita" : idx >= 1 ? " andando" : "") + (precisaNota ? " pede-nota" : "") + '">' +
          '<div class="etapa-j-topo"><span class="etapa-num">' + (e.status === "Paga" ? ic("check") : esc(e.id)) + '</span><div class="etapa-j-titulo"><h3>' + esc(e.escopo) + "</h3><p>" + pilula(e.status) +
            (r ? ' <span class="dica">parcela ' + r.id + " · " + (r.status === "Paga" ? "recebida" : "vence " + quando(r.vencimento)) + (r.retencoes ? " · líquido " + brl(r.valor) : "") + "</span>" : "") + "</p></div><b>" + brl(e.valor) + "</b></div>" +
          '<ol class="passos" aria-label="Situação da etapa">' + PASSOS.map(function (s, i) { return '<li class="' + (i < idx ? "ok" : i === idx ? "agora" : "") + '"' + (i === idx ? ' aria-current="step"' : "") + "><span>" + (ROT[s] || s) + "</span></li>"; }).join("") + "</ol>" +
          (a ? '<div class="acoes">' + a + "</div>" : "") +
          (precisaNota ? formNota(c, e) : "") + "</li>";
      }).join("") + "</ol></section>" +
      '<section class="painel"><h2>Histórico</h2><ol class="historico">' + linha.map(function (x) {
        return "<li>" + canal(x.canal) + "<div><span>" + esc(x.texto) + "</span><small>" + data(x.em) + "</small></div></li>";
      }).join("") + "</ol></section></div>";
  };
  function campo(id, rotulo, extra, dica) {
    return '<div class="campo"><label for="' + id + '">' + rotulo + '</label><input id="' + id + '" ' + extra + ' aria-describedby="' + id + "-erro" + (dica ? " " + id + "-dica" : "") + '">' +
      (dica ? '<small id="' + id + '-dica" class="dica">' + dica + "</small>" : "") + '<small id="' + id + '-erro" class="erro-campo" role="alert" hidden></small></div>';
  }
  function formNota(c, e) {
    var p = "n-" + e.id;
    return '<form class="form-nota" data-form="nota" data-id="' + c.id + "|" + e.id + '" novalidate><div class="form-nota-topo">' + ic("doc") + "<div><strong>Anexar a NFS-e desta etapa</strong><small>Emita no emissor nacional e anexe aqui. Ela segue por e-mail ao cliente.</small></div></div>" +
      '<div class="form-nota-grade">' +
      campo(p + "-num", "Número", 'name="numero" maxlength="20"') +
      campo(p + "-val", "Valor do serviço", 'name="valor" inputmode="decimal" value="' + e.valor.toFixed(2).replace(".", ",") + '"', "Igual ao bruto da etapa") +
      campo(p + "-ret", "Retenções", 'name="retencoes" inputmode="decimal" value="0,00"', "ISS, IRRF, CSRF retidos") +
      campo(p + "-comp", "Competência", 'name="competencia" type="month" value="' + (e.aprovadaEm || U.dias(0)).slice(0, 7) + '"', "Mês da prestação") +
      '<div class="campo largo"><label for="' + p + '-arq">XML e PDF</label><input id="' + p + '-arq" name="arquivo" type="file" accept=".xml,.pdf" multiple aria-describedby="' + p + '-arq-erro"><small id="' + p + '-arq-erro" class="erro-campo" role="alert" hidden></small></div>' +
      '</div><div class="acoes"><button class="btn primario" type="submit">' + ic("check") + "Anexar e enviar</button></div></form>";
  }

  // Dinheiro: entra, sai e notas, em cartões de linha com a ação ao lado.
  var abaFin = "receber";
  Telas.financeiro = function () {
    var ar = Q.aReceber(), ap = Q.aPagar(), pn = Q.pendentesNota(), proj = Q.projecao(30);
    var abas = { receber: "A receber", pagar: "A pagar", notas: "Notas fiscais" };
    var corpo = "";
    if (abaFin === "receber") corpo = '<ul class="linhas">' + st.receber.slice().reverse().map(function (r) {
      var n = st.notas.filter(function (x) { return x.receberId === r.id && x.status === "Anexada"; })[0];
      return '<li><span class="avatar pequeno">' + iniciais(U.clienteDe({ clienteId: r.clienteId }).nome) + '</span><div class="linha-texto"><strong>' + esc(r.descricao) + "</strong><small>" + r.id + ' · <a href="#/contratos/' + r.contratoId + '">' + r.contratoId + "</a> · " + (n ? "NFS-e " + esc(n.numero) : '<span class="txt-erro">sem NFS-e</span>') + (r.retencoes ? " · bruto " + brl(r.bruto) : "") + "</small></div>" +
        '<div class="linha-valor"><b>' + brl(r.valor) + "</b>" + (r.status === "Aberta" ? prazo(r.vencimento) : pilula("Paga")) + "</div>" +
        '<div class="acoes">' + (r.status === "Aberta" ? botao(ic("whatsapp") + "Cobrar", "cobrar", r.id, "suave", ' title="Lembrete com Pix pelo WhatsApp (simulado)"') + botao(ic("check") + "Recebi", "receber", r.id, "primario") : "") + "</div></li>";
    }).join("") + "</ul>";
    if (abaFin === "pagar") corpo = '<div class="acoes direita">' + botao(ic("mais") + "Nova conta", "abrirNovaConta", "", "suave") + '</div><ul class="linhas">' + st.pagar.slice().reverse().map(function (g) {
      return '<li><span class="avatar pequeno escuro">' + iniciais(g.descricao) + '</span><div class="linha-texto"><strong>' + esc(g.descricao) + "</strong><small>" + esc(g.categoria) + (g.contratoId ? ' · <a href="#/contratos/' + g.contratoId + '">' + g.contratoId + "</a>" : "") + "</small></div>" +
        '<div class="linha-valor"><b>−' + brl(g.valor) + "</b>" + (g.status === "Aberta" ? prazo(g.vencimento) : pilula("Paga")) + "</div>" +
        '<div class="acoes">' + (g.status === "Aberta" ? botao(ic("check") + "Paguei", "pagar", g.id, "suave") : "") + "</div></li>";
    }).join("") + "</ul>";
    if (abaFin === "notas") corpo = (pn.length ? '<div class="alerta-faixa">' + ic("sistema") + "<span>" + pn.length + " etapa(s) aprovada(s) sem nota: " + pn.map(function (x) { return '<a href="#/contratos/' + x.c.id + '">' + x.e.id + " · " + esc(U.clienteDe(x.c).nome) + "</a>"; }).join(", ") + "</span></div>" : "") +
      '<ul class="linhas">' + st.notas.slice().reverse().map(function (n) {
        return '<li><span class="avatar pequeno">' + ic("doc") + '</span><div class="linha-texto"><strong>NFS-e ' + esc(n.numero) + (n.substitui ? ' <small class="dica">substitui ' + esc(n.substitui) + "</small>" : "") + "</strong><small>" + '<a href="#/contratos/' + n.contratoId + '">' + n.contratoId + "</a> · " + n.etapaId + " · competência " + U.mes(n.competencia + "-01") + " · " + esc(n.xml) + ", " + esc(n.pdf) + "</small></div>" +
          '<div class="linha-valor"><b>' + brl(n.valor) + "</b>" + (n.retencoes ? '<small class="dica">retido ' + brl(n.retencoes) + "</small>" : "") + pilula(n.status) + "</div>" +
          '<div class="acoes">' + (n.status === "Anexada" ? botao("Cancelar", "cancelarNota", n.id, "suave perigo") : "") + "</div></li>";
      }).join("") + "</ul>";
    return cabeca("Dinheiro", "O que entra, o que sai e as notas. A cobrança nasce quando o cliente aprova a etapa.") +
      '<div class="faixa-numeros">' +
        numero("A receber", brl(soma(ar, function (r) { return r.valor; })), ar.length + " parcela(s)", "#/financeiro") +
        numero("A pagar", brl(soma(ap, function (g) { return g.valor; })), ap.length + " conta(s)", "#/financeiro") +
        numero("Saldo em 30 dias", brl(proj[proj.length - 1].v), "projeção", "#/financeiro") +
        numero("Notas pendentes", String(pn.length), pn.length ? "travam recebimento" : "tudo em dia", "#/financeiro") +
      "</div>" +
      '<section class="painel"><div class="painel-topo"><h2>Caixa projetado</h2><span class="dica">vencimentos em aberto</span></div>' + grafico(proj, 210) + "</section>" +
      '<section class="painel"><div class="abas" role="tablist" aria-label="Lançamentos">' + Object.keys(abas).map(function (k) {
        return '<button type="button" role="tab" id="aba-' + k + '" data-aba="' + k + '" aria-selected="' + (abaFin === k) + '" tabindex="' + (abaFin === k ? 0 : -1) + '">' + abas[k] + "</button>";
      }).join("") + '</div><div role="tabpanel" aria-labelledby="aba-' + abaFin + '">' + corpo + "</div></section>";
  };

  Telas.clientes = function () {
    var perdidos = st.leads.filter(function (l) { return l.status === "Perdido"; });
    return cabeca("Clientes", "Lead vira cliente quando assina o contrato.") +
      '<div class="cartoes">' + st.clientes.map(function (k) {
        var cs = st.contratos.filter(function (c) { return c.clienteId === k.id; });
        var rec = soma(st.receber.filter(function (r) { return r.clienteId === k.id && r.status === "Paga"; }), function (r) { return r.valorPago || 0; });
        var aberto = soma(st.receber.filter(function (r) { return r.clienteId === k.id && r.status === "Aberta"; }), function (r) { return r.valor; });
        var conv = U.conversaDoLead(k.leadId);
        return '<article class="cartao-cli"><header><span class="avatar grande">' + iniciais(k.nome) + "</span><div><h2>" + esc(k.nome) + "</h2><small>cliente desde " + data(k.desde) + " · " + esc(k.documento) + "</small></div></header>" +
          "<dl><div><dt>Recebido</dt><dd>" + brl(rec) + "</dd></div><div><dt>Em aberto</dt><dd>" + brl(aberto) + "</dd></div><div><dt>Contratos</dt><dd>" + cs.length + "</dd></div></dl>" +
          '<div class="acoes">' + cs.map(function (c) { return '<a class="btn suave" href="#/contratos/' + c.id + '">' + ic("doc") + c.id + "</a>"; }).join("") + (conv ? '<a class="btn suave" href="#/inbox/' + conv.id + '">' + ic(CANAL[conv.canal].i) + "Conversa</a>" : "") + "</div></article>";
      }).join("") + "</div>" +
      (perdidos.length ? '<section class="painel"><h2>Leads perdidos</h2><ul class="linhas">' + perdidos.map(function (l) {
        return '<li><span class="avatar pequeno escuro">' + iniciais(l.empresa) + '</span><div class="linha-texto"><strong>' + esc(l.empresa) + "</strong><small>" + esc(l.interesse) + " · " + esc(l.origem) + '</small></div><div class="acoes">' + botao("Reabrir", "reabrirLead", l.id, "suave") + "</div></li>";
      }).join("") + "</ul></section>" : "");
  };

  var AUTOS = [
    ["triagemIa", "ia", "Ler cada conversa nova", "A IA resume, marca intenção e urgência e sugere o próximo passo."],
    ["propostaEmail", "email", "Enviar a proposta por e-mail", "PDF da proposta com o texto rascunhado pela IA."],
    ["followupIa", "ia", "Follow-up em 3 dias", "Sem resposta, a IA escreve e manda pelo canal da conversa."],
    ["cobrancaWhatsApp", "whatsapp", "Avisar a parcela pelo WhatsApp", "Quando o cliente aprova a etapa, recebe valor, vencimento e Pix."],
    ["lembreteNfse", "sistema", "Lembrar de emitir a NFS-e", "Etapa aprovada sem nota sobe para o topo do dia."],
    ["agradecimento", "whatsapp", "Agradecer o pagamento", "Mensagem curta quando a parcela é recebida."]
  ];
  Telas.integracoes = function () {
    var conexoes = [
      ["email", "E-mail", "SMTP da FMA", "Reaproveita o SmtpEmailCanal do EasyStok"],
      ["whatsapp", "WhatsApp", "API oficial ou WPPConnect", "Reaproveita o CanalWhatsApp do EasyStok"],
      ["ia", "IA", "Claude", "Reaproveita o AnthropicMessagesClient do EasyStok"]
    ];
    return cabeca("Conexões", "Os canais que o sistema usa sozinho. No protótipo tudo é simulado: nada sai deste navegador.") +
      '<div class="conexoes">' + conexoes.map(function (x) {
        return '<article class="conexao ' + x[0] + '">' + canal(x[0]) + "<h2>" + x[1] + "</h2><p>" + x[2] + "</p><small>" + x[3] + '</small><span class="pilula alerta">Simulado</span></article>';
      }).join("") + "</div>" +
      '<section class="painel"><h2>Automações</h2><ul class="automacoes">' + AUTOS.map(function (a) {
        var on = !!st.automacoes[a[0]];
        return "<li>" + canal(a[1]) + '<div><strong id="au-' + a[0] + '">' + a[2] + "</strong><small>" + a[3] + '</small></div><button type="button" class="chave" role="switch" aria-checked="' + on + '" aria-labelledby="au-' + a[0] + '" data-acao="alternarAutomacao" data-id="' + a[0] + '"><span></span></button></li>';
      }).join("") + "</ul></section>" +
      '<section class="painel"><h2>Blog</h2><p class="sub">Levantado em 12/11/2026. Os artigos vivem no Levante; aqui só o estado.</p><ul class="linhas">' +
        '<li><span class="avatar pequeno">LV</span><div class="linha-texto"><strong>Levante</strong><small>Blog pessoal técnico (.NET, Next.js, MongoDB). Último commit em 13/08/2026. Cutover do domínio em curso: faltam e-mail, redirect do www e indexação.</small></div><div class="linha-valor"><span class="pilula alerta">parado</span></div></li>' +
        '<li><span class="avatar pequeno escuro">FS</span><div class="linha-texto"><strong>Blog da FMA Software</strong><small>Não existe: nenhum repositório, domínio ou plano editorial. Decisão do Felipe: dentro do Levante ou próprio.</small></div><div class="linha-valor"><span class="pilula erro">não existe</span></div></li>' +
      "</ul></section>";
  };

  // ---------- diálogos ----------
  var dlg = document.getElementById("dialogo");
  function abrirDialogo(html) { dlg.innerHTML = html; dlg.returnValue = ""; dlg.showModal(); var f = dlg.querySelector("input,textarea,select,button"); if (f) f.focus(); }
  function confirmar(titulo, texto, ok) {
    return new Promise(function (res) {
      abrirDialogo('<form method="dialog" class="dialogo-corpo"><h2>' + esc(titulo) + "</h2><p>" + esc(texto) + '</p><div class="acoes direita"><button class="btn suave" value="nao">Voltar</button><button class="btn primario" value="sim">' + esc(ok || "Confirmar") + "</button></div></form>");
      dlg.addEventListener("close", function f() { dlg.removeEventListener("close", f); res(dlg.returnValue === "sim"); });
    });
  }
  var CONFIRMAR = {
    aceitarProposta: ["Cliente aceitou?", "Cria o contrato e envia para assinatura.", "Registrar aceite"],
    recusarProposta: ["Proposta recusada?", "O lead vai para perdidos. Dá para reabrir depois.", "Registrar recusa"],
    cancelarContrato: ["Cancelar o contrato?", "Só antes da assinatura. O lead volta a qualificado.", "Cancelar contrato"],
    cancelarNota: ["Cancelar esta NFS-e?", "Cancele primeiro no emissor. A etapa volta a pedir nota.", "Cancelar nota"],
    restaurar: ["Restaurar os dados de exemplo?", "Apaga o que você fez neste navegador.", "Restaurar"]
  };

  // ---------- copiloto ----------
  var copLog = document.getElementById("copiloto-log");
  var CHIPS = ["Como está o caixa?", "O que vence esta semana?", "Quais notas faltam?", "Rascunhe o follow-up da Alfa"];
  function abrirCopiloto(abrir) {
    document.body.classList.toggle("copiloto-aberto", abrir);
    document.getElementById("alternar-copiloto").setAttribute("aria-expanded", String(abrir));
  }
  function balao(de, texto) {
    var el = document.createElement("div"); el.className = "bolha " + de; copLog.appendChild(el);
    if (de === "ia" && !reduzir) {
      var i = 0; el.classList.add("digitando");
      (function passo() { i += 3; el.textContent = texto.slice(0, i); copLog.scrollTop = copLog.scrollHeight; if (i < texto.length) setTimeout(passo, 12); else el.classList.remove("digitando"); })();
    } else { el.textContent = texto; }
    copLog.scrollTop = copLog.scrollHeight;
  }
  function perguntar(q) {
    abrirCopiloto(true); balao("eu", q);
    var pensando = document.createElement("div"); pensando.className = "bolha ia pensando"; pensando.innerHTML = '<span></span><span></span><span></span><span class="sr">Pensando</span>'; copLog.appendChild(pensando);
    setTimeout(function () { pensando.remove(); balao("ia", F.perguntar(q)); }, reduzir ? 0 : 600);
  }
  function iniciarCopiloto() {
    balao("ia", "Oi, Felipe. Pergunte sobre caixa, propostas e notas, ou peça um rascunho. Respondo com o que está no sistema.");
    document.getElementById("copiloto-chips").innerHTML = CHIPS.map(function (c) { return '<button type="button" data-pergunta="' + esc(c) + '">' + esc(c) + "</button>"; }).join("");
  }

  // ---------- paleta de comandos (Ctrl K) ----------
  var pal = document.getElementById("paleta"), palQ = document.getElementById("paleta-q"), palL = document.getElementById("paleta-lista"), palSel = 0, palItens = [];
  function comandos(q) {
    var t = q.toLowerCase().trim();
    var base = [
      { r: "Ir para Hoje", h: "#/hoje", i: "hoje" }, { r: "Ir para Conversas", h: "#/inbox", i: "inbox" }, { r: "Ir para Negócios", h: "#/funil", i: "funil" },
      { r: "Ir para Dinheiro", h: "#/financeiro", i: "financeiro" }, { r: "Ir para Clientes", h: "#/clientes", i: "clientes" }, { r: "Ir para Conexões", h: "#/integracoes", i: "integracoes" },
      { r: "Criar lead", a: "abrirNovoLead", i: "mais" }, { r: "Criar conta a pagar", a: "abrirNovaConta", i: "mais" }
    ];
    st.conversas.forEach(function (c) { base.push({ r: "Conversa com " + c.nome, h: "#/inbox/" + c.id, i: CANAL[c.canal].i }); });
    st.propostas.forEach(function (p) { base.push({ r: "Proposta " + p.id + " · " + U.leadDe(p).empresa, h: "#/propostas/" + p.id, i: "doc" }); });
    st.contratos.forEach(function (c) { base.push({ r: "Contrato " + c.id + " · " + U.clienteDe(c).nome, h: "#/contratos/" + c.id, i: "doc" }); });
    var achados = t ? base.filter(function (x) { return x.r.toLowerCase().indexOf(t) >= 0; }) : base.slice(0, 8);
    if (t) achados.unshift({ r: "Perguntar à IA: " + q.trim(), p: q.trim(), i: "ia" });
    return achados.slice(0, 9);
  }
  function desenharPaleta() {
    palItens = comandos(palQ.value); palSel = Math.max(0, Math.min(palSel, palItens.length - 1));
    palL.innerHTML = palItens.map(function (x, i) { return '<li role="option" id="op-' + i + '" aria-selected="' + (i === palSel) + '" data-i="' + i + '">' + ic(x.i) + "<span>" + esc(x.r) + "</span></li>"; }).join("");
    palQ.setAttribute("aria-activedescendant", palItens.length ? "op-" + palSel : "");
    var sel = palL.querySelector('[aria-selected="true"]'); if (sel && sel.scrollIntoView) sel.scrollIntoView({ block: "nearest" });
  }
  function executarComando(x) {
    pal.close();
    if (!x) return;
    if (x.h) location.hash = x.h; else if (x.a) executar(x.a, ""); else if (x.p) perguntar(x.p);
  }
  function abrirPaleta() { palQ.value = ""; palSel = 0; desenharPaleta(); pal.showModal(); palQ.focus(); }

  // ---------- roteamento e render ----------
  function rota() {
    var partes = (location.hash || "#/hoje").replace(/^#\//, "").split("/");
    return { nome: Telas[partes[0]] ? partes[0] : "hoje", id: partes[1] || null };
  }
  function contador() {
    var n = st.conversas.filter(function (c) { return c.naoLida; }).length, el = document.getElementById("contador-inbox");
    el.hidden = !n; el.textContent = n; el.setAttribute("aria-label", n + " não lidas");
  }
  function render(focar) {
    var r = rota(), main = document.getElementById("conteudo");
    main.innerHTML = Telas[r.nome](r.id);
    main.setAttribute("data-tela", r.nome);
    var ativo = r.nome === "propostas" || r.nome === "contratos" ? "funil" : r.nome;
    document.querySelectorAll(".trilho a[data-rota]").forEach(function (a) {
      if (a.getAttribute("data-rota") === ativo) a.setAttribute("aria-current", "page"); else a.removeAttribute("aria-current");
    });
    var h1 = main.querySelector("h1");
    document.title = (h1 ? h1.textContent.trim() + " · " : "") + "FMA Gestão";
    contador();
    if (focar) { window.scrollTo(0, 0); if (h1) h1.focus({ preventScroll: true }); }
  }
  F.navegar = function (h) { location.hash = h; };

  function avisar(msg) {
    var t = document.getElementById("toast");
    t.innerHTML = ic("check") + "<span>" + esc(msg) + "</span>"; t.classList.add("visivel");
    clearTimeout(avisar._t); avisar._t = setTimeout(function () { t.classList.remove("visivel"); }, 4200);
  }
  function concluir(resultado) {
    var antes = location.hash;
    Promise.resolve(resultado).then(function (m) { F.salvar(); render(location.hash !== antes); if (m) avisar(m); });
  }

  function executar(acao, id) {
    if (CONFIRMAR[acao]) {
      var c = CONFIRMAR[acao];
      return confirmar(c[0], c[1], c[2]).then(function (ok) { if (ok) executarJa(acao, id); });
    }
    executarJa(acao, id);
  }
  var POR_COLECAO = { qualificarLead: "leads", perderLead: "leads", reabrirLead: "leads", enviarProposta: "propostas", aceitarProposta: "propostas", recusarProposta: "propostas", novaVersao: "propostas",
    receber: "receber", cobrar: "receber", pagar: "pagar", concluirLembrete: "lembretes", cancelarNota: "notas", propostaDaConversa: "conversas" };
  function executarJa(acao, id) {
    var ids = (id || "").split("|");
    if (acao === "restaurar") { st = F.restaurar(); filtroInbox = "todos"; abaFin = "receber"; if (location.hash === "#/hoje") render(true); else location.hash = "#/hoje"; avisar("Dados de exemplo restaurados."); return; }
    if (acao === "abrirNovoLead") return abrirDialogo('<form class="dialogo-corpo" data-form="lead" novalidate><h2>Novo lead</h2>' + campo("l-empresa", "Empresa", 'name="empresa" maxlength="80" autocomplete="off"') + campo("l-contato", "Contato", 'name="contato" maxlength="60" autocomplete="off"') + campo("l-interesse", "O que precisa", 'name="interesse" maxlength="120" autocomplete="off"') + '<div class="acoes direita"><button class="btn suave" type="button" data-fechar>Cancelar</button><button class="btn primario" type="submit">Criar lead</button></div></form>');
    if (acao === "abrirNovaConta") return abrirDialogo('<form class="dialogo-corpo" data-form="pagar" novalidate><h2>Nova conta a pagar</h2>' + campo("g-desc", "Descrição", 'name="descricao" maxlength="80" autocomplete="off"') + campo("g-valor", "Valor", 'name="valor" inputmode="decimal" placeholder="1.500,00"') + campo("g-venc", "Vencimento", 'name="vencimento" type="date" value="' + U.dias(10) + '"') + '<div class="campo"><label for="g-cat">Categoria</label><select id="g-cat" name="categoria"><option>Administrativo</option><option>Custo de projeto</option><option>Tributos</option><option>Ferramentas</option></select></div><div class="acoes direita"><button class="btn suave" type="button" data-fechar>Cancelar</button><button class="btn primario" type="submit">Criar conta</button></div></form>');
    if (acao === "rascunhar") { var cv = por(st.conversas, id), ta = document.getElementById("resposta"); if (ta && cv && cv.ia) escrever(ta, cv.ia.resposta); return; }
    if (acao === "avancar") return concluir(avancar(ids[0], ids[1]));
    var msg;
    if (acao === "alternarAutomacao") msg = R.alternarAutomacao(id);
    else if (POR_COLECAO[acao]) msg = R[acao](por(st[POR_COLECAO[acao]], id));
    else if (acao === "removerEtapa" || acao === "removerCusto") msg = R[acao](por(st.propostas, ids[0]), Number(ids[1]));
    else { var ct = por(st.contratos, ids[0]); msg = ids[1] ? R[acao](ct, por(ct.etapas, ids[1])) : R[acao](ct); }
    concluir(msg);
  }
  function escrever(ta, texto) {
    ta.focus();
    if (reduzir) { ta.value = texto; return; }
    var i = 0; ta.value = ""; ta.classList.add("escrevendo");
    (function passo() { i += 4; ta.value = texto.slice(0, i); ta.scrollTop = ta.scrollHeight; if (i < texto.length) setTimeout(passo, 10); else ta.classList.remove("escrevendo"); })();
  }

  // ---------- eventos ----------
  function erroCampo(f, nome, msg) {
    var el = f.elements[nome]; el.setAttribute("aria-invalid", "true");
    var p = document.getElementById(el.id + "-erro"); p.textContent = msg; p.hidden = false; el.focus();
  }
  function limparErros(f) {
    f.querySelectorAll("[aria-invalid]").forEach(function (x) { x.removeAttribute("aria-invalid"); });
    f.querySelectorAll(".erro-campo").forEach(function (x) { x.hidden = true; x.textContent = ""; });
  }
  document.addEventListener("click", function (ev) {
    var b = ev.target.closest("[data-acao]");
    if (b) { ev.preventDefault(); executar(b.getAttribute("data-acao"), b.getAttribute("data-id")); return; }
    var fil = ev.target.closest("[data-filtro]"); if (fil) { filtroInbox = fil.getAttribute("data-filtro"); render(); document.querySelector('[data-filtro="' + filtroInbox + '"]').focus(); return; }
    var aba = ev.target.closest("[data-aba]"); if (aba) { abaFin = aba.getAttribute("data-aba"); render(); document.querySelector('[data-aba="' + abaFin + '"]').focus(); return; }
    var per = ev.target.closest("[data-pergunta]"); if (per) { perguntar(per.getAttribute("data-pergunta")); return; }
    if (ev.target.closest("[data-fechar]")) { dlg.close(); return; }
    var op = ev.target.closest("#paleta-lista li"); if (op) { executarComando(palItens[+op.getAttribute("data-i")]); return; }
    if (ev.target === pal) { pal.close(); return; }
    if (ev.target.closest(".pular")) { ev.preventDefault(); var h1 = document.querySelector("main h1"); if (h1) h1.focus(); }
  });
  // Setas entre abas (padrão WAI-ARIA de tabs).
  document.addEventListener("keydown", function (ev) {
    var aba = ev.target.closest && ev.target.closest("[role=tab]"); if (!aba || (ev.key !== "ArrowRight" && ev.key !== "ArrowLeft")) return;
    var todas = Array.prototype.slice.call(aba.parentNode.querySelectorAll("[role=tab]")), i = todas.indexOf(aba);
    var prox = todas[(i + (ev.key === "ArrowRight" ? 1 : -1) + todas.length) % todas.length];
    ev.preventDefault(); prox.click();
  });
  document.addEventListener("submit", function (ev) {
    var f = ev.target; if (f.getAttribute("method") === "dialog") return;
    ev.preventDefault(); limparErros(f);
    var tipo = f.getAttribute("data-form"), v;
    if (f.id === "copiloto-form") { var q = f.q.value.trim(); if (q) { perguntar(q); f.q.value = ""; } return; }
    if (tipo === "responder") {
      var conv = por(st.conversas, f.getAttribute("data-id")), txt = f.texto.value.trim();
      if (!txt) { f.texto.focus(); return; }
      return concluir(R.responder(conv, txt));
    }
    if (tipo === "lead") {
      var empresa = f.empresa.value.trim();
      if (!empresa) return erroCampo(f, "empresa", "Informe o nome da empresa.");
      R.criarLead({ empresa: empresa, contato: f.contato.value.trim(), interesse: f.interesse.value.trim() });
      dlg.close(); return concluir("Lead " + empresa + " criado na coluna Leads.");
    }
    if (tipo === "pagar") {
      var desc = f.descricao.value.trim(); v = U.lerValor(f.valor.value);
      if (!desc) return erroCampo(f, "descricao", "Descreva a conta.");
      if (!(v > 0)) return erroCampo(f, "valor", "Use um valor maior que zero, como 1.500,00.");
      if (!f.vencimento.value) return erroCampo(f, "vencimento", "Informe o vencimento.");
      var mm = R.criarPagar({ descricao: desc, valor: v, vencimento: f.vencimento.value, categoria: f.categoria.value });
      dlg.close(); abaFin = "pagar";
      if (location.hash !== "#/financeiro") location.hash = "#/financeiro";
      return concluir(mm);
    }
    if (tipo === "etapa" || tipo === "custo") {
      var p = por(st.propostas, f.getAttribute("data-id")), nome = tipo === "etapa" ? "escopo" : "descricao";
      var t = f[nome].value.trim(); v = U.lerValor(f.valor.value);
      if (!t) return erroCampo(f, nome, tipo === "etapa" ? "Descreva a etapa." : "Descreva o custo.");
      if (!(v > 0)) return erroCampo(f, "valor", "Use um valor maior que zero, como 1.500,00.");
      if (tipo === "etapa") p.etapas.push({ escopo: t, valor: v }); else p.custos.push({ descricao: t, valor: v });
      F.salvar(); render(); var foco = document.getElementById(tipo === "etapa" ? "f-escopo" : "k-desc"); if (foco) foco.focus(); return;
    }
    if (tipo === "nota") {
      var ids = f.getAttribute("data-id").split("|"), c = por(st.contratos, ids[0]), e = por(c.etapas, ids[1]);
      var arqs = Array.prototype.slice.call(f.arquivo.files || []);
      var xml = arqs.filter(function (a) { return /\.xml$/i.test(a.name); })[0], pdf = arqs.filter(function (a) { return /\.pdf$/i.test(a.name); })[0];
      var ret = f.retencoes.value.trim() ? U.lerValor(f.retencoes.value) : 0;
      var res = R.anexarNota(c, e, { numero: f.numero.value.trim(), valor: U.lerValor(f.valor.value), retencoes: ret, competencia: f.competencia.value, xml: xml && xml.name, pdf: pdf && pdf.name });
      if (res && res.erro) return erroCampo(f, res.erro, res.msg);
      concluir(res);
    }
  });

  // Arrastar e soltar no funil.
  var arrastando = null;
  document.addEventListener("dragstart", function (ev) {
    var c = ev.target.closest && ev.target.closest(".card-k"); if (!c) return;
    arrastando = { tipo: c.getAttribute("data-tipo"), id: c.getAttribute("data-id") };
    ev.dataTransfer.effectAllowed = "move"; ev.dataTransfer.setData("text/plain", arrastando.id);
    c.classList.add("arrastando"); document.body.classList.add("modo-arraste");
    var alvo = document.querySelector('.coluna-k[data-col="' + DESTINO[arrastando.tipo] + '"]'); if (alvo) alvo.classList.add("pode");
  });
  document.addEventListener("dragend", function () {
    arrastando = null; document.body.classList.remove("modo-arraste");
    document.querySelectorAll(".arrastando,.pode,.sobre").forEach(function (x) { x.classList.remove("arrastando", "pode", "sobre"); });
  });
  document.addEventListener("dragover", function (ev) {
    var col = ev.target.closest && ev.target.closest(".coluna-k"); if (!col || !arrastando) return;
    ev.preventDefault();
    document.querySelectorAll(".sobre").forEach(function (x) { if (x !== col) x.classList.remove("sobre"); }); col.classList.add("sobre");
  });
  document.addEventListener("drop", function (ev) {
    var col = ev.target.closest && ev.target.closest(".coluna-k"); if (!col || !arrastando) return;
    ev.preventDefault();
    var a = arrastando;
    if (col.getAttribute("data-col") === DESTINO[a.tipo]) concluir(avancar(a.tipo, a.id));
    else avisar("Um passo por vez: solte na coluna destacada.");
  });

  document.addEventListener("keydown", function (ev) {
    if ((ev.ctrlKey || ev.metaKey) && ev.key && ev.key.toLowerCase() === "k") { ev.preventDefault(); if (pal.open) pal.close(); else abrirPaleta(); }
    if (ev.key === "Escape" && document.body.classList.contains("copiloto-aberto") && !pal.open && !dlg.open && window.innerWidth < 1280) abrirCopiloto(false);
  });
  palQ.addEventListener("input", function () { palSel = 0; desenharPaleta(); });
  palQ.addEventListener("keydown", function (ev) {
    if (!palItens.length) return;
    if (ev.key === "ArrowDown") { ev.preventDefault(); palSel = (palSel + 1) % palItens.length; desenharPaleta(); }
    if (ev.key === "ArrowUp") { ev.preventDefault(); palSel = (palSel - 1 + palItens.length) % palItens.length; desenharPaleta(); }
    if (ev.key === "Enter") { ev.preventDefault(); executarComando(palItens[palSel]); }
  });
  document.getElementById("abrir-paleta").addEventListener("click", abrirPaleta);
  document.getElementById("alternar-copiloto").addEventListener("click", function () {
    var abrir = !document.body.classList.contains("copiloto-aberto"); abrirCopiloto(abrir);
    if (abrir) document.getElementById("copiloto-q").focus();
  });
  document.getElementById("fechar-copiloto").addEventListener("click", function () { abrirCopiloto(false); document.getElementById("alternar-copiloto").focus(); });
  document.getElementById("restaurar").addEventListener("click", function () { executar("restaurar", ""); });
  window.addEventListener("hashchange", function () { if (/^#\//.test(location.hash) || !location.hash) render(true); });

  iniciarCopiloto();
  abrirCopiloto(window.innerWidth >= 1280);
  render();
})();
