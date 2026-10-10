const token = (name) => `rgb(var(--${name}) / <alpha-value>)`;

module.exports = {
  content: ["./index.html", "./src/**/*.{ts,tsx}", "./landing/index.html", "./landing/**/*.tsx"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        ink: { DEFAULT: token("ink"), 2: token("ink-2"), 3: token("ink-3") },
        muted: token("muted"),
        faint: { DEFAULT: token("faint"), 2: token("faint-2") },
        page: token("page"),
        surface: token("surface"),
        tile: token("tile"),
        track: token("track"),
        divider: token("divider"),
        line: token("line"),
        dash: token("dash"),
        accent: token("accent"),
        brand: { DEFAULT: token("brand"), hover: token("brand-hover") },
        pill: { DEFAULT: token("pill"), ink: token("pill-ink") },
        bar: { DEFAULT: token("bar"), light: token("bar-light") },
        mint: { DEFAULT: token("mint"), line: token("mint-line"), hover: token("mint-hover") },
        danger: { DEFAULT: token("danger"), icon: token("danger-icon"), line: token("danger-line"), tint: token("danger-tint") },
        warn: token("warn"),
        info: token("info"),
        footer: "#121a22",
      },
      fontFamily: {
        sans: ['"Space Grotesk"', '"Segoe UI"', "system-ui", "sans-serif"],
      },
      borderRadius: {
        card: "32px",
        tile: "24px",
      },
      boxShadow: {
        card: "var(--card-shadow)",
        cover: "var(--cover-shadow)",
        pop: "var(--pop-shadow)",
      },
    },
  },
  plugins: [],
};
