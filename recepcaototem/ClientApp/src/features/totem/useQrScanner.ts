import { useCallback, useEffect, useRef, useState } from 'react'
import type QrScannerLib from 'qr-scanner'

export type QrScannerState = 'idle' | 'starting' | 'scanning' | 'denied' | 'unsupported' | 'error'

// Wraps qr-scanner's camera lifecycle. The library is imported lazily so it only
// loads when the operator actually opens the camera. No timers: qr-scanner runs
// its own decode loop and we simply react to the first result.
export function useQrScanner(onDecode: (raw: string) => void) {
  const videoRef = useRef<HTMLVideoElement>(null)
  const scannerRef = useRef<QrScannerLib | null>(null)
  const onDecodeRef = useRef(onDecode)
  const [state, setState] = useState<QrScannerState>('idle')

  useEffect(() => { onDecodeRef.current = onDecode }, [onDecode])

  const stop = useCallback(() => {
    scannerRef.current?.stop()
    setState((current) => (current === 'scanning' || current === 'starting' ? 'idle' : current))
  }, [])

  const start = useCallback(async () => {
    if (!videoRef.current) return
    setState('starting')
    try {
      const { default: QrScanner } = await import('qr-scanner')
      if (!(await QrScanner.hasCamera())) { setState('unsupported'); return }
      if (!scannerRef.current) {
        scannerRef.current = new QrScanner(
          videoRef.current,
          (result) => { onDecodeRef.current(result.data); stop() },
          { preferredCamera: 'environment', highlightScanRegion: true, maxScansPerSecond: 5, returnDetailedScanResult: true },
        )
      }
      await scannerRef.current.start()
      setState('scanning')
    } catch (error) {
      const name = (error as { name?: string } | null)?.name
      setState(name === 'NotAllowedError' || name === 'SecurityError' ? 'denied' : 'error')
    }
  }, [stop])

  useEffect(() => () => {
    scannerRef.current?.destroy()
    scannerRef.current = null
  }, [])

  return { videoRef, state, start, stop }
}
