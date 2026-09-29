"use client";

import { useState } from "react";
import type { PublicMedia } from "@/lib/types";
import { ProductImage } from "@/components/product-image";

/** Product gallery: optimized images and 720p videos (poster first; the video only downloads when played). */
export function Gallery({ media, name }: { media: PublicMedia[]; name: string }) {
  const [index, setIndex] = useState(0);
  const current = media[index];
  return (
    <div className="space-y-3">
      <div className="overflow-hidden rounded-2xl">
        {!current ? (
          <ProductImage image={null} alt={name} sizes="100vw" className="aspect-square w-full" />
        ) : current.kind === "Video" ? (
          <video key={current.id} src={current.videoUrl ?? undefined} poster={current.posterUrl ?? undefined} controls playsInline preload="none"
            className="aspect-square w-full bg-black object-contain" />
        ) : (
          <ProductImage image={current.image} alt={current.altText ?? name} sizes="(min-width: 768px) 50vw, 100vw" className="aspect-square w-full" priority />
        )}
      </div>
      {media.length > 1 && (
        <div className="flex gap-2 overflow-x-auto">
          {media.map((m, i) => (
            <button key={m.id} onClick={() => setIndex(i)} className={`relative h-16 w-16 shrink-0 overflow-hidden rounded-md border-2 ${i === index ? "border-brand-600" : "border-transparent"}`}
              aria-label={`Show ${m.kind === "Video" ? "video" : "image"} ${i + 1}`}>
              {m.kind === "Video" ? (
                <>
                  {/* eslint-disable-next-line @next/next/no-img-element -- optimized poster */}
                  <img src={m.posterUrl ?? ""} alt="" className="h-full w-full object-cover" loading="lazy" />
                  <span className="absolute inset-0 flex items-center justify-center text-lg text-white drop-shadow">▶</span>
                </>
              ) : (
                <ProductImage image={m.image} alt="" sizes="64px" className="h-full w-full" />
              )}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
