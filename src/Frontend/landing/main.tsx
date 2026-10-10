/* playdex.dev: the landing on its own, a static site on Cloudflare Pages. No API and no sign-in here;
   everything app-bound links to dash.playdex.dev, where the app (src/main.tsx) runs. */
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import { LandingPage } from "@/pages/landing/LandingPage";
import { PrivacyPage } from "@/pages/privacy/PrivacyPage";
import "@/styles/app.css";

createRoot(document.getElementById("app")!).render(
  <StrictMode>
    <BrowserRouter>
      <ThemeProvider>
        <Routes>
          <Route path="/" element={<LandingPage />} />
          <Route path="/privacy" element={<PrivacyPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </ThemeProvider>
    </BrowserRouter>
  </StrictMode>,
);
