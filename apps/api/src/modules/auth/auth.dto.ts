export type LoginResponseDto = {
  userId: number;
  email: string;
  isAdmin: boolean;
  accessToken: string;
  refreshToken: string;
};

export type RefreshResponseDto = {
  accessToken: string;
  refreshToken: string;
};

export type MeResponseDto = {
  userId: number;
  email: string;
  isAdmin: boolean;
};
