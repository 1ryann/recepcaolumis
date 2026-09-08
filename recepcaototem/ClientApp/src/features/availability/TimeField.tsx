import { Clock3 } from 'lucide-react'
import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { parseHm } from './timeRange'

type TimeFieldProps = {
  value: string
  onChange: (value: string) => void
  label: string
  disabled?: boolean
  min?: string
  max?: string
  step?: number
}

type Placement = 'right' | 'below' | 'left'
const PANEL_WIDTH = 264

const format = (minutes: number) =>
  `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`

function buildOptions(min: string, max: string, step: number) {
  const from = parseHm(min) ?? 5 * 60
  const to = parseHm(max) ?? 23 * 60 + 30
  const safeStep = step > 0 ? step : 30
  const out: string[] = []
  for (let minutes = from; minutes <= to && out.length < 96; minutes += safeStep) out.push(format(minutes))
  return out
}

export function TimeField({ value, onChange, label, disabled = false, min = '05:00', max = '23:30', step = 30 }: TimeFieldProps) {
  const rootRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const [open, setOpen] = useState(false)
  const [placement, setPlacement] = useState<Placement>('right')

  const options = buildOptions(min, max, step)

  useLayoutEffect(() => {
    if (!open || !rootRef.current) return
    const rect = rootRef.current.getBoundingClientRect()
    if (rect.right + PANEL_WIDTH + 16 <= window.innerWidth) setPlacement('right')
    else if (rect.left - PANEL_WIDTH - 16 >= 0) setPlacement('left')
    else setPlacement('below')
  }, [open])

  useEffect(() => {
    if (!open) return
    const onDocMouseDown = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDocMouseDown)
    return () => document.removeEventListener('mousedown', onDocMouseDown)
  }, [open])

  const pick = (option: string) => { onChange(option); setOpen(false) }

  return <div
    className="timefield"
    ref={rootRef}
    onKeyDown={(event) => { if (event.key === 'Escape') setOpen(false) }}
  >
    <input
      ref={inputRef}
      className="time-input timefield-input"
      type="text"
      inputMode="numeric"
      maxLength={5}
      placeholder="--:--"
      aria-label={label}
      value={value}
      disabled={disabled}
      onFocus={() => { if (!disabled) setOpen(true) }}
      onChange={(event) => onChange(event.target.value)}
    />
    <button
      type="button"
      className="timefield-open"
      aria-label={`Escolher ${label}`}
      disabled={disabled}
      onClick={() => { if (!disabled) setOpen((current) => !current) }}
    ><Clock3 size={15} /></button>
    {open && !disabled && <div className={`timefield-pop is-${placement}`} role="listbox" aria-label={`Horários de ${label}`}>
      <div className="timefield-grid">
        {options.map((option) => <button
          key={option}
          type="button"
          role="option"
          aria-selected={option === value}
          className={`timefield-opt ${option === value ? 'is-selected' : ''}`}
          onClick={() => pick(option)}
        >{option}</button>)}
      </div>
    </div>}
  </div>
}
