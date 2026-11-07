import { BookOpenCheck, ChevronDown, Mail, Scale, ShieldCheck, Users } from 'lucide-react'
import { Link } from 'react-router-dom'
import { brand } from '../../app/brand'
import { PageHeader } from '../../components/Layout'
import { Card } from '../../components/ui'

const topics = [
  { icon: Users, title: 'Running a group', items: [
    ['How do I invite members?', 'Open the group, go to Members and create an invite link. Share it in your WhatsApp group. Links expire, can be revoked, and stop working when the group is full.'],
    ['Can I change the rules after members join?', 'No. Members accept a specific version of the rules. To change them, cancel the draft or start a new group.'],
    ['What happens when everyone has joined?', 'The group becomes ready to start. On the start date the full schedule of contributions and payouts is created.'],
  ] },
  { icon: BookOpenCheck, title: 'Recording payments', items: [
    ['What does “Recorded by organizer” mean?', 'The organizer says the money was paid. It is not a bank confirmation. The member can confirm it or raise a problem.'],
    ['How do I fix a mistake?', 'Open the record and choose to reverse it. The original stays in the history, linked to the correction.'],
    [`Does ${brand.name} move money?`, 'No. Members pay the way your group already pays. The app keeps the records.'],
  ] },
  { icon: Scale, title: 'Disputes', items: [
    ['How do I report a wrong record?', 'Open the record from the group and choose “Report a problem”. Only you and the organizer see the dispute.'],
    ['What if the organizer doesn’t respond?', 'After the waiting period, the member who raised it can escalate the dispute for independent review.'],
  ] },
  { icon: ShieldCheck, title: 'Your account', items: [
    ['Why do I need to verify my identity?', 'Everyone verifies their phone and an ID document. Organizers also give their BVN and a passport photo, because members rely on them.'],
    ['I forgot my password', 'Use “Forgot password” on the log-in page. The reset link works once and expires after 30 minutes.'],
  ] },
] as const

export function HelpPage() {
  return (
    <>
      <PageHeader title="Help" subtitle="Answers to common questions about groups, records and your account." />
      <div className="grid gap-6 lg:grid-cols-[1fr_300px]">
        <div className="space-y-6">
          {topics.map((t) => (
            <section key={t.title} aria-labelledby={`help-${t.title}`}>
              <h2 id={`help-${t.title}`} className="mb-3 flex items-center gap-2 text-sm font-semibold uppercase tracking-wider text-ink-soft"><t.icon aria-hidden className="size-4 text-teal" />{t.title}</h2>
              <Card flush>
                <div className="divide-y divide-line">
                  {t.items.map(([q, a]) => (
                    <details key={q} className="group">
                      <summary className="flex min-h-12 cursor-pointer list-none items-center justify-between gap-4 px-4 py-3 text-sm font-medium sm:px-5 [&::-webkit-details-marker]:hidden">
                        {q}<ChevronDown aria-hidden className="size-4 shrink-0 text-ink-soft transition-transform group-open:rotate-180" />
                      </summary>
                      <p className="px-4 pb-4 text-sm leading-relaxed text-ink-soft sm:px-5">{a}</p>
                    </details>
                  ))}
                </div>
              </Card>
            </section>
          ))}
        </div>
        <aside>
          <Card>
            <Mail aria-hidden className="size-5 text-teal" />
            <h2 className="mt-2 font-semibold">Still need help?</h2>
            <p className="mt-1 text-sm text-ink-soft">Email us. Never include passwords, codes, BVN or card details.</p>
            <a href={`mailto:${brand.supportEmail}`} className="mt-3 inline-flex min-h-11 items-center text-sm font-semibold text-teal underline-offset-4 hover:underline">{brand.supportEmail}</a>
            <p className="mt-3 text-xs text-ink-soft">Read our <Link to="/financial-services-disclaimer" className="font-semibold underline">financial services disclaimer</Link>.</p>
          </Card>
        </aside>
      </div>
    </>
  )
}
