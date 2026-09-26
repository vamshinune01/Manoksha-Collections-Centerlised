import { forward } from "@/lib/forward";
import { resellerToken } from "@/lib/session";

/** Same-origin proxy to the backend's reseller API only (`/api/v1/reseller/*`). */
async function handle(request: Request, context: { params: Promise<{ path: string[] }> }) {
  return forward(request, "/api/v1/reseller", (await context.params).path, await resellerToken());
}

export { handle as GET, handle as POST, handle as PUT };
