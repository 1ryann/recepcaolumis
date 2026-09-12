import '@testing-library/jest-dom/vitest'
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'

afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); localStorage.clear() })

// jsdom ships no <canvas> rendering backend (getContext('2d') logs "Not implemented" and
// returns null without the optional native `canvas` package). Only cropToBlob.ts's
// getCroppedImageBlob relies on a real 2D context + toBlob output, so stub both minimally:
// a no-op drawImage is enough since the test only asserts that a Blob comes out the other
// end, not on actual pixel content.
if (typeof HTMLCanvasElement !== 'undefined') {
  HTMLCanvasElement.prototype.getContext = vi.fn(() => ({ drawImage: vi.fn() })) as unknown as typeof HTMLCanvasElement.prototype.getContext
  HTMLCanvasElement.prototype.toBlob = function toBlob(callback: BlobCallback, type?: string) {
    queueMicrotask(() => callback(new Blob(['test-image'], { type: type ?? 'image/png' })))
  }
}

// jsdom implements no image decoding pipeline at all: <img>/`new Image()` elements never
// get real naturalWidth/naturalHeight, and never fire their native 'load' event on their
// own. That's invisible to every component in this app except the two added for the photo
// crop feature (react-easy-crop's <Cropper>, which sizes itself off its <img onLoad>, and
// cropToBlob.ts's own `new Image()` load), so nothing else in the suite relies on this.
// Patch the 'src' setter to give it a fixed natural size and asynchronously dispatch
// 'load' once a source is assigned, so those onload/onLoad handlers actually get called.
if (typeof HTMLImageElement !== 'undefined') {
  const proto = HTMLImageElement.prototype
  const descriptor = Object.getOwnPropertyDescriptor(proto, 'src')
  if (descriptor?.set && descriptor.get) {
    Object.defineProperty(proto, 'src', {
      configurable: true,
      get() { return descriptor.get!.call(this) },
      set(value: string) {
        descriptor.set!.call(this, value)
        if (!value) return
        Object.defineProperty(this, 'naturalWidth', { value: 400, configurable: true })
        Object.defineProperty(this, 'naturalHeight', { value: 400, configurable: true })
        queueMicrotask(() => this.dispatchEvent(new Event('load')))
      },
    })
  }
}
