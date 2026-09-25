import Link from "next/link";

export function CatalogNav({ active }: { active: "products" | "categories" | "attributes" }) {
  const items = [
    { key: "products", href: "/catalog", label: "Products" },
    { key: "categories", href: "/catalog/categories", label: "Categories" },
    { key: "attributes", href: "/catalog/attributes", label: "Variant attributes" },
  ] as const;
  return (
    <nav className="mb-5 flex gap-1 border-b border-slate-200">
      {items.map((i) => (
        <Link key={i.key} href={i.href}
          className={`-mb-px border-b-2 px-3 py-2 text-sm ${i.key === active ? "border-brand-600 font-medium text-brand-700" : "border-transparent text-slate-600 hover:text-slate-900"}`}>
          {i.label}
        </Link>
      ))}
    </nav>
  );
}
