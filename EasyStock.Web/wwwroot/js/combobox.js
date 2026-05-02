/* combobox.js — Alpine factory para searchable select acessível (ARIA combobox).
 * Uso:
 *   <div x-data="combobox({ items: [{value:1,label:'Foo'}], name: 'cat' })">...</div>
 */
window.combobox = function (config) {
    return {
        items: config.items || [],
        filtered: [],
        query: '',
        selected: config.value ?? null,
        open: false,
        activeIdx: 0,
        name: config.name || 'combobox',
        placeholder: config.placeholder || 'Selecionar...',
        init() { this.filtered = this.items.slice(); this._syncLabel(); },
        _syncLabel() {
            const sel = this.items.find(i => String(i.value) === String(this.selected));
            this.query = sel ? sel.label : '';
        },
        onInput() {
            const q = (this.query || '').toLowerCase();
            this.filtered = this.items.filter(i => i.label.toLowerCase().includes(q));
            this.open = true; this.activeIdx = 0;
        },
        onFocus() { this.filtered = this.items.slice(); this.open = true; },
        onBlur() { setTimeout(() => { this.open = false; this._syncLabel(); }, 120); },
        onKey(e) {
            if (!this.open && (e.key === 'ArrowDown' || e.key === 'Enter')) { this.open = true; e.preventDefault(); return; }
            if (e.key === 'ArrowDown') { this.activeIdx = Math.min(this.activeIdx + 1, this.filtered.length - 1); e.preventDefault(); }
            else if (e.key === 'ArrowUp') { this.activeIdx = Math.max(this.activeIdx - 1, 0); e.preventDefault(); }
            else if (e.key === 'Enter') { if (this.filtered[this.activeIdx]) { this.pick(this.filtered[this.activeIdx]); e.preventDefault(); } }
            else if (e.key === 'Escape') { this.open = false; }
        },
        pick(item) { this.selected = item.value; this.query = item.label; this.open = false; this.$dispatch('change', { value: item.value }); }
    };
};
