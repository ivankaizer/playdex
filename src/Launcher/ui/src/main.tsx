import "@fontsource-variable/space-grotesk";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { isDesktop } from "./host/desktop";
import "./nav/input";
import "./styles/tokens.css";
import "./styles/shell.css";

document.documentElement.dataset.input = "pad";
document.documentElement.dataset.mode = isDesktop ? "desktop" : "fse";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
