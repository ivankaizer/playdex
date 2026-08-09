module.exports = {
  content: [
    "./App.razor",
    "./Infrastructure/**/*.{razor,cs}",
    "./Layout/**/*.{razor,cs}",
    "./Modules/**/*.{razor,cs}",
    "./Routing/**/*.{razor,cs}",
    "./wwwroot/{index.html,js/*.js}",
  ],
  darkMode: "class",
  theme: {
    extend: {},
  },
  plugins: [],
};
