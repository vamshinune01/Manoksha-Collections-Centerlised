import { forward } from "@/lib/forward";

/**
 * Development UPI simulator (the payer's side). The backend maps these routes only when the simulator is the configured
 * provider, which is refused in Production — so in Production this simply returns 404.
 */
async function handle(request: Request, context: { params: Promise<{ path: string[] }> }) {
  return forward(request, "/api/v1/payment-simulator", (await context.params).path, undefined);
}

export { handle as GET, handle as POST };
