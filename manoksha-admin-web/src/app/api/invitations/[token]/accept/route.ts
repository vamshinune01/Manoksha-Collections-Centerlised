import { NextResponse } from "next/server";
import { forwardAuthStep, isSameOrigin } from "@/lib/auth-step";

/** Staff invitation: the invited person sets their own password. */
export async function POST(request: Request, context: { params: Promise<{ token: string }> }) {
  if (!isSameOrigin(request)) return NextResponse.json({ title: "Forbidden" }, { status: 403 });
  const { token } = await context.params;
  const { password } = (await request.json()) as { password?: string };
  return forwardAuthStep(`invitations/${encodeURIComponent(token)}/accept`, { password });
}
