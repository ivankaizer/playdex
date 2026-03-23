import { renderNotificationWebp } from "../src/modules/telegram/notification-image";
import { sendTelegramImage } from "../src/modules/telegram/client";

type CliOptions = {
  type: "started" | "ended";
  title: string;
  platform: string;
  image: string;
  duration?: string;
  fontUrl?: string;
  chatId: string;
  token: string;
};

const DEFAULT_FONT_URL =
  "https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600;700&display=swap";

const parseArgs = (argv: string[]) => {
  const args = new Map<string, string>();
  for (let i = 0; i < argv.length; i += 1) {
    const value = argv[i];
    if (!value.startsWith("--")) {
      continue;
    }

    const key = value.slice(2);
    const next = argv[i + 1];
    if (next && !next.startsWith("--")) {
      args.set(key, next);
      i += 1;
    } else {
      args.set(key, "");
    }
  }
  return args;
};

const printHelp = () => {
  console.log(`Usage:
  bun run scripts/telegram-notify-image.ts --type started --title "Game Title" \\
    --platform "steam" --image "/path/to/cover.jpg" \\
    --chat-id 123456

  bun run scripts/telegram-notify-image.ts --type ended --title "Game Title" \\
    --platform "steam" --image "/path/to/cover.jpg" --duration "1h 32m" \\
    --chat-id 123456

Environment:
  TELEGRAM_BOT_TOKEN  Bot token used to send the image.
`);
};

const requireArg = (args: Map<string, string>, key: string) => {
  const value = args.get(key)?.trim();
  if (!value) {
    throw new Error(`Missing required argument: --${key}`);
  }
  return value;
};

const run = async () => {
  const argv = process.argv.slice(2);
  if (argv.includes("--help") || argv.includes("-h")) {
    printHelp();
    return;
  }

  const args = parseArgs(argv);
  const type = (args.get("type")?.trim() || "started") as CliOptions["type"];
  if (type !== "started" && type !== "ended") {
    throw new Error("Invalid --type. Use started or ended.");
  }

  const token = Bun.env.TELEGRAM_BOT_TOKEN?.trim();
  if (!token) {
    throw new Error("Missing TELEGRAM_BOT_TOKEN environment variable.");
  }

  const options: CliOptions = {
    type,
    title: requireArg(args, "title"),
    platform: requireArg(args, "platform"),
    image: requireArg(args, "image"),
    duration: args.get("duration")?.trim(),
    fontUrl: args.get("font-url")?.trim() || DEFAULT_FONT_URL,
    chatId: requireArg(args, "chat-id"),
    token,
  };

  if (options.type === "ended" && !options.duration) {
    throw new Error("--duration is required when --type is ended.");
  }

  const imageBuffer = await renderNotificationWebp({
    title: options.title,
    platform: options.platform,
    image: options.image,
    kind: options.type,
    duration: options.duration,
    fontUrl: options.fontUrl,
  });

  const headline =
    options.type === "ended" ? "Session ended" : "Session started";
  const caption = [
    headline,
    options.type === "ended" && options.duration
      ? `Duration: ${options.duration}`
      : null,
  ]
    .filter(Boolean)
    .join("\n");

  await sendTelegramImage(
    options.token,
    options.chatId,
    imageBuffer,
    undefined,
    caption
  );
  console.log("Notification image sent.");
};

run().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
