import { PsnClient } from "../src/infrastructure/clients/psn-client";

const npsso = Bun.env.PSN_NPSSO;
if (!npsso || !npsso.trim()) {
  console.error("Missing PSN_NPSSO.");
  process.exit(1);
}

const client = new PsnClient({ npsso });

const run = async () => {
  const accountId = await client.resolveAccountId();

  if (!accountId) {
    console.error("Could not resolve account ID from access token.");
    process.exit(1);
  }

  console.log(accountId);
};

run().catch((error) => {
  console.error("Failed to resolve PSN account ID.");
  console.error(error);
  process.exit(1);
});
