import { NextResponse } from "next/server";
import { serverConfig } from "@/lib/config";
import { isSameOrigin } from "@/lib/session";

export async function POST(request: Request) {
  if (!isSameOrigin(request)) return NextResponse.json({ title: "Forbidden" }, { status: 403 });
  const { mobile } = (await request.json()) as { mobile?: string };
  const upstream = await fetch(`${serverConfig.apiUrl}/api/v1/auth/otp/request`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ mobile, context: "customer" }),
    cache: "no-store",
  });
  return new NextResponse(await upstream.text(), { status: upstream.status, headers: { "Content-Type": "application/json" } });
}
