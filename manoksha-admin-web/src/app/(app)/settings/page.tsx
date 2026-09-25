import { Alert, Forbidden, PageHeader } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Setting } from "@/lib/types";
import { SettingRow } from "./setting-row";

export default async function SettingsPage() {
  const me = (await getMe())!;
  if (!can(me, P.settingsView)) return <Forbidden what="business settings" />;
  const settings = await backendFetch<Setting[]>("admin/settings");

  return (
    <>
      <PageHeader title="Business settings" description="Owner-controlled values. Every change needs a reason and is versioned and audited." />
      {!settings.ok ? (
        <Alert>{settings.problem.title}</Alert>
      ) : (
        <div className="space-y-3">
          {settings.data.map((s) => (
            <SettingRow key={s.key} setting={s} editable={can(me, P.settingsManage)} />
          ))}
        </div>
      )}
    </>
  );
}
