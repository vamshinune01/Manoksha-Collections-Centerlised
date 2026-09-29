import type { ImageUrls } from "@/lib/types";

/**
 * Responsive product image from pre-optimized WebP renditions: the browser picks the smallest size that fits (phones never
 * download the large one). Plain <img> on purpose — the files are already optimized in Cloud Storage.
 */
export function ProductImage({ image, alt, sizes, className, priority }: { image: ImageUrls | null | undefined; alt: string; sizes: string; className?: string; priority?: boolean }) {
  if (!image) {
    return <div className={`flex items-center justify-center bg-gradient-to-br from-brand-50 to-slate-100 text-3xl font-semibold text-brand-500/70 ${className ?? ""}`}>{alt.slice(0, 1)}</div>;
  }
  const w = (max: number) => Math.min(max, image.width);
  return (
    // eslint-disable-next-line @next/next/no-img-element -- renditions are already optimized WebP files
    <img
      src={image.medium}
      srcSet={`${image.thumb} ${w(400)}w, ${image.medium} ${w(900)}w, ${image.large} ${w(1600)}w`}
      sizes={sizes}
      alt={image.altText ?? alt}
      width={image.width}
      height={image.height}
      loading={priority ? "eager" : "lazy"}
      decoding="async"
      className={`bg-slate-100 object-cover ${className ?? ""}`}
    />
  );
}
