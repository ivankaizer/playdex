const Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
const Multiplier = 1_299_827n;
const Increment = 811n;
const MaxSafeInteger = 9_007_199_254_740_991n;
const Base = BigInt(Alphabet.length);

/**
 * The hash for a game: a number is encoded, a string that already is a hash is kept, and other digits are read as
 * a raw id. A hash can be all digits (game 87421 is "2029282"), so it is checked before the digits are re-encoded.
 */
export function encodeGameId(gameId: number | string | null | undefined): string {
  if (typeof gameId === "number") {
    if (!Number.isFinite(gameId) || gameId <= 0 || !Number.isInteger(gameId)) return "";
    return encodeBase(BigInt(gameId) * Multiplier + Increment);
  }

  const trimmed = gameId?.trim() ?? "";
  if (trimmed.length === 0) return "";
  if (decodeGameHash(trimmed) !== null) return trimmed;
  return /^[0-9]+$/.test(trimmed) ? encodeGameId(Number(trimmed)) : "";
}

export function decodeGameHash(hash: string | null | undefined): number | null {
  const trimmed = hash?.trim() ?? "";
  if (trimmed.length === 0) return null;

  const decoded = decodeBase(trimmed);
  if (decoded === null || decoded < Increment) return null;

  const raw = decoded - Increment;
  if (raw % Multiplier !== 0n) return null;

  const gameId = raw / Multiplier;
  if (gameId <= 0n || gameId > MaxSafeInteger) return null;
  return Number(gameId);
}

function encodeBase(value: bigint): string {
  if (value === 0n) return Alphabet[0];
  let encoded = "";
  for (let remaining = value; remaining > 0n; remaining /= Base) {
    encoded = Alphabet[Number(remaining % Base)] + encoded;
  }
  return encoded;
}

function decodeBase(value: string): bigint | null {
  let decoded = 0n;
  for (const character of value) {
    const index = Alphabet.indexOf(character);
    if (index < 0) return null;
    decoded = decoded * Base + BigInt(index);
  }
  return decoded;
}
