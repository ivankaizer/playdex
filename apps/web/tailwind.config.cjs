module.exports = {
  content: [
    "./src/**/*.{ts,tsx}",
    "./App.razor",
    "./Pages/**/*.{razor,cs}",
    "./Components/**/*.{razor,cs}",
    "./Layout/**/*.{razor,cs}",
    "./wwwroot/{index.html,js/*.js}",
  ],
  darkMode: "class",
  theme: {
    extend: {},
  },
  plugins: [],
};
