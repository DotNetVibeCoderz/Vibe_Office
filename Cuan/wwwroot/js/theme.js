// Tema disimpan di perangkat masing-masing pengguna dan dipasang sebelum
// Blazor terhubung, supaya halaman tidak berkedip putih dulu saat dimuat.
(function () {
    try {
        var saved = localStorage.getItem('cuan-theme');
        if (saved === 'dark' || saved === 'light') {
            document.documentElement.setAttribute('data-theme', saved);
        }
    } catch (e) { /* localStorage diblokir — pakai tema bawaan */ }
})();

window.cuanTheme = {
    get: function () {
        try { return localStorage.getItem('cuan-theme') || ''; } catch (e) { return ''; }
    },
    set: function (theme) {
        document.documentElement.setAttribute('data-theme', theme);
        try { localStorage.setItem('cuan-theme', theme); } catch (e) { }
        return theme;
    },
    apply: function (fallback) {
        var saved = window.cuanTheme.get();
        var theme = saved || fallback || 'light';
        document.documentElement.setAttribute('data-theme', theme);
        return theme;
    }
};

window.cuanConfirm = function (message) {
    return window.confirm(message);
};

window.cuanPrint = function () {
    window.print();
};
