import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: { default: "Manoksha Collections", template: "%s · Manoksha Collections" },
  description: "Imitation jewellery, sarees and kids wear from Manoksha Collections — Karimnagar, Hyderabad, Mulugu.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en-IN">
      <body className="min-h-screen">{children}</body>
    </html>
  );
}
