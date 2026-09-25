import { redirect } from "next/navigation";
import { Shell } from "@/components/shell";
import { getMe } from "@/lib/backend";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const me = await getMe();
  if (!me) redirect("/login");
  if (me.accountType !== "Internal") redirect("/login");
  return <Shell me={me}>{children}</Shell>;
}
