import Link from "next/link";

const ITEMS = [
  { key: "stock", href: "/inventory", label: "Stock" },
  { key: "transfers", href: "/inventory/transfers", label: "Transfers" },
  { key: "counts", href: "/inventory/counts", label: "Stock counts" },
  { key: "adjustments", href: "/inventory/adjustments", label: "Adjustments" },
  { key: "discrepancies", href: "/inventory/discrepancies", label: "Discrepancies" },
] as const;

export function InventoryNav({ active }: { active: (typeof ITEMS)[number]["key"] }) {
  return (
    <nav className="mb-5 flex flex-wrap gap-1 border-b border-slate-200">
      {ITEMS.map((i) => (
        <Link key={i.key} href={i.href}
          className={`-mb-px border-b-2 px-3 py-2 text-sm ${i.key === active ? "border-brand-600 font-medium text-brand-700" : "border-transparent text-slate-600 hover:text-slate-900"}`}>
          {i.label}
        </Link>
      ))}
    </nav>
  );
}

export const TRANSFER_TONE: Record<string, "slate" | "green" | "red" | "amber" | "brand"> = {
  Requested: "amber", Approved: "brand", Prepared: "brand", InTransit: "brand", Received: "green", Discrepancy: "red", Rejected: "slate", Cancelled: "slate",
};

export const STATUS_LABEL: Record<string, string> = {
  Available: "Available", Reserved: "Reserved", TransferPending: "Transfer pending", InTransit: "In transit", Damaged: "Damaged",
  Repair: "Repair", Lost: "Lost", Blocked: "Blocked", Received: "Received",
};
