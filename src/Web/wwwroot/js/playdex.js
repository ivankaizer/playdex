(() => {
    const playdex = window.playdex = {};

    const draftPasteHandlers = new Map();
    playdex.drafts = {
        timezone: () => Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
        validateImage: (url, maxPixels) => new Promise(resolve => {
            const image = new Image();
            image.onload = () => resolve(image.naturalWidth > 0 && image.naturalHeight > 0 && image.naturalWidth * image.naturalHeight <= maxPixels);
            image.onerror = () => resolve(false);
            image.src = url;
        }),
        detachPaste: id => {
            const existing = draftPasteHandlers.get(id);
            if (existing) existing.element.removeEventListener("paste", existing.handler);
            draftPasteHandlers.delete(id);
        },
        attachPaste: (id, dotnet, maxBytes, maxPixels) => {
            const element = document.getElementById(id);
            if (!element || draftPasteHandlers.get(id)?.element === element) return;
            playdex.drafts.detachPaste(id);
            const handler = async event => {
                const file = [...(event.clipboardData?.files || [])][0];
                if (!file) return;
                event.preventDefault();
                if (!["image/png", "image/jpeg", "image/webp"].includes(file.type) || file.size > maxBytes) {
                    await dotnet.invokeMethodAsync("PasteError", "Choose a PNG, JPEG or WebP screenshot within the size limit.");
                    return;
                }
                const reader = new FileReader();
                reader.onload = async () => {
                    if (!await playdex.drafts.validateImage(reader.result, maxPixels)) {
                        await dotnet.invokeMethodAsync("PasteError", "The image is invalid or exceeds the pixel limit.");
                        return;
                    }
                    await dotnet.invokeMethodAsync("PasteImage", reader.result, file.type);
                };
                reader.readAsDataURL(file);
            };
            element.addEventListener("paste", handler);
            draftPasteHandlers.set(id, { element, handler });
        }
    };

    playdex.storage = {
        get: key => localStorage.getItem(key),
        set: (key, value) => localStorage.setItem(key, value),
        remove: key => localStorage.removeItem(key),
    };

    playdex.applyTheme = theme => {
        document.documentElement.classList.toggle("dark", theme === "dark");
        document.documentElement.dataset.theme = theme;
    };

    playdex.prefersDark = () =>
        typeof window.matchMedia === "function" &&
        window.matchMedia("(prefers-color-scheme: dark)").matches;

    playdex.confirm = message => window.confirm(message);
    playdex.focus = id => document.getElementById(id)?.focus();
    playdex.scrollToTop = () => window.scrollTo({ top: 0, behavior: "smooth" });
})();
