import { expect, test } from "bun:test";
import { renderToStaticMarkup } from "react-dom/server";

import { AppFooter } from "../src/components/AppFooter";

test("renders an industry-standard Playdex footer without navigation", () => {
  const markup = renderToStaticMarkup(<AppFooter />);

  expect(markup).toContain("Playdex");
  expect(markup).toContain("rounded-3xl");
  expect(markup).toContain("bg-white/90");
  expect(markup).toContain(`© ${new Date().getFullYear()}`);
  expect(markup).toContain("Playtime tracking for connected libraries.");
  expect(markup).not.toContain("<nav");
});
