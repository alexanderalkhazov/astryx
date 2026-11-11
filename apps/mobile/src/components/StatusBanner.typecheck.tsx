// Compile-time proof of the research-vs-execution rule (TASKS.md T-032). `pnpm typecheck` fails if any of the
// expected-error directives below stops being an error, i.e. if the StatusBanner types ever allow these.
import { StatusBanner } from './StatusBanner';

export const executedWithOrderId = <StatusBanner tone="executed" title="Executed · simulated fill" orderId="SIM-463AC2" />;
export const researchWithoutOrderId = <StatusBanner tone="pending" title="Research proposal · not executed" />;

// @ts-expect-error An executed banner must show the order ID.
export const executedWithoutOrderId = <StatusBanner tone="executed" title="Executed · simulated fill" />;

// @ts-expect-error A research (pending) banner must never show an order ID.
export const researchWithOrderId = <StatusBanner tone="pending" title="Research proposal" orderId="SIM-463AC2" />;

// @ts-expect-error Error banners carry no order ID either.
export const errorWithOrderId = <StatusBanner tone="error" title="Connection lost" orderId="SIM-463AC2" />;
