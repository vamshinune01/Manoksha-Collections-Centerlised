import { NextResponse } from "next/server";
import { forwardAuthStep, isSameOrigin } from "@/lib/auth-step";

/** First-run Owner setup (the API allows it only while no Owner exists and with the setup code). */
export async function POST(request: Request) {
  if (!isSameOrigin(request)) return NextResponse.json({ title: "Forbidden" }, { status: 403 });
  const { setupCode, email, fullName, password } = (await request.json()) as Record<string, string | undefined>;
  return forwardAuthStep("setup/owner", { setupCode, email, fullName, password });
}
