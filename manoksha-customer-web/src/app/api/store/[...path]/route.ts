import { forward } from "@/lib/forward";

/** Anonymous storefront API only (`/api/v1/catalog/*`); no session is ever attached. */
async function handle(request: Request, context: { params: Promise<{ path: string[] }> }) {
  return forward(request, "/api/v1/catalog", (await context.params).path, undefined);
}

export { handle as GET, handle as POST };
