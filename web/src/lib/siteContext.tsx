import { createContext, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { api } from '../api/client'
import type { Category } from '../api/types'
import { applySiteLook } from './theme'

interface SiteState {
  /** Categories that have at least one published post, in the owner's order. Empty until loaded. */
  categories: Category[]
  /** Whether readers can subscribe by email. False until the API says the site can send mail. */
  subscribeEnabled: boolean
  /** The author's note shown at the end of every post, if there is one. */
  note: string | null
}

const EMPTY: SiteState = { categories: [], subscribeEnabled: false, note: null }
const SiteContext = createContext<SiteState>(EMPTY)

/**
 * Loads the two things that shape the whole site: its theme and its categories. Neither is needed
 * to read a post, so a failure here is silent: the site keeps its remembered theme and simply shows
 * no category tabs.
 */
export function SiteProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<SiteState>(EMPTY)

  useEffect(() => {
    let cancelled = false
    api.site()
      .then((site) => {
        if (cancelled) return
        applySiteLook(site.theme, site.colors)
        setState({
          categories: site.categories.filter((category) => category.postCount > 0),
          subscribeEnabled: site.subscribeEnabled === true,
          note: typeof site.note === 'string' && site.note.trim() !== '' ? site.note : null,
        })
      })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  return <SiteContext.Provider value={state}>{children}</SiteContext.Provider>
}

export function useSite() {
  return useContext(SiteContext)
}
