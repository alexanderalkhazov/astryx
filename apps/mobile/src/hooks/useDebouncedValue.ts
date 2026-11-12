import { useEffect, useState } from 'react';

/** The value once it has stopped changing for <paramref name="delayMs"/> (search uses 200 ms, SYSTEM_DESIGN.md §8.4.12). */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return debounced;
}
