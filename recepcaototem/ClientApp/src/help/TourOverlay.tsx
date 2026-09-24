import { useEffect, useRef } from 'react'
import { useTour } from './TourContext'
import { type Retangulo, useTourTarget } from './useTourTarget'

const margem = 14

function posicionar(retangulo: Retangulo) {
  const largura = Math.min(360, window.innerWidth - 32)
  const abaixo = retangulo.top + retangulo.height + margem
  const cabeAbaixo = abaixo + 200 < window.innerHeight
  const top = cabeAbaixo ? abaixo : Math.max(16, retangulo.top - 200 - margem)
  const left = Math.min(Math.max(16, retangulo.left), Math.max(16, window.innerWidth - largura - 16))
  return { top, left, width: largura }
}

export function TourOverlay() {
  const { trilha, passo, indice, total, avancar, voltar, pular } = useTour()
  const { elemento, retangulo, procurando } = useTourTarget(passo?.alvo)
  const cartao = useRef<HTMLDivElement | null>(null)
  const buscaIniciada = useRef<string | undefined>(undefined)
  const ausente = Boolean(passo) && !procurando && elemento === null && buscaIniciada.current === passo?.alvo

  useEffect(() => { if (procurando) buscaIniciada.current = passo?.alvo }, [procurando, passo?.alvo])

  useEffect(() => { if (ausente) avancar() }, [ausente, passo?.id])

  useEffect(() => {
    if (!trilha) return
    const teclado = (evento: KeyboardEvent) => {
      if (evento.key === 'Escape') { evento.preventDefault(); pular() }
      if (evento.key === 'ArrowRight') { evento.preventDefault(); avancar() }
      if (evento.key === 'ArrowLeft') { evento.preventDefault(); voltar() }
      if (evento.key !== 'Tab' || !cartao.current) return
      const foco = cartao.current.querySelectorAll<HTMLElement>('button')
      if (foco.length === 0) return
      const primeiro = foco[0]
      const ultimo = foco[foco.length - 1]
      if (!evento.shiftKey && document.activeElement === ultimo) { evento.preventDefault(); primeiro.focus() }
      if (evento.shiftKey && document.activeElement === primeiro) { evento.preventDefault(); ultimo.focus() }
    }
    document.addEventListener('keydown', teclado)
    return () => document.removeEventListener('keydown', teclado)
  }, [trilha, passo?.id])

  useEffect(() => { cartao.current?.focus() }, [passo?.id])

  if (!trilha || !passo || !retangulo) return null
  const caixa = posicionar(retangulo)
  const ultimo = indice + 1 >= total

  return (
    <div className="tour-overlay">
      <div className="tour-spotlight" style={{ top: retangulo.top - 6, left: retangulo.left - 6, width: retangulo.width + 12, height: retangulo.height + 12 }} />
      <div className="tour-card" ref={cartao} tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby="tour-titulo" style={caixa}>
        <span className="tour-progresso" aria-live="polite">Passo {indice + 1} de {total}</span>
        <h2 id="tour-titulo">{passo.titulo}</h2>
        <p>{passo.texto}</p>
        <div className="tour-acoes">
          <button className="ghost-button" type="button" onClick={pular}>Pular tutorial</button>
          <div className="tour-navegacao">
            {indice > 0 && <button className="secondary-button" type="button" onClick={voltar}>Voltar</button>}
            <button className="primary-button" type="button" onClick={avancar}>{ultimo ? 'Concluir' : 'Avançar'}</button>
          </div>
        </div>
      </div>
    </div>
  )
}
