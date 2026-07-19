/* Cockpit de Pedidos (issue 591) — store reativo da listagem (Views/Pedidos/Index).
 *
 * Semeado do SSR (<script id="pedidos-seed">) -> dirige KPIs + filtro client-side +
 * acoes AJAX (avancar status / registrar pagamento) SEM reload. Otimista com rollback;
 * GUARDA SINCRONA anti-double-click (protege o desconto de estoque no ->pronto);
 * servidor vence sempre na resposta (substitui o item pelo corpo retornado).
 *
 * Os <tr> sao renderizados pelo servidor (degrada sem JS: o <form> posta normal);
 * o Alpine so hidrata as celulas mutaveis (status/pagamento/acao) via `p.*`.
 */
(function () {
  'use strict';

  // Espelha StatusPedidoMapper (domínio) — manter em sincronia com StatusHelper.cs e
  // statusDisplayLabel (SSR em Pedidos/Index.cshtml). Issue #719 / QA v1.10 BUG-003/014.
  var STATUS_LABEL = {
    aguardando: 'Aguardando', preparando: 'Em preparo', pronto: 'Pronto', entregue: 'Entregue', cancelado: 'Cancelado',
    rascunho: 'Rascunho', aguardando_pagamento: 'Aguardando pagamento', aguardando_aprovacao_baba: 'Aguardando aprovação', aprovado_baba: 'Aprovado'
  };
  // Avanco operacional (forward-only). aprovado_baba entra na fila ERP em 'preparando'.
  // A aprovacao em si (aguardando_aprovacao_baba -> aprovado_baba) NAO vem por aqui: e a
  // acao dedicada `approve`, com lock/outbox/notificacao no backend (#862).
  var NEXT = { aguardando: 'preparando', preparando: 'pronto', pronto: 'entregue', aprovado_baba: 'preparando' };
  var NEXT_LABEL = { aguardando: 'Iniciar preparo', preparando: 'Marcar pronto', pronto: 'Confirmar entrega', aprovado_baba: 'Iniciar preparo' };
  // "Em aberto" = não-terminais operacionais. Inclui os pós-pagamento do storefront
  // (aprovacao_baba/aprovado_baba). rascunho/aguardando_pagamento são pré-operacionais
  // e ficam fora. Incluir aprovacao_baba corrige a subcontagem do BUG-009 (issue #719).
  var OPEN = ['aguardando', 'preparando', 'pronto', 'aguardando_aprovacao_baba', 'aprovado_baba'];
  // Pre-operacionais: pedido ainda nao entrou na fila de trabalho. Enquanto aqui a acao
  // NAO e receber pagamento (o cardapio guest nem cobra online) — e aprovar/recusar. Por
  // isso o botao "Receber" e o KPI "A receber" ficam suprimidos ate o pedido virar
  // operacional (aprovado_baba em diante). Corrige o pedido do cardapio "preso" (#862).
  var PRE_OPERACIONAL = ['rascunho', 'aguardando_pagamento', 'aguardando_aprovacao_baba'];
  // Motivos de recusa aceitos pela API (AprovacaoPedidoController / MotivoRecusa).
  var MOTIVOS_RECUSA = [
    { v: 'OPERACIONAL', label: 'Operacional (agenda/logística)' },
    { v: 'ESTOQUE_INSUFICIENTE', label: 'Estoque insuficiente' },
    { v: 'OUTRO', label: 'Outro' }
  ];

  // Invariante (issue #719): todo status de OPEN precisa ter rótulo em STATUS_LABEL.
  // Sem runner de teste no gate JS — esta asserção só fala no console em dev.
  OPEN.forEach(function (s) { console.assert(s in STATUS_LABEL, '[pedidos-cockpit] OPEN sem STATUS_LABEL: ' + s); });

  function token() {
    var el = document.querySelector('[name=__RequestVerificationToken]');
    return el ? el.value : '';
  }
  function money(v) {
    if (window.fmt && window.fmt.money) return window.fmt.money(v);
    return 'R$ ' + Number(v || 0).toFixed(2).replace('.', ',');
  }
  function toast(type, msg, cid) {
    if (window.showToast) window.showToast(msg, type, cid ? { correlationId: cid } : undefined);
  }

  window.pedidosCockpit = function () {
    var seed = [];
    try { seed = JSON.parse((document.getElementById('pedidos-seed') || {}).textContent || '[]'); } catch (e) { seed = []; }
    var items = {}, order = [];
    seed.forEach(function (p) { items[p.id] = Object.assign({ _justMoved: false }, p); order.push(p.id); });

    return {
      items: items,
      order: order,
      filter: '',                 // '', <status>, 'agendados', 'atrasados'
      _inflight: {},              // chave -> true (guarda sincrona)
      focusId: null,
      pay: { open: false, id: null, metodo: 'pix', valor: '', enviando: false },
      refuse: { open: false, id: null, motivo: 'OPERACIONAL', mensagem: '', enviando: false },
      motivosRecusa: MOTIVOS_RECUSA,
      announce: '',               // regiao aria-live (leitor de tela)

      init: function () {
        this.filter = (this.$root.dataset.initialFilter || '').trim();
        this.focusId = this.visibleIds()[0] || null;
        this._bindKeys();
      },

      // ── derivados / KPIs (sobre o conjunto inteiro carregado) ──
      get rows() { return this.order.map(function (id) { return this.items[id]; }, this); },
      visibleIds: function () {
        var self = this;
        return this.order.filter(function (id) { return self.matches(self.items[id]); });
      },
      get visibleCount() { return this.visibleIds().length; },
      get kpiAberto() { return this.rows.filter(function (p) { return OPEN.indexOf(p.status) >= 0; }).length; },
      get kpiPronto() { return this.rows.filter(function (p) { return p.status === 'pronto'; }).length; },
      get kpiAtrasado() { return this.rows.filter(function (p) { return p.isAtrasado; }).length; },
      // "Novos do cardapio" (issue 865): pedidos guest aguardando aprovacao da Baba.
      get kpiCardapio() { return this.rows.filter(function (p) { return p.status === 'aguardando_aprovacao_baba'; }).length; },
      get kpiReceber() { return this.rows.filter(function (p) { return p.status !== 'cancelado' && PRE_OPERACIONAL.indexOf(p.status) < 0; }).reduce(function (s, p) { return s + (p.pendente || 0); }, 0); },
      get kpiReceberFmt() { return money(this.kpiReceber); },

      // ── filtro client-side ──
      matches: function (p) {
        if (!p) return false;
        if (p._justMoved) return true;                 // linger pos-avanco
        if (this.filter === 'agendados') return p.isScheduled;
        if (this.filter === 'atrasados') return p.isAtrasado;
        if (this.filter === '') return true;
        // Bucket dedicado "Novos do cardapio" (issue 865): so aguardando_aprovacao_baba.
        // Antes esse status caia no filtro "aguardando" (BUG-014), misturado com a fila
        // ERP; agora tem tab propria — segue visivel (nao volta ao BUG-014) sem se misturar.
        if (this.filter === 'cardapio') return p.status === 'aguardando_aprovacao_baba';
        if (this.filter === 'aguardando') return p.status === 'aguardando';
        return p.status === this.filter;
      },
      isVisible: function (id) { return this.matches(this.items[id]); },
      setFilter: function (f) {
        // clicar o filtro ativo (nao-"Todos") desliga; "Todos" (f="") sempre limpa.
        this.filter = (this.filter === f && f !== '') ? '' : f;
        this.focusId = this.visibleIds()[0] || null;
      },
      isFilter: function (f) { return this.filter === f; },

      // ── labels / formatos ──
      statusLabel: function (s) { return STATUS_LABEL[s] || s; },
      nextStatus: function (s) { return NEXT[s] || ''; },
      nextLabel: function (s) { return NEXT_LABEL[s] || ''; },
      money: money,
      payText: function (p) {
        if (p.status === 'cancelado') return p.totalPago > 0 ? ('Pago · ' + money(p.totalPago)) : 'Sem cobrança';
        if (p.total === 0) return 'Sem cobrança'; // issue 689 / BUG-008b: total R$0 não é "Pendente"
        // issue 962 (BUG-002 do QA): excedente deliberado (#607) fica visivel na listagem,
        // nao so no Detail -- "Quitado" sozinho escondia que foi pago em dobro.
        if (p.quitado && p.excedente > 0) return 'Quitado · +' + money(p.excedente);
        if (p.quitado) return 'Quitado';
        if (p.totalPago > 0) return 'Parcial · ' + money(p.totalPago);
        if (p.status === 'entregue') return 'Verificar';
        return 'Pendente';
      },
      payClass: function (p) {
        if (p.status === 'cancelado') return 'stp-neutral';
        if (p.total === 0) return 'stp-neutral'; // issue 689 / BUG-008b
        if (p.quitado) return 'stp-ok';
        if (p.totalPago > 0) return 'stp-warn';
        if (p.status === 'entregue') return 'stp-neutral';
        return 'stp-warn';
      },
      // "Receber" so em pedido operacional: pre-operacional (aguardando aprovacao etc.)
      // ainda nao e recebivel; a acao ali e aprovar/recusar (#862).
      canPay: function (p) { return p.status !== 'cancelado' && (p.pendente || 0) > 0 && PRE_OPERACIONAL.indexOf(p.status) < 0; },
      needsAprovacao: function (p) { return !!p && p.status === 'aguardando_aprovacao_baba'; },
      isFocus: function (id) { return this.focusId === id; },

      // ── avancar status (AJAX, otimista, rollback) ──
      advance: function (id) {
        if (this._inflight[id]) return;                // GUARDA SINCRONA — 1a linha
        var it = this.items[id];
        var nxt = it && this.nextStatus(it.status);
        if (!it || !nxt) return;
        this._inflight[id] = true;
        var snap = Object.assign({}, it);
        it.status = nxt; it._justMoved = true;          // otimista
        var self = this;
        esFetch('/pedidos/' + id + '/status.json', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
          body: JSON.stringify({ status: nxt })
        }).then(function (res) {
          return res.json().catch(function () { return null; }).then(function (data) {
            if (!res.ok || !data || !data.success) {
              Object.assign(it, snap);                  // rollback
              toast('error', (data && data.error && data.error.message) || 'Não foi possível avançar o pedido.', data && data.correlationId);
              return;
            }
            Object.assign(it, data.pedido); it._justMoved = true;   // servidor vence
            toast('success', 'Pedido: ' + self.statusLabel(it.status) + (it.status === 'pronto' ? ' · estoque baixado' : '') + '.');
            self.announce = 'Pedido atualizado para ' + self.statusLabel(it.status) + '.';
            self._linger(id);
          });
        }).catch(function () {
          Object.assign(it, snap);                      // rede/offline -> rollback
          toast('error', 'Sem conexão — o pedido não avançou.');
        }).finally(function () { delete self._inflight[id]; });
      },
      _linger: function (id) {
        var self = this;
        setTimeout(function () { var it = self.items[id]; if (it) it._justMoved = false; }, 4000);
      },

      // ── aprovar/recusar pedido do cardapio (#862) ──
      // O cardapio guest chega em 'aguardando_aprovacao_baba'; a Baba confirma (aprovar ->
      // aprovado_baba, entra na fila) ou recusa (-> cancelado, com refund/notificacao no
      // backend). Reusa os endpoints prontos da API via BFF (.json). Otimista + rollback,
      // guarda sincrona anti-double-click, servidor vence na resposta.
      approve: function (id) {
        if (this._inflight[id]) return;                 // GUARDA SINCRONA
        var it = this.items[id];
        if (!it || !this.needsAprovacao(it)) return;
        this._inflight[id] = true;
        var snap = Object.assign({}, it);
        it.status = 'aprovado_baba'; it._justMoved = true;   // otimista
        var self = this;
        esFetch('/pedidos/' + id + '/aprovar.json', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
          body: JSON.stringify({})
        }).then(function (res) {
          return res.json().catch(function () { return null; }).then(function (data) {
            if (!res.ok || !data || !data.success) {
              Object.assign(it, snap);                  // rollback
              toast('error', (data && data.error && data.error.message) || 'Não foi possível aprovar o pedido.', data && data.correlationId);
              return;
            }
            Object.assign(it, data.pedido); it._justMoved = true;   // servidor vence
            toast('success', 'Pedido aprovado.');
            self.announce = 'Pedido aprovado.';
            self._linger(id);
          });
        }).catch(function () {
          Object.assign(it, snap);                      // rede/offline -> rollback
          toast('error', 'Sem conexão — o pedido não foi aprovado.');
        }).finally(function () { delete self._inflight[id]; });
      },
      openRefuse: function (id) {
        var it = this.items[id];
        if (!it || !this.needsAprovacao(it)) return;
        this.refuse = { open: true, id: id, motivo: 'OPERACIONAL', mensagem: '', enviando: false };
      },
      closeRefuse: function () { this.refuse.open = false; this.refuse.id = null; },
      refuseTarget: function () { return this.items[this.refuse.id] || {}; },
      submitRefuse: function () {
        var id = this.refuse.id;
        var key = 'refuse:' + id;
        if (!id || this._inflight[key]) return;          // single-flight
        var it = this.items[id];
        if (!it) return;
        // Recusa cancela o pedido — destrutiva: NAO faz otimista, aplica so na resposta.
        this._inflight[key] = true; this.refuse.enviando = true;
        var self = this;
        esFetch('/pedidos/' + id + '/recusar.json', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
          body: JSON.stringify({ motivo: this.refuse.motivo, mensagemCliente: (this.refuse.mensagem || '').trim() || null })
        }).then(function (res) {
          return res.json().catch(function () { return null; }).then(function (data) {
            if (!res.ok || !data || !data.success) {
              toast('error', (data && data.error && data.error.message) || 'Não foi possível recusar o pedido.', data && data.correlationId);
              return;
            }
            Object.assign(it, data.pedido); it._justMoved = true;   // servidor vence (status: cancelado)
            toast('success', 'Pedido recusado.');
            self.announce = 'Pedido recusado.';
            self.closeRefuse();
            self._linger(id);
          });
        }).catch(function () {
          toast('error', 'Sem conexão — o pedido não foi recusado.');
        }).finally(function () { delete self._inflight[key]; self.refuse.enviando = false; });
      },

      // ── pagamento inline (popover) ──
      openPay: function (id) {
        var it = this.items[id];
        if (!it) return;
        this.pay = { open: true, id: id, metodo: 'pix', valor: (it.pendente || 0).toFixed(2), enviando: false };
        var self = this;
        this.$nextTick(function () { if (self.$refs.payValor) self.$refs.payValor.focus(); });
      },
      closePay: function () { this.pay.open = false; this.pay.id = null; },
      payFill: function (kind) {
        var it = this.items[this.pay.id];
        if (!it) return;
        this.pay.valor = (kind === 'total' ? (it.total || 0) : (it.pendente || 0)).toFixed(2);
      },
      payTarget: function () { return this.items[this.pay.id] || {}; },
      submitPay: function () {
        var id = this.pay.id;
        var key = 'pay:' + id;
        if (!id || this._inflight[key]) return;          // single-flight
        var it = this.items[id];
        var valor = parseFloat(String(this.pay.valor).replace(',', '.'));
        if (!(valor > 0)) { toast('error', 'Informe um valor maior que zero.'); return; }
        if (valor > (it.pendente || 0) + 0.005) { toast('error', 'Valor maior que o pendente (' + money(it.pendente) + ').'); return; }
        this._inflight[key] = true; this.pay.enviando = true;
        var snap = Object.assign({}, it);
        var self = this;
        esFetch('/pedidos/' + id + '/pagamentos.json', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
          body: JSON.stringify({ metodo: this.pay.metodo, valor: valor })
        }).then(function (res) {
          return res.json().catch(function () { return null; }).then(function (data) {
            if (!res.ok || !data || !data.success) {
              toast('error', (data && data.error && data.error.message) || 'Não foi possível registrar o pagamento.', data && data.correlationId);
              return;
            }
            Object.assign(it, data.pedido);              // servidor vence
            toast('success', 'Pagamento de ' + money(valor) + ' registrado.');
            self.announce = 'Pagamento de ' + money(valor) + ' registrado.';
            self.closePay();
          });
        }).catch(function () {
          Object.assign(it, snap);
          toast('error', 'Sem conexão — pagamento não registrado.');
        }).finally(function () { delete self._inflight[key]; self.pay.enviando = false; });
      },

      // ── teclado (balcao desktop) ──
      _bindKeys: function () {
        var self = this;
        document.addEventListener('keydown', function (e) {
          var tag = (document.activeElement && document.activeElement.tagName) || '';
          var typing = /^(INPUT|SELECT|TEXTAREA)$/.test(tag);
          if (self.refuse.open) { if (e.key === 'Escape') { e.preventDefault(); self.closeRefuse(); } return; }
          if (self.pay.open) { if (e.key === 'Escape') { e.preventDefault(); self.closePay(); } return; }
          if (e.key === '/' && !typing) { e.preventDefault(); var s = document.querySelector('input[type=search]'); if (s) s.focus(); return; }
          if ((e.key === 'n' || e.key === 'N') && !typing) {
            e.preventDefault(); var b = document.querySelector('[data-cockpit-novo]'); if (b) b.click(); return;
          }
          if (typing) return;
          var vis = self.visibleIds();
          if (!vis.length) return;
          if (e.key === 'ArrowDown' || e.key === 'j') { e.preventDefault(); self._moveFocus(vis, 1); }
          else if (e.key === 'ArrowUp' || e.key === 'k') { e.preventDefault(); self._moveFocus(vis, -1); }
          else if ((e.key === 'Enter' || e.key === ' ') && self.focusId) {
            if (e.repeat) return;                         // Enter segurado nao re-dispara
            e.preventDefault(); self.advance(self.focusId);
          } else if ((e.key === 'p' || e.key === 'P') && self.focusId) {
            e.preventDefault(); var it = self.items[self.focusId]; if (it && self.canPay(it)) self.openPay(self.focusId);
          }
        });
      },
      _moveFocus: function (vis, dir) {
        var i = vis.indexOf(this.focusId);
        i = (i < 0) ? 0 : Math.min(vis.length - 1, Math.max(0, i + dir));
        this.focusId = vis[i];
        var row = document.getElementById('row-' + this.focusId);
        if (row) row.scrollIntoView({ block: 'nearest' });
      }
    };
  };
})();
