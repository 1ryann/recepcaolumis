import { useEffect, useState } from 'react'

export type Retangulo = { top: number; left: number; width: number; height: number }

export const limiteAlvoEmMs = 1500

export function aguardarAlvo(alvo: string, limite = limiteAlvoEmMs) {
  const seletor = `[data-tour="${alvo}"]`
  return new Promise<HTMLElement | null>(resolve => {
    const imediato = document.querySelector<HTMLElement>(seletor)
    if (imediato) return resolve(imediato)
    const observador = new MutationObserver(() => {
      const encontrado = document.querySelector<HTMLElement>(seletor)
      if (!encontrado) return
      observador.disconnect()
      clearTimeout(prazo)
      resolve(encontrado)
    })
    const prazo = setTimeout(() => { observador.disconnect(); resolve(null) }, limite)
    observador.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-tour'] })
  })
}

function foraDaTela(retangulo: Retangulo) {
  if (retangulo.width === 0 && retangulo.height === 0) return false
  const direita = retangulo.left + retangulo.width
  const baixo = retangulo.top + retangulo.height
  return direita <= 0 || baixo <= 0 || retangulo.left >= window.innerWidth || retangulo.top >= window.innerHeight
}

export function useTourTarget(alvo: string | undefined) {
  const [elemento, setElemento] = useState<HTMLElement | null>(null)
  const [retangulo, setRetangulo] = useState<Retangulo | null>(null)
  const [procurando, setProcurando] = useState(false)
  const [resolvido, setResolvido] = useState<string | undefined>(undefined)

  useEffect(() => {
    setElemento(null)
    setRetangulo(null)
    setResolvido(undefined)
    if (!alvo) { setProcurando(false); return }
    let cancelado = false
    setProcurando(true)
    void aguardarAlvo(alvo).then(encontrado => {
      if (cancelado) return
      setElemento(encontrado)
      setResolvido(alvo)
      setProcurando(false)
    })
    return () => { cancelado = true }
  }, [alvo])

  useEffect(() => {
    if (!elemento) return
    const medir = () => {
      const caixa = elemento.getBoundingClientRect()
      const proximo = { top: caixa.top, left: caixa.left, width: caixa.width, height: caixa.height }
      if (foraDaTela(proximo)) { setElemento(null); setRetangulo(null); return }
      setRetangulo(proximo)
    }
    elemento.scrollIntoView?.({ block: 'center' })
    medir()
    const observador = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(medir)
    observador?.observe(elemento)
    window.addEventListener('resize', medir)
    window.addEventListener('scroll', medir, true)
    return () => {
      observador?.disconnect()
      window.removeEventListener('resize', medir)
      window.removeEventListener('scroll', medir, true)
    }
  }, [elemento])

  return { elemento, retangulo, procurando, resolvido }
}
