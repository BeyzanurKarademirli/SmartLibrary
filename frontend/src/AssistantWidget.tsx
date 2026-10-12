import { useState } from 'react'
import type { FormEvent } from 'react'
import { BookOpen, LoaderCircle, Send, X } from 'lucide-react'
import { usePreferences } from './preferences'

const API = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5180'
type Turn = { role: 'user' | 'model'; text: string }

export default function AssistantWidget({ token }: { token: string | null }) {
  const { t, language } = usePreferences()
  const [open, setOpen] = useState(false)
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [unavailable, setUnavailable] = useState(false)
  const [turns, setTurns] = useState<Turn[]>([])
  const [error, setError] = useState('')
  async function send(event: FormEvent) {
    event.preventDefault()
    const message = input.trim()
    if (!message || !token || busy) return
    const next = [...turns, { role: 'user' as const, text: message }]
    setTurns(next); setInput(''); setError(''); setBusy(true)
    try {
      const response = await fetch(`${API}/api/assistant/chat`, { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: JSON.stringify({ message, language, history: turns.slice(-8) }) })
      const body = await response.json().catch(() => null)
      if (!response.ok) {
        if (response.status === 503) setUnavailable(true)
        throw new Error(body?.detail || body?.error || (language === 'en' ? 'The assistant could not respond.' : 'Asistan yanıt veremedi.'))
      }
      setTurns([...next, { role: 'model', text: body.reply }])
    } catch (cause) { setError((cause as Error).message) } finally { setBusy(false) }
  }
  return <>
    <button type="button" className="assistant-launch" onClick={() => setOpen(value => !value)} aria-label={t('Asistan')} title={t('Kütüphane asistanı')}><BookOpen size={19}/><span>{t('Asistan')}</span></button>
    {open && <section className="assistant-panel" aria-label={t('Kütüphane asistanı')}>
      <header className="assistant-header"><div className="assistant-avatar"><BookOpen size={17}/></div><div><strong>{t('Kütüphane asistanı')}</strong><span>{language === 'en' ? 'SmartLibrary help' : 'SmartLibrary desteği'}</span></div><button type="button" className="icon-button" onClick={() => setOpen(false)} aria-label={language === 'en' ? 'Close' : 'Kapat'}><X size={17}/></button></header>
      <div className="assistant-messages"><div className="assistant-message assistant-bot">{t('Merhaba! Kitap bulma, ödünç ve rezervasyon konularında yardımcı olabilirim.')}</div>{turns.map((turn, index) => <div key={index} className={`assistant-message ${turn.role === 'user' ? 'assistant-user' : 'assistant-bot'}`}>{turn.text}</div>)}{busy && <div className="assistant-message assistant-bot"><LoaderCircle className="assistant-spinner" size={15}/></div>}{unavailable && <p className="assistant-help">{t('AI servisi henüz yapılandırılmadı.')} {language === 'en' ? 'Create a free-tier key in ' : 'Ücretsiz kullanım için anahtarını ' }<a href="https://aistudio.google.com/app/apikey" target="_blank" rel="noreferrer">Google AI Studio</a>{language === 'en' ? ', add it as GEMINI_API_KEY in .env, then restart Docker.' : ' üzerinden oluşturup .env dosyasına GEMINI_API_KEY adıyla ekle ve Docker’ı yeniden başlat.'}</p>}{error && <p className="assistant-error">{error}</p>}</div>
      <p className="assistant-disclaimer">{t('Yapay zekâ yanıtları hatalı olabilir.')} {language === 'en' ? 'Avoid sharing personal data.' : 'Kişisel bilgi paylaşma.'}</p>
      <form className="assistant-form" onSubmit={send}><input value={input} onChange={event => setInput(event.target.value)} placeholder={t('Mesajını yaz...')} maxLength={1000} disabled={!token || busy || unavailable}/><button type="submit" className="assistant-send" aria-label={t('Gönder')} disabled={!input.trim() || !token || busy || unavailable}>{busy ? <LoaderCircle className="assistant-spinner" size={16}/> : <Send size={16}/>}</button></form>
      {!token && <p className="assistant-help">{language === 'en' ? 'Sign in to use the assistant.' : 'Asistanı kullanmak için giriş yap.'}</p>}
    </section>}
  </>
}
