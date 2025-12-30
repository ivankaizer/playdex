import { dbTests } from "../tests/db/cases";

const rawName = Bun.argv.slice(2).join(" ").trim();

if (!rawName) {
  console.error('Usage: bun run db:dump "test name"');
  process.exit(1);
}

const normalizedName = rawName.toLowerCase();

const exactMatches = dbTests.filter(
  (testCase) => testCase.name.toLowerCase() === normalizedName
);

const partialMatches =
  exactMatches.length > 0
    ? exactMatches
    : dbTests.filter((testCase) =>
        testCase.name.toLowerCase().includes(normalizedName)
      );

if (partialMatches.length === 0) {
  console.error(`No db test found for "${rawName}".`);
  console.error("Available db tests:");
  for (const testCase of dbTests) {
    console.error(`- ${testCase.name}`);
  }
  process.exit(1);
}

if (partialMatches.length > 1) {
  console.error(`Multiple db tests match "${rawName}".`);
  console.error("Matches:");
  for (const testCase of partialMatches) {
    console.error(`- ${testCase.name}`);
  }
  process.exit(1);
}

const [selected] = partialMatches;
const statements = selected.seed.map((statement) => {
  const trimmed = statement.trim().replace(/;\s*$/, "");
  return `${trimmed};`;
});

console.log(statements.join("\n"));
