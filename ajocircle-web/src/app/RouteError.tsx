import { isRouteErrorResponse, Link, useRouteError } from 'react-router-dom'
import { ctaPrimary, ctaSecondary } from '../components/publicStyles'
import { NotFoundPage, SystemMessage } from '../pages/PublicPages'

/** Global error boundary for every route. Never shows stack traces or server details to users. */
export function RouteError() {
  const err = useRouteError()
  if (isRouteErrorResponse(err) && err.status === 404) return <NotFoundPage />
  // A deploy can remove old lazy-loaded chunks; a reload fetches the new ones.
  const chunk = err instanceof Error && /dynamically imported module|Loading chunk|Importing a module script failed/i.test(err.message)
  return (
    <div className="min-h-dvh bg-paper">
      <SystemMessage code={chunk ? 'Update available' : 'Error'} title={chunk ? 'A new version is available' : 'Something went wrong'}
        text={chunk ? 'Reload the page to continue.' : 'This page could not be shown. Your records are safe. Try again, or go back to your dashboard.'}
        action={<>
          <button type="button" onClick={() => window.location.reload()} className={ctaPrimary}>Reload</button>
          <Link to="/app/dashboard" reloadDocument className={ctaSecondary}>Go to dashboard</Link>
        </>} />
    </div>
  )
}
