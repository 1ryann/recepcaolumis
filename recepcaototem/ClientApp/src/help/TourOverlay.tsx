import { useEffect, useRef } from 'react'
import { useTour } from './TourContext'
import { type Retangulo, useTourTarget } from './useTourTarget'

const margem = 14

function elementoEditavel(alvo: EventTarget | null) {
  if (!(alvo instanceof HTMLElement)) return false
  return alvo.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(alvo.tagName)
}

function largura() {
  return Math.min(360, window.innerWidth - 32)
}

function posicionar(retangulo: Retangulo) {
  const width = largura()
  const abaixo = retangulo.top + retangulo.height + margem
  const cabeAbaixo = abaixo + 200 < window.innerHeight
  const top = cabeAbaixo ? abaixo : Math.max(16, retangulo.top - 200 - margem)
  const left = Math.min(Math.max(16, retangulo.left), Math.max(16, window.innerWidth - width - 16))
  return { top, left, width }
}

// Sem âncora visível o cartão não tem a que se encostar, então fica no centro.
function centralizar() {
  const width = largura()
  return { top: Math.max(16, Math.round(window.innerHeight / 2 - 130)), left: Math.max(16, Math.round((window.innerWidth - width) / 2)), width }
}

export function TourOverlay() {
  const { trilha, passo, indice, total, avancar, voltar, pular } = useTour()
  const { elemento, retangulo, procurando, resolvido, medido } = useTourTarget(passo?.alvo)
  const cartao = useRef<HTMLDivElement | null>(null)
  const ausente = Boolean(passo?.alvo) && !procurando && elemento === null && resolvido === passo?.alvo
  // `resolvido` diz a que alvo este resultado pertence: sem ele, o cartão de um passo
  // apareceria medido pelo alvo do passo anterior.
  const visivel = Boolean(passo) && resolvido === passo?.alvo && elemento !== null && medido

  useEffect(() => { if (ausente) avancar() }, [ausente, passo?.id])

  useEffect(() => {
    if (!trilha) return
    const teclado = (evento: KeyboardEvent) => {
      if (evento.key === 'Escape') { evento.preventDefault(); pular(); return }
      if ((evento.key === 'ArrowRight' || evento.key === 'ArrowLeft') && elementoEditavel(evento.target)) return
      if (evento.key === 'ArrowRight') { evento.preventDefault(); avancar() }
      if (evento.key === 'ArrowLeft') { evento.preventDefault(); voltar() }
      if (evento.key !== 'Tab' || !cartao.current) return
      const foco = Array.from(cartao.current.querySelectorAll<HTMLElement>('button'))
      if (foco.length === 0) return
      const primeiro = foco[0]
      const ultimo = foco[foco.length - 1]
      const dentro = foco.includes(document.activeElement as HTMLElement)
      if (!evento.shiftKey && (!dentro || document.activeElement === ultimo)) { evento.preventDefault(); primeiro.focus() }
      if (evento.shiftKey && (!dentro || document.activeElement === primeiro)) { evento.preventDefault(); ultimo.focus() }
    }
    document.addEventListener('keydown', teclado)
    return () => document.removeEventListener('keydown', teclado)
  }, [trilha, passo?.id])

  useEffect(() => { if (visivel) cartao.current?.focus() }, [passo?.id, visivel])

  if (!trilha || !passo || !visivel) return null
  const caixa = retangulo ? posicionar(retangulo) : centralizar()
  const ultimo = indice + 1 >= total

  return (
    <div className="tour-overlay">
      {retangulo && <div className="tour-spotlight" style={{ top: retangulo.top - 6, left: retangulo.left - 6, width: retangulo.width + 12, height: retangulo.height + 12 }} />}
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
