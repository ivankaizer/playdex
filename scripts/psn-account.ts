import {
  exchangeCodeForAccessToken,
  exchangeNpssoForCode,
  makeUniversalSearch,
} from "psn-api";

const npsso = Bun.env.PSN_NPSSO;
const nickname = Bun.env.PSN_NICKNAME;
if (!npsso || !npsso.trim()) {
  console.error("Missing PSN_NPSSO.");
  process.exit(1);
}

if (!nickname || !nickname.trim()) {
  console.error("Missing PSN_NICKNAME.");
  process.exit(1);
}

const run = async () => {
  const trimmedNickname = nickname.trim();
  const code = await exchangeNpssoForCode(npsso);
  const tokenResponse = await exchangeCodeForAccessToken(code);
  const response = await makeUniversalSearch(
    { accessToken: tokenResponse.accessToken },
    trimmedNickname,
    "SocialAllAccounts"
  );
  const accountId =
    response.domainResponses?.[0]?.results?.[0]?.socialMetadata?.accountId ??
    "";

  if (!accountId) {
    console.error("Could not resolve account ID from nickname.");
    process.exit(1);
  }

  console.log(accountId);
};

run().catch((error) => {
  console.error("Failed to resolve PSN account ID.");
  console.error(error);
  process.exit(1);
});
