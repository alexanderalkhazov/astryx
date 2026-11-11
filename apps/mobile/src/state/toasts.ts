import { motion } from '@astryx/design-tokens';
import { create } from 'zustand';

export type ToastTone = 'success' | 'info' | 'warning' | 'error';

export interface Toast {
  id: string;
  tone: ToastTone;
  title: string;
  body?: string;
  createdAt: number;
}

interface ToastState {
  toasts: Toast[];
  /** Shows a toast for 3.8 s (design). Keeps at most 3; the oldest is dropped. Returns its id. */
  show: (toast: Omit<Toast, 'id' | 'createdAt'>) => string;
  dismiss: (id: string) => void;
  clear: () => void;
}

export const MAX_TOASTS = 3;

const timers = new Map<string, ReturnType<typeof setTimeout>>();
let sequence = 0;

export const useToasts = create<ToastState>((set, get) => ({
  toasts: [],
  show: (toast) => {
    sequence += 1;
    const id = `toast-${sequence}`;
    const next = [...get().toasts, { ...toast, id, createdAt: Date.now() }];
    const dropped = next.slice(0, Math.max(0, next.length - MAX_TOASTS));
    dropped.forEach((t) => {
      clearTimeout(timers.get(t.id));
      timers.delete(t.id);
    });
    set({ toasts: next.slice(-MAX_TOASTS) });
    timers.set(id, setTimeout(() => get().dismiss(id), motion.toast));
    return id;
  },
  dismiss: (id) => {
    clearTimeout(timers.get(id));
    timers.delete(id);
    set({ toasts: get().toasts.filter((t) => t.id !== id) });
  },
  clear: () => {
    timers.forEach((timer) => clearTimeout(timer));
    timers.clear();
    set({ toasts: [] });
  },
}));

/** Imperative helper for non-component code (mutations, SignalR handlers). */
export const toast = (t: Omit<Toast, 'id' | 'createdAt'>) => useToasts.getState().show(t);
