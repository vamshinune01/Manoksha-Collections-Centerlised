import { NextResponse } from "next/server";
import { forwardAuthStep, isSameOrigin } from "@/lib/auth-step";

export async function POST(request: Request) {
  if (!isSameOrigin(request)) return NextResponse.json({ title: "Forbidden" }, { status: 403 });
  const { email, password } = (await request.json()) as { email?: string; password?: string };
  return forwardAuthStep("auth/internal/login", { email, password, client: "admin" });
}
