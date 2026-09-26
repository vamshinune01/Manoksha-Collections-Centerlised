import { redirect } from "next/navigation";
import { customerFetch, getCustomerMe } from "@/lib/backend";
import type { CustomerProfile } from "@/lib/types";
import { Alert } from "@/components/ui";
import { ProfileForm } from "./profile-form";

export const metadata = { title: "My account" };

export default async function AccountPage() {
  if (!(await getCustomerMe())) redirect("/login?next=/account");
  const profile = await customerFetch<CustomerProfile>("profile");
  if (!profile.ok) return <Alert>{profile.title}</Alert>;
  return <ProfileForm profile={profile.data} />;
}
