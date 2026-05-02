/* bulk.js — Alpine factory para bulk actions em tabelas.
 *
 * Uso:
 *   <table x-data="bulkTable()" x-init="init()">
 *     <thead><tr>
 *       <th><input type="checkbox" @change="toggleAll($event)" :checked="allChecked"></th>
 *       <th>Nome</th>
 *     </tr></thead>
 *     <tbody>
 *       <tr><td><input type="checkbox" :value="123" @change="toggle($event)"></td><td>...</td></tr>
 *     </tbody>
 *   </table>
 *   <div class="bulk-bar" x-show="selected.length > 0" x-cloak>
 *     <span x-text="selected.length + ' selecionado(s)'"></span>
 *     <button @click="clear()" class="btn btn-ghost btn-sm">Cancelar</button>
 *   </div>
 */
window.bulkTable = function () {
    return {
        selected: [],
        get allChecked() {
            const inputs = this.$el.querySelectorAll('tbody input[type=checkbox]');
            return inputs.length > 0 && this.selected.length === inputs.length;
        },
        init() { /* hook */ },
        toggle(e) {
            const v = e.target.value;
            if (e.target.checked) {
                if (!this.selected.includes(v)) this.selected.push(v);
            } else {
                this.selected = this.selected.filter(x => x !== v);
            }
        },
        toggleAll(e) {
            const inputs = this.$el.querySelectorAll('tbody input[type=checkbox]');
            this.selected = e.target.checked ? Array.from(inputs).map(i => i.value) : [];
            inputs.forEach(i => { i.checked = e.target.checked; });
        },
        clear() {
            this.selected = [];
            this.$el.querySelectorAll('input[type=checkbox]').forEach(i => i.checked = false);
        }
    };
};
