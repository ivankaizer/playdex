/** The app lives on its own host; the landing never talks to the API and only links there. */
export const dashUrl = (import.meta.env.VITE_DASH_URL as string | undefined) ?? "https://dash.playdex.dev";
