// TypeScript port of the `playdex.drafts.*` interop from wwwroot/js/playdex.js.

export const supportedImageTypes: readonly string[] = ["image/png", "image/jpeg", "image/webp"];

export function draftTimezone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
}

export function validateImage(url: string, maxPixels: number): Promise<boolean> {
  return new Promise((resolve) => {
    const image = new Image();
    image.onload = () =>
      resolve(image.naturalWidth > 0 && image.naturalHeight > 0 && image.naturalWidth * image.naturalHeight <= maxPixels);
    image.onerror = () => resolve(false);
    image.src = url;
  });
}

export function readAsDataUrl(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error ?? new Error("The file could not be read."));
    reader.readAsDataURL(file);
  });
}

export interface PasteCallbacks {
  pasteImage: (dataUrl: string, mediaType: string) => void;
  pasteError: (error: string) => void;
}

/** Listens for pasted screenshots on `element`; returns the detach function. */
export function attachPaste(
  element: HTMLElement,
  callbacks: PasteCallbacks,
  maxBytes: number,
  maxPixels: number,
): () => void {
  const handler = (event: ClipboardEvent) => {
    const file = [...(event.clipboardData?.files ?? [])][0];
    if (!file) return;
    event.preventDefault();
    if (!supportedImageTypes.includes(file.type) || file.size > maxBytes) {
      callbacks.pasteError("Choose a PNG, JPEG or WebP screenshot within the size limit.");
      return;
    }
    const reader = new FileReader();
    reader.onload = async () => {
      const result = String(reader.result);
      if (!(await validateImage(result, maxPixels))) {
        callbacks.pasteError("The image is invalid or exceeds the pixel limit.");
        return;
      }
      callbacks.pasteImage(result, file.type);
    };
    reader.readAsDataURL(file);
  };
  element.addEventListener("paste", handler);
  return () => element.removeEventListener("paste", handler);
}

/** `Convert.FromBase64String` for the payload of a data URL. */
export function dataUrlBytes(dataUrl: string): Uint8Array<ArrayBuffer> {
  const base64 = dataUrl.slice(dataUrl.indexOf(",") + 1);
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

/**
 * Shortens a file name in the middle so the start and the extension stay visible:
 * "IMG_20250914_183022_HDR_portrait_edited_edited.webp" → "IMG_20250914_183022_HDR_…ed_edited.webp".
 * Works on code points so emoji and other astral characters are never split.
 */
export function middleTruncate(name: string, max: number): string {
  const chars = Array.from(name);
  if (chars.length <= max) return name;
  const dot = name.lastIndexOf(".");
  const extension = dot > 0 && name.length - dot <= 6 ? Array.from(name.slice(dot)) : [];
  const base = chars.slice(0, chars.length - extension.length);
  const keep = Math.max(2, max - extension.length - 1);
  const head = Math.ceil(keep * 0.6);
  return [...base.slice(0, head), "…", ...base.slice(base.length - (keep - head)), ...extension].join("");
}
