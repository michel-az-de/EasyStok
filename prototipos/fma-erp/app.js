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
  var CANAL = { whatsapp: { n: "WhatsApp", i: "whatsapp" }, email: { n: "E-mail", i: "email" }, ia: { n: "IA", i: "ia" }, sistema: { n: "Sistema", i: "sistema" },
    linkedin: { n: "LinkedIn", i: "linkedin" }, instagram: { n: "Instagram", i: "instagram" }, blog: { n: "Blog", i: "blog" }, metricool: { n: "Metricool", i: "sistema" } };
  function canal(c) { var k = CANAL[c] || CANAL.sistema; return '<span class="canal ' + (CANAL[c] ? c : "sistema") + '" title="' + k.n + '">' + ic(k.i) + '<span class="sr">' + k.n + "</span></span>"; }
  var ROT = { AguardandoAssinatura: "Aguardando assinatura", EmExecucao: "Em execução", Substituida: "Substituída" };
  var TOM = { Enviada: "alerta", Aceita: "ok", Recusada: "erro", Expirada: "erro", AguardandoAssinatura: "alerta", Vigente: "info", Encerrado: "ok", Cancelado: "erro",
    EmExecucao: "info", Entregue: "alerta", Aprovada: "alerta", Faturada: "ok", Paga: "ok", Aberta: "alerta", Anexada: "ok", Cancelada: "erro",
    Aprovado: "info", Agendado: "alerta", Publicado: "ok", Nova: "alerta", Lead: "ok", Novo: "alerta", Qualificado: "info", Proposta: "alerta", Ganho: "ok", Perdido: "erro" };
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

  var FORMATO = { artigo: "Artigo", video: "Vídeo", carrossel: "Carrossel", post: "Post" };
  var AREA = { conteudo: "conteudo", contatos: "clientes", contratos: "doc", contas: "financeiro" };
  function redes(lista) { return '<span class="redes">' + lista.map(canal).join("") + "</span>"; }
  function semana(d) { return new Date(d + "T12:00:00").toLocaleDateString("pt-BR", { weekday: "short" }).replace(".", ""); }
  function metricas(p) {
    var m = p.metricas || {};
    return '<dl class="metricas"><div><dt>Alcance</dt><dd>' + (m.alcance || 0).toLocaleString("pt-BR") + "</dd></div><div><dt>Reações</dt><dd>" + (m.reacoes || 0) +
      "</dd></div><div><dt>Comentários</dt><dd>" + (m.comentarios || 0) + '</dd></div><div class="destaque"><dt>Leads</dt><dd>' + Q.leadsGerados(p.id) + "</dd></div></dl>";
  }

  var Telas = {};

  // Conteúdo: o centro da v4. Escrever, aprovar e agendar; a publicação passa pelo Metricool (simulado).
  Telas.conteudo = function (id) {
    if (id) return editorPost(id);
    var hj = Q.hoje();
    var faixa = '<section class="painel"><div class="painel-topo"><h2>Pede você hoje</h2><span class="dica">no máximo 5, por urgência</span></div>' +
      (hj.length ? '<ol class="fila">' + hj.map(function (x, i) {
        return '<li class="fila-item' + (x.urgencia === "alta" ? " urgente" : "") + '" style="--i:' + i + '"><span class="canal sistema">' + ic(AREA[x.area] || "sistema") + "</span>" +
          '<div class="fila-texto"><strong>' + esc(x.titulo) + "</strong><span>" + esc(x.det) + "</span></div>" +
          '<a class="btn ' + (i === 0 ? "primario" : "suave") + '" href="' + x.alvo + '">' + esc(x.acao) + ic("seta") + "</a></li>";
      }).join("") + "</ol>" : '<div class="vazio">' + ic("check") + "<p>Nada pendente. Bom momento para escrever.</p></div>") + "</section>";
    var dias14 = [];
    for (var d = 0; d < 14; d++) dias14.push(U.dias(d));
    var cal = '<section class="painel"><div class="painel-topo"><h2>Próximas duas semanas</h2><span class="dica">o que já está agendado</span></div><ol class="semana">' + dias14.map(function (dd, i) {
      var ps = st.posts.filter(function (p) { return (p.status === "Agendado" && p.agendadoPara === dd) || (p.status === "Publicado" && p.publicadoEm === dd); });
      return '<li class="dia' + (i === 0 ? " hoje" : "") + (ps.length ? " tem" : "") + '"><span class="dia-sem">' + (i === 0 ? "hoje" : semana(dd)) + "</span><b>" + dd.slice(8) + "</b>" +
        ps.map(function (p) { return '<a class="chip-post" href="#/conteudo/' + p.id + '" title="' + esc(p.titulo) + '">' + esc(FORMATO[p.formato]) + "</a>"; }).join("") + "</li>";
    }).join("") + "</ol></section>";
    var PROX = { Ideia: "Rascunhar com IA", Rascunho: "Revisar e aprovar", Aprovado: "Escolher o dia" };
    var prod = st.posts.filter(function (p) { return PROX[p.status]; });
    var producao = '<section class="painel"><div class="painel-topo"><h2>Em produção</h2><span class="dica">' + prod.length + " peça(s)</span></div>" +
      (prod.length ? '<ul class="posts">' + prod.map(function (p) {
        return '<li><a class="post-card" href="#/conteudo/' + p.id + '"><span class="formato">' + esc(FORMATO[p.formato]) + "</span><strong>" + esc(p.titulo) + "</strong>" +
          '<span class="post-pe">' + redes(p.redes) + pilula(p.status) + '<span class="proximo">' + PROX[p.status] + ic("seta") + "</span></span></a></li>";
      }).join("") + "</ul>" : '<div class="vazio"><p>Nenhuma peça em produção. Comece por uma ideia.</p></div>') + "</section>";
    var pub = st.posts.filter(function (p) { return p.status === "Publicado"; }).reverse();
    var publicados = '<section class="painel"><div class="painel-topo"><h2>Publicados</h2><span class="dica">métricas fictícias</span></div>' +
      (pub.length ? '<ul class="linhas">' + pub.map(function (p) {
        return '<li class="pub"><div class="linha-texto"><strong><a href="#/conteudo/' + p.id + '">' + esc(p.titulo) + "</a></strong><small>" + esc(FORMATO[p.formato]) + " · " + data(p.publicadoEm) + " · " +
          p.redes.map(function (r) { return CANAL[r].n; }).join(", ") + "</small></div>" + metricas(p) + "</li>";
      }).join("") + "</ul>" : '<div class="vazio"><p>Nada publicado ainda.</p></div>') + "</section>";
    return cabeca("Conteúdo", "Escreva, aprove e agende. A publicação passa pelo Metricool, pela API oficial; aqui é simulada.", botao(ic("mais") + "Nova ideia", "abrirNovaIdeia", "", "primario")) +
      faixa + cal + '<div class="grade-duas">' + producao + publicados + "</div>";
  };

  var PASSOS_POST = ["Ideia", "Rascunho", "Aprovado", "Agendado", "Publicado"];
  function editorPost(id) {
    var p = por(st.posts, id);
    if (!p) { setTimeout(function () { location.hash = "#/conteudo"; }); return ""; }
    var idx = PASSOS_POST.indexOf(p.status), editavel = p.status === "Rascunho" || p.status === "Aprovado";
    var acoes = "";
    if (p.status === "Ideia") acoes = botao(ic("ia") + "Rascunhar com IA", "rascunharPost", p.id, "primario");
    if (p.status === "Rascunho") acoes = botao(ic("check") + "Aprovar texto", "aprovarPost", p.id, "primario");
    if (p.status === "Agendado") acoes = botao("Desagendar", "desagendar", p.id, "suave") + botao(ic("enviar") + "Publicar agora", "publicarAgora", p.id, "primario");
    var texto = editavel
      ? '<form data-form="texto" data-id="' + p.id + '" novalidate><label class="sr" for="texto-post">Texto do post</label><textarea id="texto-post" name="texto" rows="11">' + esc(p.texto) +
        '</textarea><div class="acoes entre"><span class="dica">' + (p.status === "Aprovado" ? "Editar volta o post para rascunho." : "A IA escreve; você decide o que sai.") + '</span><button class="btn suave" type="submit">Salvar texto</button></div></form>'
      : p.status === "Ideia" ? '<div class="vazio">' + ic("ia") + "<p>Só o título por enquanto. A IA rascunha a partir dele e você revisa.</p></div>"
      : '<div class="texto-final">' + esc(p.texto).replace(/\n/g, "<br>") + "</div>";
    var lado = "";
    if (p.status === "Aprovado") {
      lado = '<section class="painel"><h2>Quando e onde</h2><form data-form="agendar" data-id="' + p.id + '" novalidate>' +
        campo("a-quando", "Dia", 'name="quando" type="date" min="' + U.dias(0) + '" value="' + U.dias(2) + '"') +
        '<fieldset class="redes-escolha" aria-describedby="a-redes-erro"><legend>Onde publicar</legend>' + ["linkedin", "instagram", "blog"].map(function (r) {
          return '<label><input type="checkbox" name="redes" value="' + r + '"' + (p.redes.indexOf(r) >= 0 ? " checked" : "") + ">" + ic(CANAL[r].i) + CANAL[r].n + "</label>";
        }).join("") + '<small id="a-redes-erro" class="erro-campo" role="alert" hidden></small></fieldset>' +
        '<p class="dica">O Metricool publica pela API oficial do LinkedIn e do Instagram. Nada de robô de convite ou de mensagem.</p>' +
        '<div class="acoes"><button class="btn primario" type="submit">' + ic("check") + "Agendar</button></div></form></section>";
    } else if (p.status === "Agendado") {
      lado = '<section class="painel"><h2>Agendado</h2><p class="grande">' + data(p.agendadoPara) + " · " + quando(p.agendadoPara) + "</p>" + redes(p.redes) + '<p class="dica">Simulado pelo Metricool.</p></section>';
    } else if (p.status === "Publicado") {
      lado = '<section class="painel"><h2>Resultado</h2>' + metricas(p) + '<p class="dica">Métricas fictícias.</p></section>';
    } else {
      lado = '<section class="painel"><h2>Próximo passo</h2><p class="dica">' + (p.status === "Ideia" ? "Peça o rascunho à IA." : "Revise o texto e aprove. Só texto aprovado pode ser agendado.") + "</p></section>";
    }
    var ints = st.interacoes.filter(function (i) { return i.postId === p.id; });
    if (ints.length) lado += '<section class="painel"><h2>Quem reagiu</h2><ul class="linhas">' + ints.map(function (i) {
      return '<li><span class="avatar pequeno">' + iniciais(i.nome) + '</span><div class="linha-texto"><strong>' + esc(i.nome) + (i.empresa ? " · " + esc(i.empresa) : "") + "</strong><small>" + esc(i.ia.intencao) + "</small></div>" + pilula(i.status) + "</li>";
    }).join("") + '</ul><a class="link" href="#/contatos">Ver em Contatos</a></section>';
    return cabeca(esc(p.titulo), esc(FORMATO[p.formato]) + " · " + pilula(p.status), acoes, ["#/conteudo", "Conteúdo"]) +
      '<ol class="passos largo" aria-label="Situação do post">' + PASSOS_POST.map(function (s, i) { return '<li class="' + (i < idx ? "ok" : i === idx ? "agora" : "") + '"' + (i === idx ? ' aria-current="step"' : "") + "><span>" + s + "</span></li>"; }).join("") + "</ol>" +
      '<div class="grade-proposta"><article class="post-previa" aria-label="Prévia do post"><header><span class="avatar">FS</span><div><strong>Felipe · FMA Software</strong><small>' + esc(FORMATO[p.formato]) + " · " + p.redes.map(function (r) { return CANAL[r].n; }).join(", ") + "</small></div></header>" +
        (p.formato === "video" || p.formato === "carrossel" ? '<div class="midia">' + ic(p.formato === "video" ? "enviar" : "doc") + "<span>" + (p.formato === "video" ? "Vídeo de até 60 s" : "Carrossel em lâminas") + "</span></div>" : "") +
        texto + '</article><div class="coluna">' + lado + "</div></div>";
  }

  // Contatos: o que chegou dos posts, as conversas e os leads. Prospecção é manual, até 5 por dia.
  Telas.contatos = function () {
    var novas = st.interacoes.filter(function (i) { return i.status === "Nova"; });
    var hojeN = Q.abordagensHoje(), lim = Q.limiteDia();
    var chegou = '<section class="painel"><div class="painel-topo"><h2>Chegou dos posts</h2><span class="dica">a IA marca a intenção; você decide</span></div>' +
      (novas.length ? '<ul class="linhas">' + novas.map(function (i) {
        var frio = i.ia.intencao === "Sem intenção comercial", post = por(st.posts, i.postId);
        return '<li><span class="avatar pequeno">' + iniciais(i.nome) + canal(i.rede) + '</span><div class="linha-texto"><strong>' + esc(i.nome) + (i.empresa ? " · " + esc(i.empresa) : "") + " <small>" + esc(i.tipo) + " · " + quando(i.em) + "</small></strong>" +
          '<span class="citacao">“' + esc(i.texto) + "”</span><small>" + '<span class="intencao' + (frio ? " fria" : "") + '">' + ic("ia") + esc(i.ia.intencao) + "</span>" + (post ? ' em <a href="#/conteudo/' + post.id + '">' + esc(post.titulo) + "</a>" : "") + "</small></div>" +
          '<div class="acoes">' + botao("Ignorar", "ignorar", i.id, "suave") + (frio ? "" : botao(ic("mais") + "Virar lead", "virarLead", i.id, "primario")) + "</div></li>";
      }).join("") + "</ul>" : '<div class="vazio">' + ic("check") + "<p>Nada novo dos posts.</p></div>") + "</section>";
    var convs = st.conversas.slice().sort(function (a, b) { return (b.naoLida - a.naoLida) || (a.atualizadaEm < b.atualizadaEm ? 1 : -1); });
    var conversas = '<section class="painel"><div class="painel-topo"><h2>Conversas</h2><span class="dica">WhatsApp e e-mail</span></div><ul class="lista-conversas">' + convs.map(function (c) {
      var ult = c.mensagens[c.mensagens.length - 1];
      return '<li><a href="#/conversa/' + c.id + '" class="conversa' + (c.naoLida ? " nao-lida" : "") + '"><span class="avatar">' + iniciais(c.nome) + canal(c.canal) + "</span>" +
        '<span class="conversa-texto"><span class="conversa-linha"><strong>' + esc(c.nome) + "</strong><small>" + quando(c.atualizadaEm) + "</small></span>" +
        '<span class="previa">' + (ult.de === "nos" ? "Você: " : "") + esc(ult.texto) + "</span></span>" + (c.naoLida ? '<span class="ponto-novo"><span class="sr">não lida</span></span>' : "") + "</a></li>";
    }).join("") + "</ul></section>";
    var ativos = st.leads.filter(function (l) { return l.status !== "Ganho" && l.status !== "Perdido"; }).reverse();
    var leads = '<section class="painel"><div class="painel-topo"><h2>Leads</h2><span class="contador-msg' + (hojeN >= lim ? " cheio" : "") + '">' + hojeN + " de " + lim + " mensagens hoje</span></div>" +
      '<ul class="linhas">' + ativos.map(function (l) {
        var prop = st.propostas.filter(function (p) { return p.leadId === l.id; }).pop();
        var a = botao(ic("enviar") + "Abordar", "abrirAbordar", l.id, "suave");
        if (prop) a += '<a class="btn suave" href="#/propostas/' + prop.id + '">' + ic("doc") + "Proposta " + prop.id + "</a>";
        else a += botao(ic("doc") + "Montar proposta", "criarProposta", l.id, "primario");
        return '<li><span class="avatar pequeno">' + iniciais(l.empresa) + '</span><div class="linha-texto"><strong>' + esc(l.empresa) + " · " + esc(l.contato) + "</strong><small>" + esc(l.interesse) + " · " + esc(l.origem) + "</small></div>" +
          '<div class="linha-valor">' + pilula(l.status) + '</div><div class="acoes">' + a + "</div></li>";
      }).join("") + "</ul>" + '<p class="dica">O sistema não envia mensagem no LinkedIn: ele rascunha, conta e registra. Quem envia é você.</p></section>';
    var perdidos = st.leads.filter(function (l) { return l.status === "Perdido"; });
    return cabeca("Contatos", "Quem chegou pelos posts, quem está conversando e quem você está prospectando.", botao(ic("mais") + "Novo lead", "abrirNovoLead", "", "suave")) +
      chegou + '<div class="grade-duas">' + leads + conversas + "</div>" +
      (perdidos.length ? '<section class="painel"><h2>Perdidos</h2><ul class="linhas">' + perdidos.map(function (l) {
        return '<li><span class="avatar pequeno escuro">' + iniciais(l.empresa) + '</span><div class="linha-texto"><strong>' + esc(l.empresa) + "</strong><small>" + esc(l.interesse) + '</small></div><div class="acoes">' + botao("Reabrir", "reabrirLead", l.id, "suave") + "</div></li>";
      }).join("") + "</ul></section>" : "");
  };
  Telas.conversa = function (id) {
    var c = por(st.conversas, id);
    if (!c) { setTimeout(function () { location.hash = "#/contatos"; }); return ""; }
    if (c.naoLida) { c.naoLida = false; F.salvar(); }
    return '<div class="conversa-solo"><h1 class="sr" tabindex="-1">Conversa com ' + esc(c.nome) + "</h1>" + conversa(c) + "</div>";
  };
  function conversa(c) {
    var lead = c.leadId ? por(st.leads, c.leadId) : null;
    var cliente = lead && st.clientes.filter(function (k) { return k.leadId === lead.id; })[0];
    var vinculo = cliente ? '<a class="pilula ok" href="#/contratos">Cliente</a>' : lead ? '<a class="pilula alerta" href="#/contatos">Lead · ' + esc(lead.status) + "</a>" : '<span class="pilula">Contato novo</span>';
    var prop = lead && st.propostas.filter(function (p) { return p.leadId === lead.id; }).pop();
    var acoesIa = "";
    if (!cliente && c.ia && c.ia.etapas && !prop) acoesIa += botao(ic("ia") + "Montar proposta com IA", "propostaDaConversa", c.id, "primario");
    if (prop) acoesIa += '<a class="btn suave" href="#/propostas/' + prop.id + '">' + ic("doc") + "Proposta " + prop.id + "</a>";
    if (cliente && cliente.id) { var ct = st.contratos.filter(function (k) { return k.clienteId === cliente.id; }).pop(); if (ct) acoesIa += '<a class="btn suave" href="#/contratos/' + ct.id + '">' + ic("doc") + "Contrato " + ct.id + "</a>"; }
    if (c.ia) acoesIa += botao(ic("ia") + "Rascunhar resposta", "rascunhar", c.id, acoesIa ? "suave" : "primario");
    return '<header class="conversa-topo"><a class="voltar" href="#/contatos">' + ic("voltar") + "Contatos</a>" +
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

  // Proposta: documento em papel à esquerda, edição e o e-mail da IA à direita.
  Telas.propostas = function (id) {
    var p = por(st.propostas, id);
    if (!p) { setTimeout(function () { location.hash = "#/contatos"; }); return ""; }
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
        (conv ? '<a class="btn suave" href="#/conversa/' + conv.id + '">' + ic(CANAL[conv.canal].i) + "Abrir conversa</a>" : "") + "</section>";
    } else {
      lado = '<section class="painel"><h2>Situação</h2><p>' + pilula(p.status) + "</p>" + (contrato ? '<p class="dica">Virou o contrato ' + contrato.id + ".</p>" : "") + "</section>";
    }
    return cabeca("Proposta " + p.id + ' <span class="versao">v' + p.versao + "</span>", esc(lead.empresa) + " · válida até " + data(p.validade) + " · " + pilula(p.status), acoes, ["#/contatos", "Contatos"]) +
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
    if (!id) return listaContratos();
    if (!c) { setTimeout(function () { location.hash = "#/contratos"; }); return ""; }
    var cli = U.clienteDe(c);
    var total = soma(c.etapas, function (e) { return e.valor; });
    var pago = soma(c.etapas.filter(function (e) { return e.status === "Paga"; }), function (e) { return e.valor; });
    var acao = c.status === "AguardandoAssinatura" ? botao("Cancelar", "cancelarContrato", c.id, "suave perigo") + botao(ic("check") + "Assinatura recebida", "assinar", c.id, "primario") : "";
    var linha = c.eventos.map(function (e) { return { em: e.em, texto: e.texto, canal: "sistema" }; })
      .concat(st.atividades.filter(function (a) { return a.alvo && a.alvo.indexOf("/" + c.id) >= 0; }))
      .sort(function (a, b) { return a.em < b.em ? 1 : a.em > b.em ? -1 : 0; });
    return cabeca("Contrato " + c.id, '<a href="#/contratos">' + esc(cli.nome) + '</a> · proposta <a href="#/propostas/' + c.propostaId + '">' + c.propostaId + "</a> · " + pilula(c.status), acao, ["#/contratos", "Contratos"]) +
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
  function listaContratos() {
    var abertas = st.propostas.filter(function (p) { return p.status === "Rascunho" || p.status === "Enviada"; });
    var lemb = st.lembretes.filter(function (m) { return m.status === "Pendente"; }).sort(function (a, c) { return a.quando < c.quando ? -1 : 1; });
    return cabeca("Contratos", "Proposta aceita vira contrato. Cada etapa aprovada pede a NFS-e e gera a cobrança.") +
      '<section class="painel"><h2>Contratos</h2><ul class="linhas">' + st.contratos.slice().reverse().map(function (c) {
        var cli = U.clienteDe(c), total = soma(c.etapas, function (e) { return e.valor; });
        var pago = soma(c.etapas.filter(function (e) { return e.status === "Paga"; }), function (e) { return e.valor; });
        var semNota = c.etapas.filter(function (e) { return (e.status === "Aprovada" || e.status === "Paga") && !e.notaId; }).length;
        return '<li><span class="avatar pequeno">' + iniciais(cli.nome) + '</span><div class="linha-texto"><strong><a href="#/contratos/' + c.id + '">' + c.id + " · " + esc(cli.nome) + "</a></strong><small>" + c.etapas.length + " etapa(s) · recebido " + brl(pago) + " de " + brl(total) +
          (semNota ? ' · <span class="txt-erro">' + semNota + " sem NFS-e</span>" : "") + '</small></div><div class="linha-valor">' + pilula(c.status) + '</div><div class="acoes"><a class="btn suave" href="#/contratos/' + c.id + '">Abrir' + ic("seta") + "</a></div></li>";
      }).join("") + "</ul></section>" +
      '<div class="grade-duas"><section class="painel"><h2>Propostas abertas</h2>' + (abertas.length ? '<ul class="linhas">' + abertas.map(function (p) {
        return '<li class="sem-avatar"><div class="linha-texto"><strong><a href="#/propostas/' + p.id + '">' + p.id + " · " + esc(U.leadDe(p).empresa) + "</a></strong><small>v" + p.versao + " · válida até " + data(p.validade) + '</small></div><div class="linha-valor"><b>' + brl(U.totalProposta(p)) + "</b>" + pilula(p.status) + "</div></li>";
      }).join("") + "</ul>" : '<div class="vazio"><p>Nenhuma proposta aberta.</p></div>') + "</section>" +
      '<section class="painel"><h2>Lembretes</h2>' + (lemb.length ? '<ul class="agenda">' + lemb.map(function (m) {
        return '<li><a href="' + esc(m.alvo) + '">' + esc(m.texto) + "</a>" + prazo(m.quando) + botao(ic("check") + '<span class="sr">Concluir: ' + esc(m.texto) + "</span>", "concluirLembrete", m.id, "icone") + "</li>";
      }).join("") + "</ul>" : '<div class="vazio"><p>Nenhum lembrete pendente.</p></div>') + "</section></div>";
  }

  // Contas: entra, sai e notas, em cartões de linha com a ação ao lado.
  var abaFin = "receber";
  Telas.contas = function () {
    var ar = Q.aReceber(), ap = Q.aPagar(), pn = Q.pendentesNota();
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
    return cabeca("Contas", "O que entra, o que sai e as notas. A cobrança nasce quando o cliente aprova a etapa.") +
      '<div class="faixa-numeros">' +
        numero("A receber", brl(soma(ar, function (r) { return r.valor; })), ar.length + " parcela(s)", "#/contas") +
        numero("A pagar", brl(soma(ap, function (g) { return g.valor; })), ap.length + " conta(s)", "#/contas") +
        numero("Notas pendentes", String(pn.length), pn.length ? "travam recebimento" : "tudo em dia", "#/contas") +
      "</div>" +
      '<section class="painel"><div class="abas" role="tablist" aria-label="Lançamentos">' + Object.keys(abas).map(function (k) {
        return '<button type="button" role="tab" id="aba-' + k + '" data-aba="' + k + '" aria-selected="' + (abaFin === k) + '" tabindex="' + (abaFin === k ? 0 : -1) + '">' + abas[k] + "</button>";
      }).join("") + '</div><div role="tabpanel" aria-labelledby="aba-' + abaFin + '">' + corpo + "</div></section>";
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

  // ---------- roteamento e render ----------
  function rota() {
    var partes = (location.hash || "#/conteudo").replace(/^#\//, "").split("/");
    return { nome: Telas[partes[0]] ? partes[0] : "conteudo", id: partes[1] || null };
  }
  var ATIVO = { propostas: "contatos", conversa: "contatos" };
  function contador() {
    var n = st.conversas.filter(function (c) { return c.naoLida; }).length +
      st.interacoes.filter(function (i) { return i.status === "Nova" && i.ia.intencao !== "Sem intenção comercial"; }).length;
    var el = document.getElementById("contador-contatos");
    el.hidden = !n; el.textContent = n; el.setAttribute("aria-label", n + " novidade(s)");
  }
  function render(focar) {
    var r = rota(), main = document.getElementById("conteudo");
    main.innerHTML = Telas[r.nome](r.id);
    main.setAttribute("data-tela", r.nome);
    var ativo = ATIVO[r.nome] || r.nome;
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
    return Promise.resolve(resultado).then(function (m) { F.salvar(); render(location.hash !== antes); if (m) avisar(m); });
  }

  function executar(acao, id) {
    if (CONFIRMAR[acao]) {
      var c = CONFIRMAR[acao];
      return confirmar(c[0], c[1], c[2]).then(function (ok) { if (ok) executarJa(acao, id); });
    }
    executarJa(acao, id);
  }
  var POR_COLECAO = { qualificarLead: "leads", perderLead: "leads", reabrirLead: "leads", criarProposta: "leads", enviarProposta: "propostas", aceitarProposta: "propostas", recusarProposta: "propostas", novaVersao: "propostas",
    receber: "receber", cobrar: "receber", pagar: "pagar", concluirLembrete: "lembretes", cancelarNota: "notas", propostaDaConversa: "conversas",
    rascunharPost: "posts", aprovarPost: "posts", desagendar: "posts", publicarAgora: "posts", virarLead: "interacoes", ignorar: "interacoes" };
  function botoes(ok) { return '<div class="acoes direita"><button class="btn suave" type="button" data-fechar>Cancelar</button><button class="btn primario" type="submit">' + ok + "</button></div>"; }
  function executarJa(acao, id) {
    var ids = (id || "").split("|");
    if (acao === "restaurar") { st = F.restaurar(); abaFin = "receber"; if (location.hash === "#/conteudo") render(true); else location.hash = "#/conteudo"; avisar("Dados de exemplo restaurados."); return; }
    if (acao === "abrirNovoLead") return abrirDialogo('<form class="dialogo-corpo" data-form="lead" novalidate><h2>Novo lead</h2>' + campo("l-empresa", "Empresa", 'name="empresa" maxlength="80" autocomplete="off"') + campo("l-contato", "Contato", 'name="contato" maxlength="60" autocomplete="off"') + campo("l-interesse", "O que precisa", 'name="interesse" maxlength="120" autocomplete="off"') + botoes("Criar lead") + "</form>");
    if (acao === "abrirNovaConta") return abrirDialogo('<form class="dialogo-corpo" data-form="pagar" novalidate><h2>Nova conta a pagar</h2>' + campo("g-desc", "Descrição", 'name="descricao" maxlength="80" autocomplete="off"') + campo("g-valor", "Valor", 'name="valor" inputmode="decimal" placeholder="1.500,00"') + campo("g-venc", "Vencimento", 'name="vencimento" type="date" value="' + U.dias(10) + '"') + '<div class="campo"><label for="g-cat">Categoria</label><select id="g-cat" name="categoria"><option>Administrativo</option><option>Custo de projeto</option><option>Tributos</option><option>Ferramentas</option></select></div>' + botoes("Criar conta") + "</form>");
    if (acao === "abrirNovaIdeia") return abrirDialogo('<form class="dialogo-corpo" data-form="ideia" novalidate><h2>Nova ideia</h2>' + campo("i-titulo", "Título", 'name="titulo" maxlength="100" autocomplete="off" placeholder="Ex.: por que entrego protótipo antes do código"') +
      '<div class="campo"><label for="i-formato">Formato</label><select id="i-formato" name="formato"><option value="post">Post</option><option value="artigo">Artigo (vai também para o blog)</option><option value="carrossel">Carrossel</option><option value="video">Vídeo</option></select></div>' + botoes("Criar ideia") + "</form>");
    if (acao === "abrirAbordar") {
      var ld = por(st.leads, id), n = Q.abordagensHoje(), lim = Q.limiteDia(), cheio = n >= lim;
      var sug = "Oi " + ld.contato + ", tudo bem? Vi que vocês estão olhando " + ld.interesse.toLowerCase() + ". Eu trabalho por etapas pequenas, com protótipo antes do código. Faz sentido uma conversa de 15 minutos?";
      return abrirDialogo('<form class="dialogo-corpo" data-form="abordar" data-id="' + ld.id + '" novalidate><h2>Abordar ' + esc(ld.contato) + " · " + esc(ld.empresa) + "</h2>" +
        '<p class="contador-msg' + (cheio ? " cheio" : "") + '">' + n + " de " + lim + " mensagens hoje</p>" +
        '<div class="campo"><label for="ab-texto">Mensagem (rascunho da IA, edite à vontade)</label><textarea id="ab-texto" name="texto" rows="6" aria-describedby="ab-texto-erro">' + esc(sug) + '</textarea><small id="ab-texto-erro" class="erro-campo" role="alert" hidden></small></div>' +
        '<p class="dica">O sistema não envia nada. Copie e mande você mesmo pelo LinkedIn; aqui fica só o registro.</p>' + botoes(cheio ? "Limite de hoje atingido" : "Registrar envio") + "</form>");
    }
    if (acao === "rascunhar") { var cv = por(st.conversas, id), ta = document.getElementById("resposta"); if (ta && cv && cv.ia) escrever(ta, cv.ia.resposta); return; }
    if (acao === "rascunharPost") {
      var pr = por(st.posts, id), m0 = R.rascunharPost(pr);
      return concluir(m0).then(function () { var t2 = document.getElementById("texto-post"); if (t2) escrever(t2, pr.texto); });
    }
    if (acao === "aprovarPost") { var pa = por(st.posts, id), tp = document.getElementById("texto-post"); if (tp) R.salvarTexto(pa, tp.value); return concluir(R.aprovarPost(pa)); }
    var msg;
    if (POR_COLECAO[acao]) msg = R[acao](por(st[POR_COLECAO[acao]], id));
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
    var aba = ev.target.closest("[data-aba]"); if (aba) { abaFin = aba.getAttribute("data-aba"); render(); document.querySelector('[data-aba="' + abaFin + '"]').focus(); return; }
    if (ev.target.closest("[data-fechar]")) { dlg.close(); return; }
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
    var tipo = f.getAttribute("data-form"), v, res;
    if (tipo === "responder") {
      var conv = por(st.conversas, f.getAttribute("data-id")), txt = f.texto.value.trim();
      if (!txt) { f.texto.focus(); return; }
      return concluir(R.responder(conv, txt));
    }
    if (tipo === "ideia") {
      var tit = f.titulo.value.trim();
      if (!tit) return erroCampo(f, "titulo", "Dê um título à ideia.");
      var fmt = f.formato.value, np = R.criarPost({ titulo: tit, formato: fmt, redes: fmt === "artigo" ? ["linkedin", "blog"] : fmt === "post" ? ["linkedin"] : ["linkedin", "instagram"] });
      dlg.close(); F.salvar(); location.hash = "#/conteudo/" + np.id; avisar("Ideia criada. Peça o rascunho à IA."); return;
    }
    if (tipo === "texto") return concluir(R.salvarTexto(por(st.posts, f.getAttribute("data-id")), f.texto.value));
    if (tipo === "agendar") {
      var redesSel = Array.prototype.slice.call(f.querySelectorAll('input[name="redes"]:checked')).map(function (x) { return x.value; });
      res = R.agendarPost(por(st.posts, f.getAttribute("data-id")), { redes: redesSel, quando: f.quando.value });
      if (res && res.erro === "quando") return erroCampo(f, "quando", res.msg);
      if (res && res.erro === "redes") {
        var er = document.getElementById("a-redes-erro"); er.textContent = res.msg; er.hidden = false;
        f.querySelector("fieldset").setAttribute("aria-invalid", "true"); f.querySelector('input[name="redes"]').focus(); return;
      }
      if (res && res.erro) return avisar(res.msg);
      return concluir(res);
    }
    if (tipo === "abordar") {
      res = R.abordar(por(st.leads, f.getAttribute("data-id")), f.texto.value.trim());
      if (res && res.erro) return erroCampo(f, "texto", res.msg);
      dlg.close(); return concluir(res);
    }
    if (tipo === "lead") {
      var empresa = f.empresa.value.trim();
      if (!empresa) return erroCampo(f, "empresa", "Informe o nome da empresa.");
      R.criarLead({ empresa: empresa, contato: f.contato.value.trim(), interesse: f.interesse.value.trim() });
      dlg.close(); return concluir("Lead " + empresa + " criado.");
    }
    if (tipo === "pagar") {
      var desc = f.descricao.value.trim(); v = U.lerValor(f.valor.value);
      if (!desc) return erroCampo(f, "descricao", "Descreva a conta.");
      if (!(v > 0)) return erroCampo(f, "valor", "Use um valor maior que zero, como 1.500,00.");
      if (!f.vencimento.value) return erroCampo(f, "vencimento", "Informe o vencimento.");
      var mm = R.criarPagar({ descricao: desc, valor: v, vencimento: f.vencimento.value, categoria: f.categoria.value });
      dlg.close(); abaFin = "pagar";
      if (location.hash !== "#/contas") location.hash = "#/contas";
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
      res = R.anexarNota(c, e, { numero: f.numero.value.trim(), valor: U.lerValor(f.valor.value), retencoes: ret, competencia: f.competencia.value, xml: xml && xml.name, pdf: pdf && pdf.name });
      if (res && res.erro) return erroCampo(f, res.erro, res.msg);
      concluir(res);
    }
  });

  document.getElementById("restaurar").addEventListener("click", function () { executar("restaurar", ""); });
  window.addEventListener("hashchange", function () { if (/^#\//.test(location.hash) || !location.hash) render(true); });
  render();
})();
