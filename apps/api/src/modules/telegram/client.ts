type TelegramApiResponse<T> = {
  ok: boolean;
  result?: T;
  description?: string;
};

export type TelegramMessage = {
  message_id: number;
  text?: string;
  chat: {
    id: number;
  };
};

export type TelegramUpdate = {
  update_id: number;
  message?: TelegramMessage;
};

const buildTelegramUrl = (token: string, method: string) =>
  `https://api.telegram.org/bot${token}/${method}`;

export const sendTelegramMessage = async (
  token: string,
  chatId: string,
  text: string
) => {
  const response = await fetch(buildTelegramUrl(token, "sendMessage"), {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ chat_id: chatId, text }),
  });

  if (!response.ok) {
    throw new Error(
      `Telegram sendMessage failed: ${response.status} ${response.statusText}`
    );
  }

  const payload =
    (await response.json()) as TelegramApiResponse<TelegramMessage>;
  if (!payload.ok) {
    throw new Error(payload.description ?? "Telegram sendMessage failed.");
  }
};

export const sendTelegramImage = async (
  token: string,
  chatId: string,
  image: Uint8Array,
  filename = "notification.webp",
  caption?: string
) => {
  const form = new FormData();
  form.append("chat_id", chatId);
  if (caption) {
    form.append("caption", caption);
  }
  form.append(
    "photo",
    new Blob([image], { type: "image/webp" }),
    filename
  );

  const response = await fetch(buildTelegramUrl(token, "sendPhoto"), {
    method: "POST",
    body: form,
  });

  if (!response.ok) {
    throw new Error(
      `Telegram sendPhoto failed: ${response.status} ${response.statusText}`
    );
  }

  const payload =
    (await response.json()) as TelegramApiResponse<TelegramMessage>;
  if (!payload.ok) {
    throw new Error(payload.description ?? "Telegram sendPhoto failed.");
  }
};

export const fetchTelegramUpdates = async (
  token: string,
  offset: number
): Promise<TelegramUpdate[]> => {
  const url = new URL(buildTelegramUrl(token, "getUpdates"));
  url.searchParams.set("timeout", "25");
  if (offset > 0) {
    url.searchParams.set("offset", String(offset));
  }

  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(
      `Telegram getUpdates failed: ${response.status} ${response.statusText}`
    );
  }

  const payload = (await response.json()) as TelegramApiResponse<
    TelegramUpdate[]
  >;
  if (!payload.ok) {
    throw new Error(payload.description ?? "Telegram getUpdates failed.");
  }

  return payload.result ?? [];
};
