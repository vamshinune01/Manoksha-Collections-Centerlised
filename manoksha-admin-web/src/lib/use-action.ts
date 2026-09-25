"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError } from "./client-api";

/** Runs a mutation, surfaces the backend's message, and refreshes server data on success. */
export function useAction() {
  const router = useRouter();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  async function run<T>(fn: () => Promise<T>): Promise<T | undefined> {
    setBusy(true);
    setError(undefined);
    try {
      const result = await fn();
      router.refresh();
      return result;
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "The action failed.");
      return undefined;
    } finally {
      setBusy(false);
    }
  }
  return { error, busy, run, setError };
}
