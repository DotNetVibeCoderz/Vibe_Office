// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

window.officenet = {
    // Blazor Server cannot hand the browser a file directly: the bytes live on the server and the
    // circuit is a WebSocket, so a converted PDF has to cross as base64 and be turned back into a
    // Blob here.
    download(fileName, base64) {
        const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
        const url = URL.createObjectURL(new Blob([bytes], { type: "application/pdf" }));

        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();

        // Revoked on the next tick: revoking immediately races the download in Safari.
        setTimeout(() => URL.revokeObjectURL(url), 0);
    }
};
