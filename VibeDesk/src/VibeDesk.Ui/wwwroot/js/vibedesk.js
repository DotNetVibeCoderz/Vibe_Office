/*
 * Browser-side helpers. Deliberately small: anything that can be done in Blazor is, and JS is
 * reserved for the four things it cannot reach — theme persistence before first paint, contenteditable
 * selection, file download, and focus/scroll control.
 */
window.vibedesk = (function () {
  'use strict';

  const THEME_KEY = 'vibedesk.theme';

  /* ── theme ───────────────────────────────────────────────────────────────
     "system" removes the attribute so prefers-color-scheme takes over; the token sheet is written
     to expect exactly that. Called from an inline script in <head> too, so the correct theme is
     applied before the first paint and the page never flashes the wrong one. */
  function setTheme(theme) {
    const root = document.documentElement;

    if (theme === 'light' || theme === 'dark') {
      root.setAttribute('data-theme', theme);
    } else {
      root.removeAttribute('data-theme');
    }

    try {
      localStorage.setItem(THEME_KEY, theme || 'system');
    } catch {
      /* Private browsing can reject storage; the theme still applies for this page. */
    }
  }

  function getTheme() {
    try {
      return localStorage.getItem(THEME_KEY) || 'system';
    } catch {
      return 'system';
    }
  }

  /* ── focus & scroll ──────────────────────────────────────────────────── */
  function focus(element) {
    if (element && typeof element.focus === 'function') {
      element.focus({ preventScroll: false });
    }
  }

  function focusSelector(selector) {
    const el = document.querySelector(selector);
    if (el) el.focus();
  }

  function selectAll(element) {
    if (element && typeof element.select === 'function') element.select();
  }

  function scrollToBottom(element) {
    if (!element) return;
    element.scrollTop = element.scrollHeight;
  }

  function scrollIntoView(element, block) {
    if (!element) return;
    element.scrollIntoView({ block: block || 'nearest', inline: 'nearest', behavior: 'smooth' });
  }

  /* ── contenteditable ─────────────────────────────────────────────────────
     The Docs editor is a contenteditable surface, so formatting goes through execCommand. It is
     deprecated but remains the only API every browser implements for rich-text editing; the
     alternative is hand-rolling selection and DOM surgery, which is a much larger correctness risk. */
  function execCommand(command, value) {
    try {
      document.execCommand(command, false, value ?? null);
      return true;
    } catch {
      return false;
    }
  }

  function getHtml(element) {
    return element ? element.innerHTML : '';
  }

  function setHtml(element, html) {
    if (!element) return;
    /* Only write when different: assigning innerHTML collapses the caret to the start, which would
       make the editor unusable if it happened on every render. */
    if (element.innerHTML !== html) element.innerHTML = html;
  }

  /* Sets a data-* attribute without touching the element's children — the placeholder toggle uses
     this because rendering a child inside a contenteditable would make it a Blazor diff target. */
  function setDataAttribute(element, name, value) {
    if (!element) return;
    if (value === null || value === undefined) element.removeAttribute('data-' + name);
    else element.setAttribute('data-' + name, value);
  }

  function getSelectedText() {
    const selection = window.getSelection();
    return selection ? selection.toString() : '';
  }

  /* Inserts HTML at the caret, used for links, images and accepted suggestions. */
  function insertHtml(html) {
    return execCommand('insertHTML', html);
  }

  function queryCommandState(command) {
    try {
      return document.queryCommandState(command);
    } catch {
      return false;
    }
  }

  /* ── download ────────────────────────────────────────────────────────────
     Used for exports the server generates as bytes. */
  function downloadBytes(fileName, base64, contentType) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

    const blob = new Blob([bytes], { type: contentType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);

    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    /* Revoke on the next tick: revoking synchronously can cancel the download in some browsers. */
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  async function copyText(text) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      return false;
    }
  }

  /* ── presentation ────────────────────────────────────────────────────── */
  async function requestFullscreen(element) {
    const target = element || document.documentElement;
    try {
      if (target.requestFullscreen) await target.requestFullscreen();
      return true;
    } catch {
      return false;
    }
  }

  async function exitFullscreen() {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
    } catch {
      /* Already exited. */
    }
  }

  /* ── keyboard shortcuts ──────────────────────────────────────────────────
     One document-level listener that forwards a small allow-list to .NET, rather than a Blazor
     @onkeydown on every surface. Registered once; re-registering replaces the previous handler. */
  let shortcutHandler = null;

  function registerShortcuts(dotNetRef) {
    if (shortcutHandler) document.removeEventListener('keydown', shortcutHandler);

    shortcutHandler = function (e) {
      const mod = e.ctrlKey || e.metaKey;
      if (!mod) return;

      const key = e.key.toLowerCase();
      const handled = ['k', 's', 'b', 'i', 'u', '/'];
      if (!handled.includes(key)) return;

      /* Ctrl+B/I/U inside the rich-text editor belong to the editor, not the shell. */
      const editing = document.activeElement?.isContentEditable;
      if (editing && ['b', 'i', 'u'].includes(key)) return;

      e.preventDefault();
      dotNetRef.invokeMethodAsync('OnShortcut', key, e.shiftKey);
    };

    document.addEventListener('keydown', shortcutHandler);
  }

  /* Measures an element so grids can virtualise against the real viewport height. */
  function measure(element) {
    if (!element) return null;
    const r = element.getBoundingClientRect();
    return { width: r.width, height: r.height, top: r.top, left: r.left };
  }

  /* Applies the saved theme immediately — called from <head> before Blazor starts. */
  setTheme(getTheme());

  return {
    setTheme,
    getTheme,
    focus,
    focusSelector,
    selectAll,
    scrollToBottom,
    scrollIntoView,
    execCommand,
    getHtml,
    setHtml,
    setDataAttribute,
    getSelectedText,
    insertHtml,
    queryCommandState,
    downloadBytes,
    copyText,
    requestFullscreen,
    exitFullscreen,
    registerShortcuts,
    measure,
  };
})();

/*
 * Syntax highlighting for the script editor.
 *
 * Hand-written rather than a library: the app has no bundler, and a CDN script would be one more
 * network dependency on a page that is otherwise entirely self-hosted. Three languages and a few
 * hundred bytes of regex covers what a script editor actually needs — keywords, strings, comments,
 * numbers — and nothing here has to understand the grammar.
 *
 * Strings and comments are matched first and their contents are never re-scanned, so a keyword
 * inside a string stays a string. That single ordering rule is what separates a highlighter that
 * works from one that mangles quoted code.
 */
window.vibedeskHighlight = (function () {
    const KEYWORDS = {
        javascript: 'var let const function return if else for while do break continue new delete ' +
            'typeof instanceof this null undefined true false try catch finally throw switch case ' +
            'default in of class extends super async await yield void',
        python: 'def class return if elif else for while break continue import from as pass raise ' +
            'try except finally with lambda None True False and or not in is global nonlocal yield ' +
            'assert del async await',
        csharp: 'var new return if else for foreach while do break continue class struct record ' +
            'interface enum public private protected internal static readonly const void int long ' +
            'double decimal bool string object true false null this base try catch finally throw ' +
            'switch case default using namespace async await yield in out ref params get set is as'
    };

    function escapeHtml(text) {
        return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function rulesFor(language) {
        const lang = (language || 'javascript').toLowerCase();
        const words = (KEYWORDS[lang] || KEYWORDS.javascript).split(' ').join('|');

        // Order matters: comments and strings must win over everything they contain.
        const rules = [];

        if (lang === 'python') {
            rules.push({ cls: 'str', re: /("""[\s\S]*?"""|'''[\s\S]*?''')/ });
            rules.push({ cls: 'com', re: /(#[^\n]*)/ });
        } else {
            rules.push({ cls: 'com', re: /(\/\*[\s\S]*?\*\/|\/\/[^\n]*)/ });
        }

        rules.push({ cls: 'str', re: /("(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)*'|`(?:[^`\\]|\\.)*`)/ });
        rules.push({ cls: 'num', re: /\b(\d+\.?\d*)\b/ });
        rules.push({ cls: 'kw', re: new RegExp('\\b(' + words + ')\\b') });
        rules.push({ cls: 'api', re: /\b(api|Api|console|input|Input|Log|result)\b/ });

        return rules;
    }

    return function (code, language) {
        const rules = rulesFor(language);

        // Each rule's source carries exactly one capture group, so joining them un-wrapped makes
        // group N correspond to rule N-1. Wrapping them again would nest the groups and make the
        // mapping depend on counting parentheses — which is how a highlighter starts colouring the
        // wrong token after someone edits a rule.
        const combined = new RegExp(rules.map(r => r.re.source).join('|'), 'g');

        let html = '';
        let last = 0;
        let match;

        while ((match = combined.exec(code)) !== null) {
            html += escapeHtml(code.slice(last, match.index));

            let cls = null;
            for (let i = 0; i < rules.length; i++) {
                if (match[i + 1] !== undefined) { cls = rules[i].cls; break; }
            }

            html += cls
                ? '<span class="vd-tok-' + cls + '">' + escapeHtml(match[0]) + '</span>'
                : escapeHtml(match[0]);

            last = combined.lastIndex;

            // A zero-length match would spin forever.
            if (match.index === combined.lastIndex) combined.lastIndex++;
        }

        html += escapeHtml(code.slice(last));
        return html;
    };
})();

/* Paints the highlight layer and keeps it aligned with the textarea it sits behind. */
window.vibedeskEditor = {
    paint: function (textareaId, layerId, language) {
        const input = document.getElementById(textareaId);
        const layer = document.getElementById(layerId);
        if (!input || !layer) return;

        // The trailing newline keeps the last line visible when the caret is on it.
        layer.innerHTML = window.vibedeskHighlight(input.value + '\n', language);
        layer.scrollTop = input.scrollTop;
        layer.scrollLeft = input.scrollLeft;
    },

    attach: function (textareaId, layerId, language) {
        const input = document.getElementById(textareaId);
        const layer = document.getElementById(layerId);
        if (!input || !layer || input.dataset.vdAttached === '1') return;

        input.dataset.vdAttached = '1';

        const repaint = () => window.vibedeskEditor.paint(textareaId, layerId, input.dataset.vdLang || language);
        input.addEventListener('input', repaint);
        input.addEventListener('scroll', () => {
            layer.scrollTop = input.scrollTop;
            layer.scrollLeft = input.scrollLeft;
        });

        // Tab indents instead of leaving the editor — the single most missed key in a code box.
        input.addEventListener('keydown', function (e) {
            if (e.key !== 'Tab') return;
            e.preventDefault();

            const start = input.selectionStart;
            const end = input.selectionEnd;
            input.value = input.value.slice(0, start) + '    ' + input.value.slice(end);
            input.selectionStart = input.selectionEnd = start + 4;

            input.dispatchEvent(new Event('input', { bubbles: true }));
        });

        repaint();
    },

    setLanguage: function (textareaId, layerId, language) {
        const input = document.getElementById(textareaId);
        if (input) input.dataset.vdLang = language;
        window.vibedeskEditor.paint(textareaId, layerId, language);
    }
};
