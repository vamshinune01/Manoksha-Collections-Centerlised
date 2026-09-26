import { LoginForm } from "./login-form";

export const metadata = { title: "Sign in" };

export default async function LoginPage({ searchParams }: { searchParams: Promise<{ next?: string }> }) {
  const { next } = await searchParams;
  // Only same-site relative paths are honoured as a return target.
  const target = next && next.startsWith("/") && !next.startsWith("//") && !next.startsWith("/reseller") ? next : "/";
  return <LoginForm next={target} />;
}
