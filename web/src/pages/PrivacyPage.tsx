import { useTitle } from '../hooks/useTitle'
import { PORTFOLIO_URL } from '../lib/site'
import { useSite } from '../lib/siteContext'

/** Changed whenever what the site collects changes. */
const LAST_UPDATED = '7 October 2026'

/**
 * What this site records about its readers. Kept to what the code actually does: if a feature that
 * touches reader data is added or removed, this page changes with it.
 */
export function PrivacyPage() {
  useTitle('Privacy')
  // The email section describes something that only exists once the site can send mail.
  const { subscribeEnabled } = useSite()

  return (
    <main className="main main--reading">
      <article className="prose legal">
        <h1 className="article__title">Privacy</h1>
        <p className="faint">Last updated {LAST_UPDATED}</p>

        <p>
          Articles is the personal blog of Ahmad Nadeem. There are no reader accounts, no advertising, and nothing
          here is sold or shared for marketing. This page lists everything the site records when you visit
          {subscribeEnabled && <>, and what it keeps if you choose to subscribe by email</>}.
        </p>

        <h2>What your browser stores</h2>
        <p>
          The first time you open an article, your browser makes up a random id and keeps it in its own storage for
          this site. It is not a cookie, it is not sent to any other site, and it does not say who you are. It exists
          so that a second click can undo a clap, and so that refreshing a page does not count you as a new reader.
          Clearing this site's data in your browser removes it.
        </p>
        <p>
          If you use the palette button to choose your own colours, those colours are kept in the same place so
          the site looks the same on your next visit. They stay on your device and are never sent to the site.
        </p>

        <h2>What the site records</h2>
        <ul>
          <li>
            <strong>Reactions.</strong> If you press Clap or Insightful, the site stores a scrambled, one-way form of
            your random id against that article. Pressing the button again deletes it.
          </li>
          <li>
            <strong>Feedback.</strong> After a reaction you may be offered a short card asking what you liked and for any
            suggestion. If you send it, the site stores the choices you picked, your words and which article they are about.
            No name, email or id is sent with it. Your browser remembers that you sent or closed the card, so it is not
            offered again on that article.
          </li>
          <li>
            <strong>Reader counts.</strong> Opening an article adds one to its reader count, at most once a day. So that
            you are not counted twice, and so the author can see how many readers come back, the scrambled id is kept with
            the article for up to 90 days and then deleted. Alongside each day's count the site keeps three coarse facts,
            as totals only and never linked to you: your country (a two-letter code from Cloudflare, never your address),
            whether you read on a phone, tablet or computer, and the name of the site you came from (such as google.com,
            never the full link).
          </li>
          <li>
            <strong>Visit statistics.</strong> Cloudflare Web Analytics counts page visits without cookies and without
            following you across sites. It shows totals such as which pages were opened and from which country.
          </li>
          <li>
            <strong>Server logs.</strong> Like every website, the servers briefly see your internet address in order to
            answer you and to stop abuse, such as one address sending hundreds of requests a minute. These logs are
            short-lived and are not used to identify readers.
          </li>
        </ul>

        {subscribeEnabled && (
          <>
        <h2>If you subscribe by email</h2>
        <p>
          Subscribing is optional. If you enter your email address, the site stores that address and nothing else
          about you: whether you have confirmed it, and when. It is used for one thing only, to send you a short
          email with a link when a new article is published.
        </p>
        <ul>
          <li>You are not subscribed until you click the link in the confirmation email, so nobody can sign you up without you.</li>
          <li>Every email has an unsubscribe link. After you unsubscribe, your address is kept only so that it is never emailed again by mistake.</li>
          <li>The emails are delivered by Brevo, a mail service, which receives your address in order to deliver them.</li>
          <li>Your address is never shown on the site, sold, or passed to anyone else.</li>
        </ul>
          </>
        )}

        <h2>What is not collected</h2>
        <p>
          No names, no passwords and no payment details: there is nowhere on this site to enter them
          {!subscribeEnabled && <>, and no email addresses either</>}. No advertising or social-media trackers are loaded.
        </p>

        <h2>Who runs the machines</h2>
        <p>
          The pages are served by Cloudflare. The articles and pictures come from a server at Render, and are stored
          in a database at Neon.{subscribeEnabled && <> Emails to subscribers are sent through Brevo.</>} These companies
          process requests on the site's behalf, under their own privacy terms.
        </p>

        <h2>Links to other sites</h2>
        <p>Articles link to other websites. Those sites have their own practices, which this page does not cover.</p>

        {PORTFOLIO_URL && (
          <>
            <h2>Questions</h2>
            <p>
              You can reach the author through the <a href={`${PORTFOLIO_URL}/contact`}>contact page</a>.
            </p>
          </>
        )}
      </article>
    </main>
  )
}
