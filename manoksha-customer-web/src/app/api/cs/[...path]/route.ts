import { forward } from "@/lib/forward";
import { customerToken } from "@/lib/session";

/** Signed-in customer API only (`/api/v1/customer/*`). */
async function handle(request: Request, context: { params: Promise<{ path: string[] }> }) {
  return forward(request, "/api/v1/customer", (await context.params).path, await customerToken());
}

export { handle as GET, handle as POST };
