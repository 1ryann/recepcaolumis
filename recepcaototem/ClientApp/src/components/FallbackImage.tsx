import { type ImgHTMLAttributes, type ReactNode, useEffect, useState } from 'react'

// A private photo can be listed (hasPhoto) yet fail to load — file missing from storage,
// 503 while storage is down. The browser then paints its broken-image glyph plus the alt
// text inside a 40px avatar. This swaps in the caller's fallback (initials) instead.
export function FallbackImage({ src, fallback, ...rest }: ImgHTMLAttributes<HTMLImageElement> & { src: string; fallback: ReactNode }) {
  const [failed, setFailed] = useState(false)
  useEffect(() => setFailed(false), [src])
  if (failed) return <>{fallback}</>
  return <img src={src} {...rest} onError={() => setFailed(true)} />
}
