import React from "react";
import satori from "satori";
import { Resvg } from "@resvg/resvg-js";
import sharp from "sharp";

export type NotificationImageKind = "started" | "ended";

type NotificationImageInput = {
  title: string;
  platform: string;
  image: string;
  kind: NotificationImageKind;
  duration?: string;
  fontUrl?: string;
};

const DEFAULT_FONT_URL =
  "https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600;700&display=swap";

const IMAGE_WIDTH = 1280;
const IMAGE_HEIGHT = 1024;
const isRemoteSource = (source: string) =>
  source.startsWith("http://") || source.startsWith("https://");

const resolveMimeType = (source: string, contentType?: string | null) => {
  if (contentType?.startsWith("image/")) {
    return contentType.split(";")[0];
  }

  const lower = source.toLowerCase();
  if (lower.endsWith(".jpg") || lower.endsWith(".jpeg")) {
    return "image/jpeg";
  }
  if (lower.endsWith(".webp")) {
    return "image/webp";
  }
  if (lower.endsWith(".gif")) {
    return "image/gif";
  }
  return "image/png";
};

const loadImageDataUrl = async (source: string) => {
  if (isRemoteSource(source)) {
    const response = await fetch(source);
    if (!response.ok) {
      throw new Error(
        `Unable to load image from ${source}: ${response.status} ${response.statusText}`
      );
    }
    const buffer = await response.arrayBuffer();
    const mimeType = resolveMimeType(source, response.headers.get("content-type"));
    const base64 = Buffer.from(buffer).toString("base64");
    return `data:${mimeType};base64,${base64}`;
  }

  const file = Bun.file(source);
  if (!(await file.exists())) {
    throw new Error(`Image file not found: ${source}`);
  }

  const buffer = await file.arrayBuffer();
  const mimeType = resolveMimeType(source, file.type);
  const base64 = Buffer.from(buffer).toString("base64");
  return `data:${mimeType};base64,${base64}`;
};

const buildNotificationTree = ({
  title,
  platform,
  image,
  kind,
  duration,
}: NotificationImageInput) => {
  const headline = kind === "ended" ? "Session ended" : "Session started";

  return React.createElement(
    "div",
    {
      style: {
        width: IMAGE_WIDTH,
        height: IMAGE_HEIGHT,
        display: "flex",
        position: "relative",
        overflow: "hidden",
        boxSizing: "border-box",
        padding: "0px",
        backgroundColor: "transparent",
        color: "#f7f7f2",
        fontFamily: "System",
      },
    },
    React.createElement(
      "div",
      {
        style: {
          position: "relative",
          display: "flex",
          width: "100%",
          height: "100%",
          overflow: "hidden",
        },
      },
      React.createElement("img", {
        src: image,
        style: {
          position: "absolute",
          inset: 0,
          width: "100%",
          height: "100%",
          objectFit: "cover",
        },
      }),
      React.createElement("div", {
        style: {
          position: "absolute",
          inset: 0,
          backgroundColor: "rgba(10, 13, 18, 0.72)",
        },
      }),
      React.createElement(
        "div",
        {
          style: {
            display: "flex",
            flexDirection: "column",
            justifyContent: "flex-end",
            height: "100%",
            padding: "64px",
            gap: "18px",
          },
        },
        React.createElement(
          "div",
          {
            style: {
              display: "flex",
              flexDirection: "column",
              gap: "18px",
              padding: "28px 32px",
              borderRadius: "24px",
              backgroundColor: "rgba(9, 12, 18, 0.78)",
              border: "1px solid rgba(255, 255, 255, 0.08)",
              width: "100%",
            },
          },
          React.createElement(
            "div",
            {
              style: {
                alignSelf: "flex-start",
                padding: "8px 16px",
                borderRadius: "999px",
                border: "1px solid rgba(249, 216, 74, 0.45)",
                backgroundColor: "rgba(249, 216, 74, 0.12)",
                color: "#f9d84a",
                fontSize: "24px",
                fontWeight: 600,
                letterSpacing: "0.5px",
                textTransform: "uppercase",
              },
            },
            headline
          ),
          React.createElement(
            "div",
            {
              style: {
                fontSize: "60px",
                fontWeight: 700,
                lineHeight: 1.1,
                textShadow: "0 8px 16px rgba(0, 0, 0, 0.45)",
              },
            },
            title
          ),
          React.createElement(
            "div",
            {
              style: {
                fontSize: "28px",
                opacity: 0.82,
              },
            },
            `Platform: ${platform}`
          ),
          kind === "ended" && duration
            ? React.createElement(
                "div",
                {
                  style: {
                    fontSize: "28px",
                    opacity: 0.82,
                  },
                },
                `Duration: ${duration}`
              )
            : null
        )
      )
    )
  );
};

type FontDefinition = {
  name: string;
  data: ArrayBuffer;
  weight: number;
  style: "normal" | "italic";
};

const fontCache = new Map<string, FontDefinition[]>();

const parseFontFaces = (css: string) => {
  const blocks = css.match(/@font-face\s*{[^}]*}/g) ?? [];
  return blocks
    .map((block) => {
      const familyMatch = block.match(/font-family:\s*([^;]+);/i);
      const weightMatch = block.match(/font-weight:\s*([^;]+);/i);
      const styleMatch = block.match(/font-style:\s*([^;]+);/i);
      const urlMatch = block.match(/url\(([^)]+)\)/i);

      const family = familyMatch
        ? familyMatch[1].trim().replace(/^["']|["']$/g, "")
        : "Custom";
      const weightRaw = weightMatch?.[1].trim() ?? "400";
      const weight = Number.parseInt(weightRaw, 10) || 400;
      const style =
        styleMatch?.[1].trim().toLowerCase() === "italic" ? "italic" : "normal";
      const url = urlMatch
        ? urlMatch[1].trim().replace(/^["']|["']$/g, "")
        : null;

      if (!url) {
        return null;
      }

      return { family, weight, style, url };
    })
    .filter((entry): entry is NonNullable<typeof entry> => Boolean(entry));
};

const loadFontData = async (fontUrl: string): Promise<FontDefinition[]> => {
  const cached = fontCache.get(fontUrl);
  if (cached) {
    return cached;
  }

  const response = await fetch(fontUrl);
  if (!response.ok) {
    throw new Error(
      `Unable to load font from ${fontUrl}: ${response.status} ${response.statusText}`
    );
  }

  if (fontUrl.endsWith(".css") || fontUrl.includes("fonts.googleapis.com")) {
    const css = await response.text();
    const faces = parseFontFaces(css);
    if (faces.length === 0) {
      throw new Error("No @font-face rules found in font CSS.");
    }

    const fonts = await Promise.all(
      faces.map(async (face) => {
        const fontResponse = await fetch(face.url);
        if (!fontResponse.ok) {
          throw new Error(
            `Unable to load font file ${face.url}: ${fontResponse.status} ${fontResponse.statusText}`
          );
        }

        return {
          name: face.family,
          data: await fontResponse.arrayBuffer(),
          weight: face.weight,
          style: face.style,
        };
      })
    );

    fontCache.set(fontUrl, fonts);
    return fonts;
  }

  const fonts = [
    {
      name: "Custom",
      data: await response.arrayBuffer(),
      weight: 400,
      style: "normal",
    },
  ];
  fontCache.set(fontUrl, fonts);
  return fonts;
};

export const renderNotificationWebp = async (
  input: NotificationImageInput
) => {
  const [imageDataUrl, fonts] = await Promise.all([
    loadImageDataUrl(input.image),
    loadFontData(input.fontUrl ?? DEFAULT_FONT_URL),
  ]);

  const svg = await satori(
    buildNotificationTree({ ...input, image: imageDataUrl }),
    {
      width: IMAGE_WIDTH,
      height: IMAGE_HEIGHT,
      fonts,
    }
  );

  const resvg = new Resvg(svg, {
    fitTo: {
      mode: "width",
      value: IMAGE_WIDTH,
    },
  });

  const pngBuffer = resvg.render().asPng();
  const webpBuffer = await sharp(pngBuffer)
    .webp({ quality: 92 })
    .toBuffer();

  return new Uint8Array(webpBuffer);
};
