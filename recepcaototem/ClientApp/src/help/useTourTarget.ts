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

export function useTourTarget(alvo: string | undefined) {
  const [elemento, setElemento] = useState<HTMLElement | null>(null)
  const [retangulo, setRetangulo] = useState<Retangulo | null>(null)
  const [procurando, setProcurando] = useState(false)

  useEffect(() => {
    setElemento(null)
    setRetangulo(null)
    if (!alvo) { setProcurando(false); return }
    let cancelado = false
    setProcurando(true)
    void aguardarAlvo(alvo).then(encontrado => {
      if (cancelado) return
      setElemento(encontrado)
      setProcurando(false)
    })
    return () => { cancelado = true }
  }, [alvo])

  useEffect(() => {
    if (!elemento) return
    const medir = () => {
      const caixa = elemento.getBoundingClientRect()
      setRetangulo({ top: caixa.top, left: caixa.left, width: caixa.width, height: caixa.height })
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

  return { elemento, retangulo, procurando }
}
