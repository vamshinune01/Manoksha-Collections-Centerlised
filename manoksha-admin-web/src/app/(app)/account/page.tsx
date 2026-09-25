import { Card, PageHeader } from "@/components/ui";
import { getMe } from "@/lib/backend";
import { ChangePasswordForm, MfaSetup } from "./account-forms";

export default async function AccountPage() {
  const me = (await getMe())!;
  return (
    <>
      <PageHeader title="My account" description={me.email ?? undefined} />
      <div className="grid gap-6 lg:grid-cols-2">
        <Card title="Change password"><ChangePasswordForm /></Card>
        <Card title="Two-step verification"><MfaSetup enabled={me.mfaEnabled} /></Card>
      </div>
    </>
  );
}
