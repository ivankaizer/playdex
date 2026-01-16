type IgdbAuthResponse = {
  access_token: string;
  expires_in: number;
};

type IgdbGame = {
  id: number;
  name: string;
  cover?: { image_id?: string };
  platforms?: { id?: number; name?: string }[];
};

type IgdbSearchResult = {
  id: string;
  title: string;
  platform: string | null;
  platformId: number | null;
  coverUrl: string | null;
};

type IgdbClientConfig = {
  clientId: string;
  clientSecret: string;
};

let cachedToken: { value: string; expiresAt: number } | null = null;

const ensureIgdbToken = async ({
  clientId,
  clientSecret,
}: IgdbClientConfig) => {
  if (cachedToken && cachedToken.expiresAt > Date.now()) {
    return cachedToken.value;
  }

  const url = new URL("https://id.twitch.tv/oauth2/token");
  url.searchParams.set("client_id", clientId);
  url.searchParams.set("client_secret", clientSecret);
  url.searchParams.set("grant_type", "client_credentials");

  const response = await fetch(url, { method: "POST" });
  if (!response.ok) {
    throw new Error(`IGDB auth failed: ${response.status}`);
  }

  const payload = (await response.json()) as IgdbAuthResponse;
  if (!payload.access_token) {
    throw new Error("IGDB auth response missing access_token.");
  }

  const expiresAt = Date.now() + payload.expires_in * 1000 - 30_000;
  cachedToken = { value: payload.access_token, expiresAt };
  return payload.access_token;
};

const formatIgdbCover = (imageId?: string) => {
  if (!imageId) {
    return null;
  }
  return `https://images.igdb.com/igdb/image/upload/t_cover_big/${imageId}.jpg`;
};

export const searchIgdbGames = async (
  query: string,
  config: IgdbClientConfig
): Promise<IgdbSearchResult[]> => {
  const trimmed = query.trim();
  if (!trimmed) {
    return [];
  }

  const token = await ensureIgdbToken(config);
  const response = await fetch("https://api.igdb.com/v4/games", {
    method: "POST",
    headers: {
      "Client-ID": config.clientId,
      Authorization: `Bearer ${token}`,
      "Content-Type": "text/plain",
    },
    body: `search "${trimmed.replace(/"/g, "")}"; fields id,name,cover.image_id,platforms.id,platforms.name; limit 10;`,
  });

  if (!response.ok) {
    throw new Error(`IGDB search failed: ${response.status}`);
  }

  const payload = (await response.json()) as IgdbGame[];
  return payload.map((game) => {
    const platformInfo = game.platforms?.find((platform) => platform?.id);
    return {
      id: String(game.id),
      title: game.name,
      platform: platformInfo?.name ?? null,
      platformId: platformInfo?.id ?? null,
      coverUrl: formatIgdbCover(game.cover?.image_id),
    };
  });
};
