import type {
  ColumnType,
  Generated,
  Insertable,
  Selectable,
  Updateable,
} from "kysely";

export type DbBigInt = string;
export type DbBigIntInput = DbBigInt | number;
export type DbTimestampInput = Date | string;

// Postgres.js returns bigint columns as strings to avoid lossy number conversion.
export type GeneratedDbBigInt = ColumnType<
  DbBigInt,
  DbBigIntInput | undefined,
  never
>;
export type DbBigIntColumn = ColumnType<DbBigInt, DbBigIntInput, DbBigIntInput>;
export type NullableDbBigIntColumn = ColumnType<
  DbBigInt | null,
  DbBigIntInput | null | undefined,
  DbBigIntInput | null
>;
export type CreatedAtColumn = ColumnType<
  Date,
  DbTimestampInput | undefined,
  never
>;
export type UpdatedAtColumn = ColumnType<
  Date,
  DbTimestampInput | undefined,
  DbTimestampInput
>;
export type NullableColumn<Value> = ColumnType<
  Value | null,
  Value | null | undefined,
  Value | null
>;

export type JsonPrimitive = boolean | number | string | null;
export type JsonValue =
  | JsonPrimitive
  | JsonValue[]
  | { [key: string]: JsonValue };

export interface UsersTable {
  id: GeneratedDbBigInt;
  email: string;
  password: NullableColumn<string>;
  created_at: CreatedAtColumn;
  updated_at: UpdatedAtColumn;
  chat_id: NullableColumn<string>;
  is_admin: Generated<boolean>;
}

export interface PlatformsTable {
  id: GeneratedDbBigInt;
  key: string;
  name: string;
  igdb_platform_id: NullableColumn<number>;
  parent_id: NullableDbBigIntColumn;
  created_at: CreatedAtColumn;
  updated_at: UpdatedAtColumn;
}

export interface PlatformCredentialsTable {
  id: GeneratedDbBigInt;
  user_id: DbBigIntColumn;
  api_key: NullableColumn<string>;
  account_id: NullableColumn<string>;
  created_at: CreatedAtColumn;
  updated_at: UpdatedAtColumn;
  active: Generated<boolean>;
  platform_id: DbBigIntColumn;
}

export interface GamesTable {
  id: GeneratedDbBigInt;
  title: string;
  external_id: NullableColumn<string>;
  canonical_key: string;
  created_at: CreatedAtColumn;
  updated_at: UpdatedAtColumn;
  capsule_url: NullableColumn<string>;
  platform_id: DbBigIntColumn;
  capsule_url_locked: Generated<boolean>;
  title_locked: Generated<boolean>;
}

export interface PlaySessionsTable {
  id: GeneratedDbBigInt;
  platform_credential_id: DbBigIntColumn;
  game_id: DbBigIntColumn;
  started_at: ColumnType<Date, DbTimestampInput, DbTimestampInput>;
  ended_at: ColumnType<
    Date | null,
    DbTimestampInput | null | undefined,
    DbTimestampInput | null
  >;
  created_at: CreatedAtColumn;
  updated_at: UpdatedAtColumn;
}

export interface PollResultsTable {
  id: GeneratedDbBigInt;
  platform_credential_id: DbBigIntColumn;
  game_id: NullableDbBigIntColumn;
  polled_at: ColumnType<Date, DbTimestampInput, DbTimestampInput>;
  raw_payload: NullableColumn<JsonValue>;
  created_at: CreatedAtColumn;
}

export interface ManualGameInputsTable {
  id: GeneratedDbBigInt;
  user_id: DbBigIntColumn;
  raw_title: string;
  canonical_title: string;
  mapped_game_id: NullableDbBigIntColumn;
  created_at: CreatedAtColumn;
  updated_at: UpdatedAtColumn;
}

export interface IgnoredGamesTable {
  id: GeneratedDbBigInt;
  user_id: DbBigIntColumn;
  game_id: DbBigIntColumn;
  created_at: CreatedAtColumn;
  updated_at: UpdatedAtColumn;
}

export interface GameRatingsTable {
  id: GeneratedDbBigInt;
  user_id: DbBigIntColumn;
  game_id: DbBigIntColumn;
  rating: number;
  created_at: CreatedAtColumn;
  updated_at: UpdatedAtColumn;
}

export interface UserSettingsTable {
  user_id: DbBigIntColumn;
  ignore_short_sessions_minutes: Generated<number>;
  created_at: CreatedAtColumn;
  updated_at: UpdatedAtColumn;
}

export interface GameConnectionsTable {
  user_id: DbBigIntColumn;
  game_id: DbBigIntColumn;
  connected_game_id: DbBigIntColumn;
  created_at: CreatedAtColumn;
  updated_at: UpdatedAtColumn;
}

export interface Database {
  users: UsersTable;
  platforms: PlatformsTable;
  platform_credentials: PlatformCredentialsTable;
  games: GamesTable;
  play_sessions: PlaySessionsTable;
  poll_results: PollResultsTable;
  manual_game_inputs: ManualGameInputsTable;
  ignored_games: IgnoredGamesTable;
  game_ratings: GameRatingsTable;
  user_settings: UserSettingsTable;
  game_connections: GameConnectionsTable;
}

export type User = Selectable<UsersTable>;
export type NewUser = Insertable<UsersTable>;
export type UserUpdate = Updateable<UsersTable>;

export type Platform = Selectable<PlatformsTable>;
export type NewPlatform = Insertable<PlatformsTable>;
export type PlatformUpdate = Updateable<PlatformsTable>;

export type PlatformCredential = Selectable<PlatformCredentialsTable>;
export type NewPlatformCredential = Insertable<PlatformCredentialsTable>;
export type PlatformCredentialUpdate = Updateable<PlatformCredentialsTable>;

export type Game = Selectable<GamesTable>;
export type NewGame = Insertable<GamesTable>;
export type GameUpdate = Updateable<GamesTable>;

export type PlaySession = Selectable<PlaySessionsTable>;
export type NewPlaySession = Insertable<PlaySessionsTable>;
export type PlaySessionUpdate = Updateable<PlaySessionsTable>;

export type PollResult = Selectable<PollResultsTable>;
export type NewPollResult = Insertable<PollResultsTable>;

export type ManualGameInput = Selectable<ManualGameInputsTable>;
export type NewManualGameInput = Insertable<ManualGameInputsTable>;
export type ManualGameInputUpdate = Updateable<ManualGameInputsTable>;

export type IgnoredGame = Selectable<IgnoredGamesTable>;
export type NewIgnoredGame = Insertable<IgnoredGamesTable>;

export type GameRating = Selectable<GameRatingsTable>;
export type NewGameRating = Insertable<GameRatingsTable>;
export type GameRatingUpdate = Updateable<GameRatingsTable>;

export type UserSettings = Selectable<UserSettingsTable>;
export type NewUserSettings = Insertable<UserSettingsTable>;
export type UserSettingsUpdate = Updateable<UserSettingsTable>;

export type GameConnection = Selectable<GameConnectionsTable>;
export type NewGameConnection = Insertable<GameConnectionsTable>;
