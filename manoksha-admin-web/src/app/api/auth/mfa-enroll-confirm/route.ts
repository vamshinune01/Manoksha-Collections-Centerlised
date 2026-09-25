import { NextResponse } from "next/server";
import { forwardAuthStep, isSameOrigin } from "@/lib/auth-step";

export async function POST(request: Request) {
  if (!isSameOrigin(request)) return NextResponse.json({ title: "Forbidden" }, { status: 403 });
  const { challengeToken, code } = (await request.json()) as { challengeToken?: string; code?: string };
  return forwardAuthStep("auth/internal/mfa/enroll/confirm", { challengeToken, code });
}
