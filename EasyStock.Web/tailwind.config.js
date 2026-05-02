/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './Views/**/*.cshtml',
    './Pages/**/*.cshtml',
    './wwwroot/js/**/*.js',
  ],
  darkMode: ['class', '[data-theme="dark"]'],
  // Safelist: classes geradas dinamicamente em código C#/Razor que o purge não detecta.
  safelist: [
    'theme-dark',
    { pattern: /^(bg|text|border)-(navy|orange|primary|accent)-(50|100|200|400|500|600|700|800|900)$/ },
  ],
  theme: {
    extend: {
      colors: {
        primary:        '#1E48A8',
        'primary-hover':'#0E2A6E',
        'primary-light':'#EDF1F9',
        accent:         '#E85814',
        'accent-hover': '#BF4307',
        sidebar:        '#06143A',
        'sidebar-text': 'rgba(255,255,255,.74)',
        'sidebar-active':'#F26B25',

        // Paleta DS completa
        navy: {
          900: '#06143A', 800: '#0A1F52', 700: '#0E2A6E',
          600: '#15388A', 500: '#1E48A8',
          200: '#BCC8E5', 100: '#DDE4F2', 50: '#EDF1F9',
        },
        orange: {
          700: '#BF4307', 600: '#E85814', 500: '#F26B25', 400: '#F8884A',
          200: '#FAC8AB', 100: '#FCDCC8', 50: '#FEEFE3',
        },
        ink: {
          900: '#0A1530', 800: '#122042', 700: '#2A3556', 600: '#4A5470',
          500: '#707892', 400: '#98A0B4', 300: '#C2C8D6',
          200: '#DEE2EB', 100: '#ECEFF5', 50: '#F5F7FB',
        },
        ok:   { 700: '#0E6B3D', 600: '#18874E', 100: '#D6EFDF', 50: '#ECF7F0' },
        warn: { 700: '#8A5A00', 600: '#B57A00', 100: '#FBEBC4', 50: '#FCF5E0' },
        crit: { 700: '#8E2A1E', 600: '#C03B2A', 100: '#F8D8D2', 50: '#FBEAE6' },

        // Surfaces theme-aware via tokens.css
        surface:     'var(--bg-surface)',
        elevated:    'var(--bg-elevated)',
        'app-bg':    'var(--bg-app)',
      },
      fontFamily: {
        sans:    ['Inter', 'system-ui', '-apple-system', 'sans-serif'],
        display: ['Fraunces', 'Inter', 'serif'],
        serif:   ['Fraunces', 'serif'],
        mono:    ['"JetBrains Mono"', 'ui-monospace', 'SF Mono', 'monospace'],
      },
      boxShadow: {
        'es-sm': 'var(--shadow-sm)',
        'es-md': 'var(--shadow-md)',
        'es-lg': 'var(--shadow-lg)',
      },
      borderRadius: {
        'es-sm':   'var(--r-sm)',
        'es-md':   'var(--r-md)',
        'es-lg':   'var(--r-lg)',
        'es-pill': 'var(--r-pill)',
      },
    },
  },
  plugins: [],
};
