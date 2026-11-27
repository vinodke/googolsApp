import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { Icon } from '../../app/Icons'
import { api, errorMessage } from '../../core/api/client'
import type { CheckoutInfo, PlanInfo, SubscriptionInfo } from '../../core/api/types'
import './billing.css'

interface RazorpayResponse { razorpay_order_id: string; razorpay_payment_id: string; razorpay_signature: string }
interface RazorpayInstance { open(): void }
type RazorpayCtor = new (options: Record<string, unknown>) => RazorpayInstance
declare global { interface Window { Razorpay?: RazorpayCtor } }

function loadRazorpay(): Promise<RazorpayCtor> {
  if (window.Razorpay) return Promise.resolve(window.Razorpay)
  return new Promise((resolve, reject) => {
    const s = document.createElement('script')
    s.src = 'https://checkout.razorpay.com/v1/checkout.js'
    s.onload = () => (window.Razorpay ? resolve(window.Razorpay) : reject(new Error('Checkout did not load')))
    s.onerror = () => reject(new Error('Could not load the payment page. Check your connection.'))
    document.body.appendChild(s)
  })
}

const rupees = (paise: number) => `₹${(paise / 100).toLocaleString('en-IN')}`

/** Plans for the signed-in student, or for a linked child (?studentId=) when opened by a parent. */
export function PlansPage() {
  const [params] = useSearchParams()
  const studentId = params.get('studentId')
  const qs = studentId ? `?studentId=${studentId}` : ''
  const qc = useQueryClient()
  const [message, setMessage] = useState<string | null>(null)
  const plans = useQuery({ queryKey: ['billing', 'plans'], queryFn: () => api.get<PlanInfo[]>('/billing/plans') })
  const sub = useQuery({ queryKey: ['billing', 'subscription', studentId], queryFn: () => api.get<SubscriptionInfo>(`/billing/subscription${qs}`) })

  const buy = useMutation({
    mutationFn: async (planCode: string) => {
      const order = await api.post<CheckoutInfo>('/billing/checkout', { planCode, studentId })
      const Razorpay = await loadRazorpay()
      await new Promise<void>((resolve, reject) => {
        new Razorpay({
          key: order.keyId,
          order_id: order.orderId,
          amount: order.amountPaise,
          currency: order.currency,
          name: 'Googols',
          description: `${order.planName} for ${order.studentName}`,
          prefill: order.email ? { email: order.email } : undefined,
          theme: { color: '#4f46e5' },
          handler: async (r: RazorpayResponse) => {
            try {
              await api.post('/billing/verify', { orderId: r.razorpay_order_id, paymentId: r.razorpay_payment_id, signature: r.razorpay_signature })
              resolve()
            } catch (e) { reject(e) }
          },
          modal: { ondismiss: () => reject(new Error('Payment was cancelled.')) },
        }).open()
      })
    },
    onSuccess: () => { setMessage('Payment successful. Premium is now active.'); void qc.invalidateQueries({ queryKey: ['billing'] }) },
  })

  const current = sub.data?.planCode
  return (
    <div className="stack-lg">
      <header className="page-head">
        <div><h1>Plans</h1><p className="muted">Everything to learn is free. Premium adds more AI teacher questions every day.</p></div>
      </header>
      {sub.data && (
        <div className="alert alert-info">
          Current plan: <strong>{sub.data.planName}</strong> · {sub.data.aiQuestionsPerDay} AI questions a day
          {sub.data.endsAtUtc ? ` · until ${new Date(sub.data.endsAtUtc).toLocaleDateString()}` : ''}
        </div>
      )}
      {message && <div className="alert alert-info" role="status">{message}</div>}
      {buy.isError && <div className="alert" role="alert">{errorMessage(buy.error)}</div>}
      <div className="plans">
        {plans.data?.map(p => (
          <section key={p.code} className={`card plan${p.code === current ? ' current' : ''}${!p.isFree && p.durationDays >= 365 ? ' best' : ''}`}>
            {!p.isFree && p.durationDays >= 365 && <span className="badge badge-warning plan-flag">Best value</span>}
            <h2>{p.name}</h2>
            <div className="price">{p.isFree ? '₹0' : rupees(p.amountPaise)}<span className="muted small">{p.isFree ? ' forever' : ` / ${p.durationDays >= 365 ? 'year' : 'month'}`}</span></div>
            <ul className="plan-points">
              <li><Icon name="check" size={16} /> All lessons, quizzes and rewards</li>
              <li><Icon name="check" size={16} /> {p.aiQuestionsPerDay} AI teacher questions a day</li>
              <li><Icon name="check" size={16} /> Parent dashboard</li>
            </ul>
            {p.isFree
              ? <button className="btn btn-ghost" disabled>{current === 'free' ? 'Current plan' : 'Included'}</button>
              : <button className="btn btn-primary" disabled={!sub.data?.paymentsEnabled || buy.isPending} onClick={() => { setMessage(null); buy.mutate(p.code) }}>
                  {buy.isPending ? 'Opening payment…' : current === p.code ? 'Extend' : 'Upgrade'}
                </button>}
          </section>
        ))}
      </div>
      {sub.data && !sub.data.paymentsEnabled && <p className="muted small">Online payment isn't switched on yet.</p>}
      <p className="muted small">Payments are handled by Razorpay (UPI, cards, net banking). Googols never sees your card or UPI details.</p>
    </div>
  )
}
