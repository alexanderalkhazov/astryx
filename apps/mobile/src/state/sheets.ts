import { create } from 'zustand';

/** Sheets that can be opened from anywhere (SYSTEM_DESIGN.md §8.3: FiltersSheet, IndicatorsSheet, ConfirmOrderSheet, FillReceiptSheet). */
export type SheetId = 'filters' | 'indicators' | 'confirm-order' | 'fill-receipt' | 'gallery-demo';

interface SheetState {
  /** One sheet at a time, matching the design. */
  active: SheetId | null;
  open: (id: SheetId) => void;
  close: (id?: SheetId) => void;
}

export const useSheetStore = create<SheetState>((set, get) => ({
  active: null,
  open: (id) => set({ active: id }),
  close: (id) => {
    if (id === undefined || get().active === id) set({ active: null });
  },
}));

export function useSheet(id: SheetId) {
  const isOpen = useSheetStore((s) => s.active === id);
  const open = useSheetStore((s) => s.open);
  const close = useSheetStore((s) => s.close);
  return { isOpen, open: () => open(id), close: () => close(id) };
}
