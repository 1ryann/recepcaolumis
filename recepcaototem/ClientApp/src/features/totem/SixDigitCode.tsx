import { useRef } from 'react'
import { isComplete6, onlyDigits6 } from './digits6'

// Six visual cells backed by one hidden controlled <input> (inputMode numeric, one-time-code,
// paste-friendly, leading zeros preserved). Clicking anywhere focuses the input. Enter fires
// onSubmit only when the value is complete. Accessible name: "código de 6 dígitos".
export function SixDigitCode({
  value,
  onChange,
  onSubmit,
  disabled,
  id = 'totem-code',
  label = 'Código de 6 dígitos',
}: {
  value: string
  onChange: (v: string) => void
  onSubmit?: () => void
  disabled?: boolean
  id?: string
  label?: string
}) {
  const ref = useRef<HTMLInputElement>(null)
  const digits = value.split('')
  return (
    <div className="totem-code-boxes" onClick={() => ref.current?.focus()}>
      <label className="sr-only" htmlFor={id}>{label}</label>
      <input
        ref={ref}
        id={id}
        className="totem-code-hidden-input"
        value={value}
        onChange={(e) => onChange(onlyDigits6(e.target.value))}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && isComplete6(value)) {
            e.preventDefault()
            onSubmit?.()
          }
        }}
        inputMode="numeric"
        autoComplete="one-time-code"
        maxLength={6}
        spellCheck={false}
        aria-label={label}
        disabled={disabled}
      />
      {Array.from({ length: 6 }, (_, i) => (
        <span
          key={i}
          className={`totem-code-cell${i === value.length ? ' is-active' : ''}`}
          aria-hidden="true"
        >
          {digits[i] ?? ''}
        </span>
      ))}
    </div>
  )
}
