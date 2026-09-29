"use client";

import { useCallback, useEffect, useState } from "react";
import { Alert, Button, Input } from "@/components/ui";
import { ApiError, callApi } from "@/lib/client-api";

interface ImageUrls { thumb: string; medium: string; large: string; altText: string | null; width: number; height: number }

interface Media {
  id: string;
  kind: "Image" | "Video";
  status: "Uploading" | "Ready" | "Failed";
  sortOrder: number;
  altText: string | null;
  originalFileName: string;
  originalBytes: number;
  width: number | null;
  height: number | null;
  durationSeconds: number | null;
  image: ImageUrls | null;
  videoUrl: string | null;
  posterUrl: string | null;
  optimizedBytes: number;
  failureReason: string | null;
}

interface Upload { mediaId: string; uploadUrl: string; method: string; contentType: string }

const mb = (b: number) => `${(b / 1024 / 1024).toFixed(b < 1024 * 1024 ? 2 : 1)} MB`;

/** PUT with progress; the URL is a short-lived signed storage URL (or the development stand-in). */
function put(url: string, file: File, contentType: string, onProgress: (p: number) => void) {
  return new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("PUT", url);
    xhr.setRequestHeader("Content-Type", contentType);
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(Math.round((e.loaded / e.total) * 100));
    xhr.onload = () => (xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new Error(`Upload failed (${xhr.status})`)));
    xhr.onerror = () => reject(new Error("Upload failed — check your connection."));
    xhr.send(file);
  });
}

/**
 * Product photos and videos (ADR-001 §29). Files go straight to storage; the server then makes the optimized versions customers
 * see (WebP images in three sizes, 720p MP4 with a poster). The first image is the product's main image.
 */
export function MediaManager({ productId, canManage }: { productId: string; canManage: boolean }) {
  const [media, setMedia] = useState<Media[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<string | null>(null);
  const base = `admin/catalog/products/${productId}/media`;

  const load = useCallback(() => callApi<Media[]>(base).then(setMedia).catch(() => setError("Media could not be loaded.")), [base]);
  useEffect(() => {
    void load();
  }, [load]);

  const upload = async (files: FileList | null) => {
    if (!files) return;
    setError(null);
    for (const file of Array.from(files)) {
      try {
        setProgress(`${file.name}: preparing…`);
        const u = await callApi<Upload>(`${base}/uploads`, "POST", { fileName: file.name, contentType: file.type, sizeBytes: file.size });
        await put(u.uploadUrl, file, u.contentType, (p) => setProgress(`${file.name}: uploading ${p}%`));
        setProgress(`${file.name}: optimizing${file.type.startsWith("video/") ? " video (this can take a minute)" : ""}…`);
        await callApi(`${base}/${u.mediaId}/complete`, "POST");
      } catch (e) {
        setError(`${file.name}: ${e instanceof ApiError || e instanceof Error ? e.message : "upload failed"}`);
      }
    }
    setProgress(null);
    await load();
  };

  const ready = media.filter((m) => m.status === "Ready").sort((a, b) => a.sortOrder - b.sortOrder);
  const move = async (index: number, delta: number) => {
    const ids = ready.map((m) => m.id);
    const [item] = ids.splice(index, 1);
    ids.splice(index + delta, 0, item!);
    try {
      setMedia(await callApi<Media[]>(`${base}/order`, "PUT", { mediaIds: ids }));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Could not reorder.");
    }
  };
  const remove = async (m: Media) => {
    if (!window.confirm(`Delete ${m.originalFileName}?`)) return;
    await callApi(`${base}/${m.id}`, "DELETE").catch((e: unknown) => setError(e instanceof ApiError ? e.message : "Could not delete."));
    await load();
  };
  const saveAlt = async (m: Media, altText: string) => {
    await callApi(`${base}/${m.id}`, "PUT", { altText }).catch((e: unknown) => setError(e instanceof ApiError ? e.message : "Could not save."));
  };

  return (
    <div className="space-y-4">
      {error && <Alert>{error}</Alert>}
      {progress && <Alert tone="info">{progress}</Alert>}
      {ready.length === 0 && <p className="text-sm text-slate-500">No photos or videos yet. The first image becomes the main product image in the shop.</p>}
      <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {ready.map((m, i) => (
          <li key={m.id} className="rounded-lg border border-slate-200 p-2">
            <div className="relative aspect-square overflow-hidden rounded-md bg-slate-100">
              {/* eslint-disable-next-line @next/next/no-img-element -- already optimized WebP served from storage */}
              <img src={m.image?.thumb ?? m.posterUrl ?? ""} alt={m.altText ?? m.originalFileName} className="h-full w-full object-cover" loading="lazy" />
              {m.kind === "Video" && <span className="absolute bottom-1 right-1 rounded bg-black/70 px-1.5 text-xs text-white">▶ {Math.round(m.durationSeconds ?? 0)}s</span>}
              {i === 0 && m.kind === "Image" && <span className="absolute left-1 top-1 rounded bg-brand-600 px-1.5 text-xs text-white">Main</span>}
            </div>
            <p className="mt-1 truncate text-xs text-slate-600" title={m.originalFileName}>{m.originalFileName}</p>
            <p className="text-[11px] text-slate-400">{m.width}×{m.height} · original {mb(m.originalBytes)} → served {mb(m.optimizedBytes)}</p>
            {canManage && (
              <div className="mt-2 space-y-2">
                <Input defaultValue={m.altText ?? ""} placeholder="Description (for accessibility)" onBlur={(e) => e.target.value !== (m.altText ?? "") && saveAlt(m, e.target.value)} />
                <div className="flex gap-1">
                  <Button variant="secondary" disabled={i === 0} onClick={() => move(i, -1)} aria-label="Move earlier">←</Button>
                  <Button variant="secondary" disabled={i === ready.length - 1} onClick={() => move(i, 1)} aria-label="Move later">→</Button>
                  <Button variant="ghost" className="ml-auto text-red-700" onClick={() => remove(m)}>Delete</Button>
                </div>
              </div>
            )}
          </li>
        ))}
      </ul>
      {media.filter((m) => m.status === "Failed").map((m) => (
        <Alert key={m.id} tone="warning">{m.originalFileName}: {m.failureReason}</Alert>
      ))}
      {canManage && (
        <label className="block">
          <span className="text-sm font-medium text-slate-700">Add photos or videos</span>
          <input type="file" multiple accept="image/jpeg,image/png,image/webp,video/mp4,video/quicktime,video/webm" disabled={!!progress}
            onChange={(e) => { void upload(e.target.files); e.target.value = ""; }} className="mt-1 block text-sm" />
          <span className="text-xs text-slate-500">Photos: JPEG/PNG/WebP up to 20 MB (max 12). Videos: MP4/MOV/WebM up to 200 MB and 2 minutes (max 2).</span>
        </label>
      )}
    </div>
  );
}
