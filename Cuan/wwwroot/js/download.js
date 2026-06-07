// Helper untuk download file dari byte[] (base64) di Blazor
// Dipanggil dari C# via IJSRuntime: downloadFileFromBytes(fileName, contentType, base64)
window.downloadFileFromBytes = (fileName, contentType, base64Data) => {
    const link = document.createElement('a');
    link.download = fileName;
    link.href = `data:${contentType};base64,${base64Data}`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
