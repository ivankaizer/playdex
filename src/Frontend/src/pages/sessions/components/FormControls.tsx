import { useEffect, useLayoutEffect, useRef, type InputHTMLAttributes, type SelectHTMLAttributes } from "react";

type ChangeInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, "value" | "defaultValue" | "onChange"> & {
  value: string;
  onValueChange: (value: string) => void;
};

/**
 * Input bound like Blazor's `value="@x" @onchange` / `@bind`: the handler runs on the native `change`
 * event (commit), and the DOM value is only overwritten when the rendered value changes.
 */
export function ChangeInput({ value, onValueChange, ...rest }: ChangeInputProps) {
  const ref = useRef<HTMLInputElement>(null);
  const handler = useRef(onValueChange);

  useLayoutEffect(() => {
    handler.current = onValueChange;
  });

  useLayoutEffect(() => {
    const element = ref.current;
    if (element && element.value !== value) element.value = value;
  }, [value]);

  useEffect(() => {
    const element = ref.current;
    if (!element) return;
    const listener = () => handler.current(element.value);
    element.addEventListener("change", listener);
    return () => element.removeEventListener("change", listener);
  }, []);

  return <input ref={ref} defaultValue={value} {...rest} />;
}

type BoundSelectProps = Omit<SelectHTMLAttributes<HTMLSelectElement>, "value" | "defaultValue" | "onChange"> & {
  value: string;
  onValueChange: (value: string) => void;
};

/**
 * Select bound like Blazor: the DOM value is assigned after the options render, so a value without a
 * matching option shows no selection (React's controlled select would fall back to the first option).
 */
export function BoundSelect({ value, onValueChange, children, ...rest }: BoundSelectProps) {
  const ref = useRef<HTMLSelectElement>(null);

  useLayoutEffect(() => {
    const element = ref.current;
    if (element && element.value !== value) element.value = value;
  });

  return (
    <select ref={ref} onChange={(event) => onValueChange(event.currentTarget.value)} {...rest}>
      {children}
    </select>
  );
}
