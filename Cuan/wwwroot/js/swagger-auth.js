(function () {
    const storageKey = "cuan-swagger-api-key";

    const getDefaultKey = () => window.__DEFAULT_API_KEY || "";
    const getHeaderName = () => window.__API_KEY_HEADER || "X-Api-Key";

    const getStoredKey = () => localStorage.getItem(storageKey) || "";
    const setStoredKey = (value) => localStorage.setItem(storageKey, value);

    const ensurePanel = () => {
        if (document.getElementById("swagger-api-key-panel")) return;

        const panel = document.createElement("div");
        panel.id = "swagger-api-key-panel";
        panel.style.cssText = "position:fixed;top:10px;right:10px;z-index:9999;background:#fff;border:1px solid #ddd;padding:10px;border-radius:8px;box-shadow:0 2px 6px rgba(0,0,0,0.1);font-family:Arial;font-size:12px;";

        panel.innerHTML = `
            <div style="font-weight:700;margin-bottom:6px;">API Key</div>
            <input id="swagger-api-key-input" type="password" style="width:220px;padding:6px;border:1px solid #ccc;border-radius:6px;" placeholder="Masukkan API Key" />
            <div style="margin-top:6px;display:flex;gap:6px;">
                <button id="swagger-api-key-save" style="padding:5px 8px;border:0;background:#6366f1;color:#fff;border-radius:6px;cursor:pointer;">Simpan</button>
                <button id="swagger-api-key-clear" style="padding:5px 8px;border:0;background:#e5e7eb;color:#111;border-radius:6px;cursor:pointer;">Reset</button>
            </div>
            <div id="swagger-api-key-status" style="margin-top:6px;color:#6b7280;"></div>
        `;

        document.body.appendChild(panel);

        const input = document.getElementById("swagger-api-key-input");
        const status = document.getElementById("swagger-api-key-status");
        const saveBtn = document.getElementById("swagger-api-key-save");
        const clearBtn = document.getElementById("swagger-api-key-clear");

        const applyKey = (key) => {
            const headerName = getHeaderName();
            window.__SWAGGER_API_KEY = key;
            status.textContent = key ? `Header ${headerName} siap digunakan` : "API Key kosong";
        };

        const stored = getStoredKey() || getDefaultKey();
        if (stored) {
            input.value = stored;
            setStoredKey(stored);
            applyKey(stored);
        } else {
            applyKey("");
        }

        saveBtn.addEventListener("click", () => {
            const value = input.value.trim();
            setStoredKey(value);
            applyKey(value);
        });

        clearBtn.addEventListener("click", () => {
            input.value = "";
            setStoredKey("");
            applyKey("");
        });
    };

    const patchFetch = () => {
        if (window.__swaggerFetchPatched) return;
        window.__swaggerFetchPatched = true;

        const originalFetch = window.fetch.bind(window);
        window.fetch = (input, init = {}) => {
            const key = window.__SWAGGER_API_KEY || getStoredKey() || getDefaultKey();
            if (key) {
                init.headers = init.headers || {};
                init.headers[getHeaderName()] = key;
            }
            return originalFetch(input, init);
        };
    };

    const boot = () => {
        ensurePanel();
        patchFetch();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", boot);
    } else {
        boot();
    }
})();
