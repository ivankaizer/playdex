(() => {
    const playdex = window.playdex = {};

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
