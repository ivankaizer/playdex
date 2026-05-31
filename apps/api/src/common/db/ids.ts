const POSITIVE_INTEGER = /^[1-9]\d*$/;

export const toDomainId = (value: string | number, field: string): number => {
  if (typeof value === "string" && !POSITIVE_INTEGER.test(value)) {
    throw new Error(`${field} must be a positive safe integer.`);
  }

  const normalized = Number(value);
  if (!Number.isSafeInteger(normalized) || normalized <= 0) {
    throw new Error(`${field} must be a positive safe integer.`);
  }

  return normalized;
};
