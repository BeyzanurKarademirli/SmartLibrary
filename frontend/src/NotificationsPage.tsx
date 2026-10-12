import { useCallback, useEffect, useState } from 'react'
import { Bell, Check } from 'lucide-react'
import { usePreferences } from './preferences'
const API = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5180'
type Notice = { id: string; title: string; message: string; isRead: boolean; createdAt: string }
export default function NotificationsPage({ token, onError }: { token: string | null; onError: (s: string) => void }) {
  const { t, language } = usePreferences()
  const [items, setItems] = useState<Notice[]>([])
  const load = useCallback(async () => {
    if (!token) return
    const response = await fetch(`${API}/api/member/notifications`, { headers: { Authorization: `Bearer ${token}` } })
    if (!response.ok) throw new Error(t('Bildirimler yüklenemedi.'))
    setItems(await response.json() as Notice[])
  }, [token, t])
  useEffect(() => { load().catch(error => onError((error as Error).message)) }, [load, onError])
  async function markRead(id: string) {
    const response = await fetch(`${API}/api/member/notifications/${id}/read`, { method: 'POST', headers: { Authorization: `Bearer ${token}` } })
    if (!response.ok) return onError(t('Bildirim güncellenemedi.'))
    setItems(items.map(item => item.id === id ? { ...item, isRead: true } : item))
  }
  return <section className="subpage"><span className="eyebrow muted">{t('ÜYE ALANI')}</span><h1>{t('Bildirimler')}</h1><p className="subpage-description">{t('Rezervasyon ve hesap güncellemelerini burada takip et.')}</p><div className="list-panel">{items.length ? items.map(item => <article className={`notice-row ${item.isRead ? 'notice-read' : ''}`} key={item.id}><div className="stat-icon violet"><Bell size={17}/></div><div className="loan-info"><strong>{item.title}</strong><span>{item.message}</span><small>{new Intl.DateTimeFormat(language === 'tr' ? 'tr-TR' : 'en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(item.createdAt))}</small></div>{!item.isRead && <button className="table-action" onClick={() => markRead(item.id)}><Check size={14}/>{t('Okundu')}</button>}</article>) : <div className="empty-state"><div className="empty-icon"><Bell size={18}/></div><strong>{t('Yeni bildirim yok.')}</strong><span>{t('Güncellemelerin burada görünecek.')}</span></div>}</div></section>
}
