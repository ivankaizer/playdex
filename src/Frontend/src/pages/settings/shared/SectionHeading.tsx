import type { ReactNode } from "react";

/** Settings card heading: kicker, 28px title, muted description and optional right-side content. */
export function SectionHeading({
  kicker,
  title,
  description,
  aside,
}: {
  kicker: ReactNode;
  title: ReactNode;
  description?: ReactNode;
  aside?: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        <p className="pdx-kicker">{kicker}</p>
        <h2 className="pdx-title">{title}</h2>
        {description ? <p className="mt-1.5 text-sm text-muted">{description}</p> : null}
      </div>
      {aside ? <div className="flex flex-wrap items-center gap-3">{aside}</div> : null}
    </div>
  );
}
