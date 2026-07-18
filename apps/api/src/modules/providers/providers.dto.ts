export type ProviderStatusDto = {
  id: number;
  platform: string;
  accountId: string | null;
  status: "active" | "paused";
  apiKey: string | null;
};

export type ProvidersResponseDto = {
  providers: ProviderStatusDto[];
};

export type ProviderUpdateResponseDto = {
  provider: ProviderStatusDto;
};

export type PlayniteKeyResponseDto = {
  apiKey: string;
};
