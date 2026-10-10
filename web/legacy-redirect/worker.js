// The old address of the site, inkwell.ahmadnadeem.dev, now only sends visitors on to the new one.
// Every address keeps its path and query: /read/some-post on the old host goes to /read/some-post on
// articles.ahmadnadeem.dev, so every link ever shared or emailed keeps working. Keep this for good.
//
// Deployed by `npx wrangler deploy` in this folder, under the old Worker's name (inkwell), which is
// what holds the old address. It has no files of its own.

const TARGET = 'https://articles.ahmadnadeem.dev'

/**
 * Where a request to the old address should go, or null if it cannot be sent anywhere safe.
 * The path is attached to the new origin as plain text, never resolved against it, so a path such as
 * //other.example/x stays a path on the new site instead of becoming a link to another site.
 */
export function redirectTarget(requestUrl) {
  const url = new URL(requestUrl)
  const next = new URL(`${TARGET}${url.pathname}${url.search}`)
  // A second line of defence: whatever the path was, the answer must still be on the new site.
  return next.origin === new URL(TARGET).origin ? next.toString() : null
}

export default {
  async fetch(request) {
    const to = redirectTarget(request.url)
    if (!to) return new Response('Not found\n', { status: 404, headers: { 'Content-Type': 'text/plain; charset=utf-8' } })

    // 301 for reading; 308 keeps the method and body for anything else (nobody posts here, but it is correct).
    const status = request.method === 'GET' || request.method === 'HEAD' ? 301 : 308
    return new Response(null, { status, headers: { Location: to, 'Cache-Control': 'public, max-age=3600' } })
  },
}
