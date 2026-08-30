import type { NetworkAdapter } from "./control-client";

export function formatAdapterTitle(adapter: NetworkAdapter): string {
  return `${shortName(adapter.name)}\n${adapter.isActive ? "ON" : "OFF"}`;
}

export function formatCycleTitle(adapters: NetworkAdapter[]): string {
  const active = adapters.find((adapter) => adapter.isActive);
  return active ? `Active\n${shortName(active.name)}` : "No active\nadapter";
}

function shortName(name: string): string {
  const normalized = name.trim() || "Adapter";
  return normalized.length <= 12 ? normalized : `${normalized.slice(0, 11)}…`;
}
