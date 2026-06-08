// EasyStok format.js — espelho JS de FormatHelper.cs.
// Expoe window.fmt com: money, date, dateTime, relativeDate, quantity, plural, pluralWord.
(function (window) {
    'use strict';

    const moneyFmt = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
    const dateFmt = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' });
    const numFmt = new Intl.NumberFormat('pt-BR');

    function toDate(d) {
        if (d == null) return null;
        if (d instanceof Date) return d;
        const parsed = new Date(d);
        return isNaN(parsed.getTime()) ? null : parsed;
    }

    function pad2(n) { return String(n).padStart(2, '0'); }

    function asDateTime(d) {
        const date = toDate(d);
        if (!date) return '';
        return `${pad2(date.getDate())}/${pad2(date.getMonth() + 1)}/${date.getFullYear()} às ${pad2(date.getHours())}:${pad2(date.getMinutes())}`;
    }

    function asRelative(d, now) {
        const date = toDate(d);
        if (!date) return '';
        const ref = now ? toDate(now) : new Date();
        const diffSec = (ref - date) / 1000;
        if (diffSec < 0) return asDateTime(date);
        if (diffSec < 60 * 60) {
            const min = Math.max(1, Math.floor(diffSec / 60));
            return `há ${min} min`;
        }
        if (diffSec < 24 * 60 * 60) {
            const h = Math.floor(diffSec / 3600);
            return h === 1 ? 'há 1 hora' : `há ${h} horas`;
        }
        if (diffSec < 7 * 24 * 60 * 60) {
            const dd = Math.floor(diffSec / 86400);
            return dd === 1 ? 'há 1 dia' : `há ${dd} dias`;
        }
        return dateFmt.format(date);
    }

    function asQuantity(value, unit) {
        if (value == null) return '';
        unit = unit == null ? 'un' : unit;
        const num = Number.isInteger(value)
            ? numFmt.format(value)
            : (Math.round(value * 1000) / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 3 });
        return unit ? `${num} ${unit}` : num;
    }

    window.fmt = {
        money: v => (v == null ? '' : moneyFmt.format(v)),
        date: d => {
            const x = toDate(d);
            return x ? dateFmt.format(x) : '';
        },
        dateTime: asDateTime,
        relativeDate: asRelative,
        quantity: asQuantity,
        // Pluralizacao pt-BR (espelho de TextHelpers.Plural no C#). plural(1,'item','itens') => '1 item'.
        plural: (n, sing, plur) => n + ' ' + (n === 1 ? sing : plur),
        pluralWord: (n, sing, plur) => (n === 1 ? sing : plur),
    };
})(window);
