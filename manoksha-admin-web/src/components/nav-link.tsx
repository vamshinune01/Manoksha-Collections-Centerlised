"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cx } from "./ui";

export function NavLink({ href, label }: { href: string; label: string }) {
  const pathname = usePathname();
  const active = href === "/" ? pathname === "/" : pathname.startsWith(href);
  return (
    <Link
      href={href}
      className={cx(
        "block rounded-md px-3 py-2 text-sm",
        active ? "bg-brand-50 font-medium text-brand-700" : "text-slate-700 hover:bg-slate-50",
      )}
    >
      {label}
    </Link>
  );
}
