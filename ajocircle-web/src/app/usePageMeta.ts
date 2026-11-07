import { useEffect } from 'react'
import { brand } from './brand'

/** Sets the document title and meta description for a page. */
export function usePageMeta(title: string, description?: string) {
  useEffect(() => {
    document.title = title ? `${title} · ${brand.name}` : `${brand.name} — ${brand.tagline}`
    if (!description) return
    let tag = document.querySelector<HTMLMetaElement>('meta[name="description"]')
    if (!tag) { tag = document.createElement('meta'); tag.name = 'description'; document.head.appendChild(tag) }
    tag.content = description
  }, [title, description])
}
