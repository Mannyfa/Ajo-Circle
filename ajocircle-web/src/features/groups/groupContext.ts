import { useOutletContext } from 'react-router-dom'
import type { GroupDetail } from '../../types/api'

export type GroupCtx = { group: GroupDetail; refresh: () => void }
export const useGroup = () => useOutletContext<GroupCtx>()
export const groupKey = (id: string) => ['group', id] as const
