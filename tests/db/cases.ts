import { defineDbTest } from "./support";
import {
  exampleListUsers,
  exampleListPlatformCredentialsForUser,
  calculateTotalPlaySessions,
} from "../../src/infrastructure/db/queries";

export const dbTests = [
  defineDbTest({
    name: "listUsers returns seeded rows",
    seed: ["insert into users (email) values ('tester@example.com')"],
    query: () => exampleListUsers(),
    expect: {
      rowCount: 1,
      rows: [{ id: "1", email: "tester@example.com" }],
    },
  }),
  defineDbTest({
    name: "listPlatformCredentialsForUser returns seeded credentials",
    seed: [
      "insert into users (email) values ('tester@example.com')",
      `insert into platform_credentials (user_id, platform, api_key, account_id)
        values (1, 'steam', 'key', 'steam-id')`,
    ],
    query: () => exampleListPlatformCredentialsForUser(1),
    expect: {
      rowCount: 1,
      rows: [
        {
          id: "1",
          platform: "steam",
          api_key: "key",
          account_id: "steam-id",
        },
      ],
    },
  }),
  defineDbTest({
    name: "calculate total play sessions",
    seed: [
      "insert into users (email) values ('tester@example.com')",
      "insert into games (platform, title, canonical_key) values ('steam', 'game', 'game:1')",
      `insert into platform_credentials (user_id, platform, api_key, account_id)
        values (1, 'steam', 'key', 'steam-id')`,
      `insert into play_sessions (platform_credential_id, game_id, started_at)
       values (1, 1, CURRENT_TIMESTAMP)`,
      `insert into play_sessions (platform_credential_id, game_id, started_at)
       values (1, 1, CURRENT_TIMESTAMP)`,
    ],
    query: () => calculateTotalPlaySessions(),
    expect: {
      rowCount: 1,
      rows: [
        {
          count: "2",
        },
      ],
    },
  }),
];
