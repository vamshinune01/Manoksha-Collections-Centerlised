import { NextResponse } from "next/server";
import { serverConfig } from "@/lib/config";
import { isSameOrigin } from "@/lib/auth-step";
import { upstreamFetch } from "@/lib/upstream";

/**
 * Development only: forwards a direct media upload (raw bytes) to the API's disk-storage stand-in. Deployed environments upload
 * straight to Google Cloud Storage with signed URLs, so this route is never used there (the API refuses it too).
 */
export async function PUT(request: Request, context: { params: Promise<{ path: string[] }> }) {
  if (process.env.NODE_ENV === "production" || !isSameOrigin(request)) {
    return NextResponse.json({ status: 404, title: "Not found" }, { status: 404 });
  }
  const path = (await context.params).path.map(encodeURIComponent).join("/");
  const url = new URL(request.url);
  const upstream = await upstreamFetch(`${serverConfig.apiUrl}/api/v1/dev-storage/${path}${url.search}`, {
    method: "PUT",
    headers: { "Content-Type": request.headers.get("content-type") ?? "application/octet-stream" },
    body: await request.arrayBuffer(),
    cache: "no-store",
  });
  return new NextResponse(null, { status: upstream.status });
}
