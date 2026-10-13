import "@fontsource-variable/space-grotesk";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { isDesktop } from "./host/desktop";
import "./nav/input";
import { inputMode } from "./nav/store";
import "./styles/tokens.css";
import "./styles/shell.css";
import { loadTheme } from "./theme";

// Desktop mode starts mouse first (no controller hints until a pad or the arrow keys are used); Big Picture pad first.
inputMode.set(isDesktop ? "pointer" : "pad");
document.documentElement.dataset.input = inputMode.get();
document.documentElement.dataset.mode = isDesktop ? "desktop" : "fse";

void loadTheme().then(() =>
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  ),
);
