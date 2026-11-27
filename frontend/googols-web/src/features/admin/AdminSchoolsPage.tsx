import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Icon } from '../../app/Icons'
import { api, errorMessage, fieldErrors } from '../../core/api/client'
import type { AdminSchool } from '../../core/api/types'
import './admin.css'

/** Platform admin: create schools and choose each school's admins. */
export function AdminSchoolsPage() {
  const qc = useQueryClient()
  const schools = useQuery({ queryKey: ['admin', 'schools'], queryFn: () => api.get<AdminSchool[]>('/admin/schools') })
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [city, setCity] = useState('')
  const refresh = () => qc.invalidateQueries({ queryKey: ['admin', 'schools'] })
  const create = useMutation({ mutationFn: () => api.post('/admin/schools', { name, code, city: city || null }), onSuccess: () => { setName(''); setCode(''); setCity(''); void refresh() } })
  const errors = fieldErrors(create.error)
  function submit(e: FormEvent) { e.preventDefault(); create.mutate() }
  return (
    <div className="stack-lg">
      <header className="page-head"><div><h1>Schools</h1><p className="muted">Each school's admins manage its divisions, staff and timetables. Schools can't see each other's data.</p></div></header>
      <form className="card stack" onSubmit={submit} aria-label="New school">
        <div className="inline-form" style={{ marginTop: 0 }}>
          <input className="input" aria-label="School name" placeholder="School name" value={name} onChange={e => setName(e.target.value)} />
          <input className="input narrow" aria-label="School code" placeholder="Code, e.g. GHSSKTM" value={code} onChange={e => setCode(e.target.value.toUpperCase())} />
          <input className="input narrow" aria-label="City" placeholder="City" value={city} onChange={e => setCity(e.target.value)} />
          <button className="btn btn-primary" disabled={!name.trim() || code.length < 3 || create.isPending}><Icon name="plus" size={16} /> Add school</button>
        </div>
        {Object.values(errors)[0] && <span className="field-error">{Object.values(errors)[0]}</span>}
      </form>
      {schools.isError && <div className="alert" role="alert">{errorMessage(schools.error)}</div>}
      {schools.data?.map(s => <SchoolCard key={s.id} s={s} onChanged={refresh} />)}
    </div>
  )
}

function SchoolCard({ s, onChanged }: { s: AdminSchool; onChanged: () => void }) {
  const [email, setEmail] = useState('')
  const add = useMutation({ mutationFn: () => api.post(`/admin/schools/${s.id}/admins`, { email }), onSuccess: () => { setEmail(''); onChanged() } })
  const remove = useMutation({ mutationFn: (userId: string) => api.del(`/admin/schools/${s.id}/admins/${userId}`), onSuccess: onChanged })
  const errors = fieldErrors(add.error)
  return (
    <section className="card stack">
      <div className="row-between"><div><strong>{s.name}</strong> <span className="badge">{s.code}</span><div className="small muted">{s.city ?? ''} · {s.divisions} divisions · {s.students} students</div></div></div>
      <div className="row" style={{ flexWrap: 'wrap' }}>
        {s.admins.length === 0 && <span className="small muted">No school admin yet.</span>}
        {s.admins.map(a => <span key={a.userId} className="badge">{a.email} <button className="icon-action" aria-label={`Remove ${a.email}`} onClick={() => remove.mutate(a.userId)}>×</button></span>)}
      </div>
      <form className="inline-form" style={{ marginTop: 0 }} onSubmit={e => { e.preventDefault(); add.mutate() }}>
        <input className="input" type="email" aria-label={`New admin for ${s.name}`} placeholder="Email of an existing Googols account" value={email} onChange={e => setEmail(e.target.value)} />
        <button className="btn btn-ghost btn-sm" disabled={!email.includes('@') || add.isPending}>Make school admin</button>
        {(errors.email || add.isError) && <span className="field-error">{errors.email ?? errorMessage(add.error)}</span>}
      </form>
    </section>
  )
}
