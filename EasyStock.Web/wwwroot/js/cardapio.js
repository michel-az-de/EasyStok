/* cardapio.js — galeria + formulário da gestão de cardápio da vitrine (issue 633, Fase 4 ADR-0031).
 *
 * Cards são ilhas Alpine (cardapioCard) com estado próprio; toggles reconciliam com a VERDADE
 * do servidor (mitiga last-write-wins, já que CardapioItem não tem rowversion). Mutações passam
 * por window.api (CSRF automático). Upload é multipart com fetch dedicado (boundary do browser).
 */
(function () {
  'use strict';

  function csrf() {
    const m = document.querySelector('meta[name="csrf-token"]');
    return m ? m.getAttribute('content') : '';
  }
  function toast(msg, type) { if (window.showToast) window.showToast(msg, type || 'success'); }

  // POST multipart (upload / form com arquivo). Não força Content-Type: o browser põe o boundary.
  async function postForm(url, formData) {
    const res = await fetch(url, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'RequestVerificationToken': csrf(), 'Accept': 'application/json' },
      body: formData,
    });
    let json = null;
    try { json = await res.json(); } catch (_) { /* corpo não-JSON */ }
    return { ok: res.ok && !!json && json.ok !== false, status: res.status, json: json };
  }

  // ── Card da galeria ─────────────────────────────────────────────────────
  window.cardapioCard = function (init) {
    return {
      id: init.id,
      nome: init.nome,
      visivel: init.visivel,
      disponivel: init.disponivel,
      busy: false,

      async _post(path, body) {
        if (this.busy) return null;
        this.busy = true;
        try {
          const r = await window.api.post('/cardapio/' + this.id + '/' + path, body);
          if (!r || r.ok === false) { toast((r && r.erro) || 'Não foi possível agora.', 'error'); return null; }
          return r.data || {};
        } catch (e) {
          toast('Não foi possível agora. Tente de novo.', 'error');
          return null;
        } finally {
          this.busy = false;
        }
      },

      async togglePublicar() {
        const d = await this._post('publicar');
        if (!d) return;
        this.visivel = d.visivel;
        toast(this.visivel ? 'Publicado! Já aparece para os clientes.' : 'Voltou para rascunho. Só você vê agora.');
      },

      async toggleDisponivel() {
        const d = await this._post('disponivel');
        if (!d) return;
        this.disponivel = d.disponivel;
        toast(this.disponivel ? 'Disponível de novo.' : 'Marcado como esgotado. O cliente vê, mas não consegue pedir.');
      },

      remover() {
        const self = this;
        window.easyConfirm({
          titulo: 'Remover este item?',
          mensagem: '"' + self.nome + '" sai da sua vitrine na hora. Você pode adicionar de novo depois.',
          textoBotao: 'Remover',
        }, async function () {
          const d = await self._post('remover');
          if (!d) return;
          const card = self.$root;
          const grid = card.parentElement;
          card.remove();
          toast('Item removido do cardápio.');
          if (grid && grid.querySelectorAll('.cdp-card').length === 0) {
            const sec = grid.closest('.cdp-cat');
            if (sec) sec.remove();
          }
        });
      },

      async mover(dir) {
        const card = this.$root;
        const isCard = function (el) { return el && el.classList && el.classList.contains('cdp-card'); };
        const sib = dir === 'up' ? card.previousElementSibling : card.nextElementSibling;
        if (!isCard(sib)) return;
        const target = parseFloat(sib.dataset.ordem) || 0;
        const beyondEl = dir === 'up' ? sib.previousElementSibling : sib.nextElementSibling;
        const beyond = isCard(beyondEl) ? (parseFloat(beyondEl.dataset.ordem) || 0) : (dir === 'up' ? target - 2 : target + 2);
        const novaOrdem = (target + beyond) / 2;
        try {
          const r = await window.api.post('/cardapio/' + this.id + '/reordenar', { novaOrdem: novaOrdem });
          if (!r || r.ok === false) { toast('Não foi possível reordenar.', 'error'); return; }
          card.dataset.ordem = String(novaOrdem);
          if (dir === 'up') card.parentNode.insertBefore(card, sib);
          else card.parentNode.insertBefore(sib, card);
          const btn = card.querySelector('[data-move="' + dir + '"]');
          if (btn) btn.focus();
        } catch (e) {
          toast('Não foi possível reordenar.', 'error');
        }
      },
    };
  };

  // ── Formulário criar / editar ────────────────────────────────────────────
  window.cardapioForm = function (cfg) {
    return {
      modo: cfg.modo || 'avulso',
      ehEdicao: !!cfg.ehEdicao,
      itemId: cfg.itemId || null,
      actionUrl: cfg.actionUrl,
      produtoId: cfg.produtoId || '',
      produtoNome: cfg.produtoNome || '',
      fotoUrl: cfg.fotoUrl || '',
      publicar: !!cfg.publicar,
      descLen: cfg.descLen || 0,
      saving: false,
      erroNome: '',
      erroPreco: '',
      taQuery: '',
      taResults: [],
      taOpen: false,
      taActive: -1,
      vinculados: cfg.vinculados || [],
      $form: null,

      init(el) {
        this.$form = el;
        const self = this;
        // FormTagHelper descarta @submit.prevent — o handler vai via addEventListener (gotcha recorrente).
        el.addEventListener('submit', function (e) { e.preventDefault(); self.submit(); });
      },

      onDesc(e) { this.descLen = (e.target.value || '').length; },

      onPickFoto(e) {
        const file = e.target.files && e.target.files[0];
        if (!file) return;
        if (!/^image\/(jpeg|png|webp)$/.test(file.type)) { toast('Use uma imagem JPG, PNG ou WebP.', 'error'); e.target.value = ''; return; }
        if (file.size > 6 * 1024 * 1024) { toast('A imagem não pode ser maior que 6 MB.', 'error'); e.target.value = ''; return; }
        this.fotoUrl = URL.createObjectURL(file);
        if (this.ehEdicao && this.itemId) this._uploadFoto(file);
      },

      async _uploadFoto(file) {
        const fd = new FormData();
        fd.append('foto', file);
        const r = await postForm('/cardapio/' + this.itemId + '/foto', fd);
        if (r.ok && r.json && r.json.data && r.json.data.url) {
          this.fotoUrl = r.json.data.url;
          toast('Foto atualizada.');
        } else {
          toast((r.json && r.json.erro) || 'A foto não subiu. Tente de novo.', 'error');
        }
      },

      async taSearch() {
        const q = this.taQuery.trim();
        if (q.length < 2) { this.taResults = []; this.taOpen = false; return; }
        try {
          const res = await fetch('/cardapio/buscar-produtos?termo=' + encodeURIComponent(q), {
            credentials: 'same-origin', headers: { 'Accept': 'application/json' },
          });
          const arr = await res.json();
          const self = this;
          this.taResults = (arr || []).filter(function (p) { return !self.vinculados.includes(String(p.id)); });
          this.taOpen = this.taResults.length > 0;
          this.taActive = -1;
        } catch (e) { this.taResults = []; this.taOpen = false; }
      },

      taPick(p) {
        this.produtoId = p.id;
        this.produtoNome = p.nome;
        this.taQuery = p.nome;
        this.taOpen = false;
      },

      taKey(e) {
        if (!this.taOpen) return;
        if (e.key === 'ArrowDown') { e.preventDefault(); this.taActive = Math.min(this.taActive + 1, this.taResults.length - 1); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); this.taActive = Math.max(this.taActive - 1, 0); }
        else if (e.key === 'Enter') { if (this.taActive >= 0) { e.preventDefault(); this.taPick(this.taResults[this.taActive]); } }
        else if (e.key === 'Escape') { this.taOpen = false; }
      },

      _validate() {
        this.erroNome = '';
        this.erroPreco = '';
        let ok = true;
        if (this.modo === 'avulso') {
          const nomeEl = this.$form.querySelector('[name="NomePublico"]');
          const precoEl = this.$form.querySelector('[name="PrecoStorefront"]');
          if (!nomeEl || !(nomeEl.value || '').trim()) { this.erroNome = 'Dê um nome ao item para continuar.'; ok = false; }
          const preco = parseFloat(((precoEl && precoEl.value) || '').replace(',', '.'));
          if (!(preco > 0)) { this.erroPreco = 'Informe um preço maior que zero.'; ok = false; }
        } else if (!this.produtoId) {
          toast('Escolha um produto do estoque.', 'error');
          ok = false;
        }
        return ok;
      },

      async submit() {
        if (this.saving) return;
        if (!this._validate()) return;
        this.saving = true;
        try {
          const fd = new FormData(this.$form);
          const r = await postForm(this.actionUrl, fd);
          if (!r.ok) {
            toast((r.json && r.json.erro) || 'Não foi possível salvar agora.', 'error');
            this.saving = false;
            return;
          }
          const data = (r.json && r.json.data) || {};
          if (data.avisoFoto) toast(data.avisoFoto, 'warning');
          else toast(this.ehEdicao ? 'Alterações salvas.' : 'Item salvo no cardápio.');
          window.location.href = '/cardapio';
        } catch (e) {
          toast('Não foi possível salvar agora. Tente de novo.', 'error');
          this.saving = false;
        }
      },
    };
  };
})();
