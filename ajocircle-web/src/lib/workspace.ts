import type { Me } from '../types/api'

/** Where an account lives: organizers in /app, members in /member (owner decision, 7 Nov 2026). */
export const homeFor = (me: Pick<Me, 'role'> | null | undefined) => (me?.role === 'Member' ? '/member/dashboard' : '/app/dashboard')

/**
 * Shared screens (group pages, records, disputes) are written with /app paths. In the member workspace they render
 * under /member instead, so links stay inside the workspace the person is using.
 */
export function wsPath(path: string) {
  return typeof window !== 'undefined' && window.location.pathname.startsWith('/member') ? path.replace(/^\/app(?=\/|$)/, '/member') : path
}
