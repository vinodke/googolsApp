import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { Icon } from '../../app/Icons'
import { api, errorMessage, fieldErrors } from '../../core/api/client'
import type { MyClassroom, ParentCode, StudentParent } from '../../core/api/types'
import { strings } from '../../core/i18n/strings'
import { useStudentMe } from '../../core/queries'

/** Student side of parent linking: create a one-time code and see who is linked. */
export function FamilyPage() {
  const me = useStudentMe()
  const t = strings[me.data?.preferredLanguage ?? 'en']
  const parents = useQuery({ queryKey: ['student', 'parents'], queryFn: () => api.get<StudentParent[]>('/students/me/parents') })
  const code = useMutation({ mutationFn: () => api.post<ParentCode>('/students/me/parent-code') })

  return (
    <div className="stack-lg">
      <header className="page-head">
        <div>
          <div className="crumbs"><Link to="/">{t.navHome}</Link><Icon name="chevron" size={14} /><span>{t.navFamily}</span></div>
          <h1>{t.familyTitle}</h1>
          <p className="muted">{t.familyIntro}</p>
        </div>
      </header>

      {me.data?.consentRequired && <div className="alert alert-info" role="status">{t.consentNeeded}</div>}

      <div className="grid-2">
        <section className="card">
          <div className="row" style={{ marginBottom: 'var(--space-4)' }}>
            <span className="icon-tile lg tint-indigo"><Icon name="shield" /></span>
            <div>
              <h2 style={{ margin: 0 }}>{t.parentCodeTitle}</h2>
              <p className="muted small" style={{ margin: 0 }}>{t.parentCodeHelp}</p>
            </div>
          </div>
          <ol className="steps">
            <li>{t.parentStep1}</li>
            <li>{t.parentStep2}</li>
            <li>{t.parentStep3}</li>
          </ol>
          {code.data ? (
            <div className="code-box" aria-live="polite">
              <span className="muted small">{t.yourCode}</span>
              <strong className="code" data-testid="parent-code">{code.data.code}</strong>
              <span className="muted small">{t.codeExpires(new Date(code.data.expiresAtUtc).toLocaleDateString())}</span>
            </div>
          ) : null}
          {code.isError && <div className="alert" role="alert">{errorMessage(code.error)}</div>}
          <button className="btn btn-primary" onClick={() => code.mutate()} disabled={code.isPending}>
            <Icon name="plus" size={18} /> {code.data ? t.newCode : t.createCode}
          </button>
        </section>

        <section className="card">
          <h2>{t.linkedParents}</h2>
          {parents.isLoading && <div className="skeleton" style={{ height: 60 }} />}
          {parents.data?.length === 0 && <p className="muted small">{t.noParents}</p>}
          <ul className="list">
            {parents.data?.map(p => (
              <li key={p.linkId} className="list-item">
                <span className="icon-tile tint-teal"><Icon name="shield" size={18} /></span>
                <span className="grow"><strong style={{ textTransform: 'capitalize' }}>{p.relationship}</strong><span className="muted small" style={{ display: 'block' }}>{p.parentEmail}</span></span>
                {p.consentGranted ? <span className="badge badge-success">{t.consentGiven}</span> : <span className="badge badge-warning">{t.consentPending}</span>}
              </li>
            ))}
          </ul>
        </section>
      </div>

      <Classrooms />
    </div>
  )
}

function Classrooms() {
  const qc = useQueryClient()
  const [code, setCode] = useState('')
  const rooms = useQuery({ queryKey: ['student', 'classrooms'], queryFn: () => api.get<MyClassroom[]>('/students/me/classrooms') })
  const join = useMutation({
    mutationFn: () => api.post<{ id: string; name: string }>('/students/me/classrooms/join', { code }),
    onSuccess: () => { setCode(''); void qc.invalidateQueries({ queryKey: ['student', 'classrooms'] }) },
  })
  const leave = useMutation({ mutationFn: (id: string) => api.del(`/students/me/classrooms/${id}`), onSuccess: () => qc.invalidateQueries({ queryKey: ['student', 'classrooms'] }) })
  const errs = fieldErrors(join.error)
  function submit(e: FormEvent) { e.preventDefault(); join.mutate() }
  return (
    <section className="card">
      <div className="row" style={{ marginBottom: 'var(--space-3)' }}>
        <span className="icon-tile lg tint-teal"><Icon name="grad" /></span>
        <div><h2 style={{ margin: 0 }}>Classrooms</h2><p className="muted small" style={{ margin: 0 }}>Join your teacher's classroom, or your school division, with the code you're given. Your teachers will see your lessons, quiz and test scores.</p></div>
      </div>
      <ul className="list" style={{ marginBottom: 'var(--space-3)' }}>
        {rooms.data?.map(r => (
          <li key={r.id} className="list-item">
            <span className="grow"><strong>{r.name}</strong><span className="small muted" style={{ display: 'block' }}>{r.teacher}{r.schoolName ? ` · ${r.schoolName}` : ''}</span></span>
            <button className="btn btn-ghost btn-sm" onClick={() => leave.mutate(r.id)}>Leave</button>
          </li>
        ))}
      </ul>
      <form className="inline-form" onSubmit={submit}>
        <input className="input narrow" aria-label="Class code" placeholder="Class code" maxLength={10} value={code} onChange={e => setCode(e.target.value.toUpperCase())} />
        <button className="btn btn-primary btn-sm" disabled={code.trim().length < 6 || join.isPending}>Join</button>
        {(errs.code || (join.isError && !errs.code)) && <span className="field-error">{errs.code ?? errorMessage(join.error)}</span>}
        {join.data && <span className="badge badge-success">Joined {join.data.name}</span>}
      </form>
    </section>
  )
}
