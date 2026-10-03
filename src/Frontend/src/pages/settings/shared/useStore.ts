import { useCallback, useRef, useState } from "react";

/**
 * Immutable state with a synchronously readable "current" value, mirroring the Blazor
 * page-state classes: async handlers read the latest state via `get()` after awaiting,
 * and every `update` re-renders.
 */
export function useStore<S>(initial: () => S) {
  const [state, setState] = useState(initial);
  const ref = useRef(state);
  const update = useCallback((reducer: (current: S) => S) => {
    ref.current = reducer(ref.current);
    setState(ref.current);
  }, []);
  const get = useCallback(() => ref.current, []);
  return [state, update, get] as const;
}
