const MESSAGE = "It works";
const INTERVAL_MS = 60_000;

console.log(MESSAGE);
setInterval(() => {
  console.log(MESSAGE);
}, INTERVAL_MS);
