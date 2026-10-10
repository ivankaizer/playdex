import { platformColor } from "@/lib/platforms";

const platformChips: ReadonlyArray<{ key: string; name: string; mark: string }> = [
  { key: "steam", name: "Steam", mark: "S" },
  { key: "psn", name: "PlayStation", mark: "PS" },
  { key: "xbox", name: "Xbox", mark: "X" },
  { key: "switch", name: "Switch", mark: "NS" },
];

/** The platforms Playdex tracks, as a row of chips (sign-in page and landing hero). */
export function PlatformChips() {
  return (
    <ul aria-label="Supported platforms" className="m-0 flex list-none flex-wrap gap-2 p-0">
      {platformChips.map((chip) => (
        <li
          key={chip.key}
          className="flex h-9 items-center gap-2 rounded-full border border-[color:var(--card-border)] bg-surface/75 pl-1.5 pr-3.5 text-[13px] font-medium"
        >
          <span
            aria-hidden="true"
            className="grid h-6 w-6 place-items-center rounded-full text-[10px] font-bold text-white"
            style={{ backgroundColor: platformColor(chip.key, chip.name) }}
          >
            {chip.mark}
          </span>
          {chip.name}
        </li>
      ))}
    </ul>
  );
}
