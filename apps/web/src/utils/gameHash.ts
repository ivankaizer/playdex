const HASH_ALPHABET =
  "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
const HASH_BASE = BigInt(HASH_ALPHABET.length);
const HASH_MULTIPLIER = 1299827n;
const HASH_INCREMENT = 811n;

const encodeBase = (value: bigint) => {
  if (value === 0n) {
    return HASH_ALPHABET[0];
  }

  let remaining = value;
  let encoded = "";
  while (remaining > 0n) {
    const index = Number(remaining % HASH_BASE);
    encoded = HASH_ALPHABET[index] + encoded;
    remaining = remaining / HASH_BASE;
  }
  return encoded;
};

const decodeBase = (value: string) => {
  let decoded = 0n;
  for (const char of value) {
    const index = HASH_ALPHABET.indexOf(char);
    if (index === -1) {
      return null;
    }
    decoded = decoded * HASH_BASE + BigInt(index);
  }
  return decoded;
};

const encodeNumericGameId = (gameId: number) => {
  if (!Number.isInteger(gameId) || gameId <= 0) {
    return "";
  }
  const hashed = BigInt(gameId) * HASH_MULTIPLIER + HASH_INCREMENT;
  return encodeBase(hashed);
};

const isNumericString = (value: string) => /^\d+$/.test(value);

export const decodeGameHash = (hash: string) => {
  const trimmed = hash.trim();
  if (!trimmed) {
    return null;
  }
  const decoded = decodeBase(trimmed);
  if (decoded === null || decoded < HASH_INCREMENT) {
    return null;
  }
  const raw = decoded - HASH_INCREMENT;
  if (raw % HASH_MULTIPLIER !== 0n) {
    return null;
  }
  const gameId = raw / HASH_MULTIPLIER;
  const numeric = Number(gameId);
  if (!Number.isSafeInteger(numeric) || numeric <= 0) {
    return null;
  }
  return numeric;
};

export const encodeGameId = (gameId: number | string) => {
  if (typeof gameId === "number") {
    return encodeNumericGameId(gameId);
  }
  const trimmed = gameId.trim();
  if (!trimmed) {
    return "";
  }
  if (isNumericString(trimmed)) {
    return encodeNumericGameId(Number(trimmed));
  }
  return decodeGameHash(trimmed) ? trimmed : "";
};
