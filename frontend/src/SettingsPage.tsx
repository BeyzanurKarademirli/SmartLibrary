import { useEffect, useState } from 'react'
import { Mail, Save, Settings } from 'lucide-react'
import { usePreferences } from './preferences'

const API = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5180'
type Values = { LoanPeriodDays: number; MaxActiveLoans: number; FinePerDay: number; ReservationPickupDays: number }
const defaults: Values = { LoanPeriodDays: 14, MaxActiveLoans: 5, FinePerDay: 5, ReservationPickupDays: 2 }

export default function SettingsPage({ token, onError, onToast }: { token: string | null; onError: (s: string) => void; onToast: (s: string) => void }) {
  const { t, language } = usePreferences()
  const [values, setValues] = useState(defaults)
  const [busy, setBusy] = useState(false)
  const [testing, setTesting] = useState(false)
  useEffect(() => {
    if (!token) return
    fetch(`${API}/api/staff/settings`, { headers: { Authorization: `Bearer ${token}` } }).then(async response => {
      if (!response.ok) throw new Error(t('Sistem ayarları alınamadı.'))
      const rows = await response.json() as Array<{ key: string; value: string }>
      const next = { ...defaults }
      for (const row of rows) if (row.key in next) next[row.key as keyof Values] = Number(row.value)
      setValues(next)
    }).catch(error => onError((error as Error).message))
  }, [token, onError, t])
  async function save() {
    if (!token) return
    setBusy(true)
    try {
      const response = await fetch(`${API}/api/staff/settings`, { method: 'PUT', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: JSON.stringify({ values }) })
      if (!response.ok) { const body = await response.json().catch(() => null); throw new Error(body?.error || t('Ayarlar kaydedilemedi.')) }
      onToast(t('Kütüphane kuralları kaydedildi.'))
    } catch (error) { onError((error as Error).message) } finally { setBusy(false) }
  }
  async function sendTestEmail() {
    if (!token) return
    setTesting(true)
    try {
      const response = await fetch(`${API}/api/admin/email/test`, { method: 'POST', headers: { Authorization: `Bearer ${token}` } })
      if (!response.ok) { const body = await response.json().catch(() => null); throw new Error(body?.detail || body?.error || t('E-posta testi gönderilemedi. SMTP ayarlarını ve uygulama şifresini kontrol edin.')) }
      const body = await response.json() as { message: string }
      onToast(body.message)
    } catch (error) { onError((error as Error).message) } finally { setTesting(false) }
  }
  const field = (key: keyof Values, label: string, help: string, min: number, max: number) => <label className="setting-field" key={key}><span><strong>{t(label)}</strong><small>{t(help)}</small></span><input type="number" min={min} max={max} value={values[key]} onChange={event => setValues({ ...values, [key]: Number(event.target.value) })} /></label>
  return <section className="subpage"><span className="eyebrow muted">{t('YÖNETİCİ')}</span><h1>{t('Sistem ayarları')}</h1><p className="subpage-description">{language === 'en' ? 'Manage loan and reservation policies for your library.' : 'Kütüphanenin ödünç ve rezervasyon kurallarını düzenle.'}</p>
    <div className="panel-card settings-card"><div className="panel-heading"><div><span className="eyebrow muted">{t('İŞLETME KURALLARI')}</span><h2>{t('Ödünç ve ceza politikası')}</h2></div><Settings size={18} /></div>
      <div className="settings-fields">{field('LoanPeriodDays', 'Ödünç süresi', 'Ödünç ve uzatma başına verilecek gün.', 1, 90)}{field('MaxActiveLoans', 'Üye başına aktif kitap', 'Aynı anda açık tutulabilecek ödünç sayısı.', 1, 30)}{field('FinePerDay', 'Günlük gecikme cezası (₺)', 'İade gecikmesinde gün başına uygulanır.', 0, 1000)}{field('ReservationPickupDays', 'Rezervasyon teslim süresi', 'Hazır rezervasyon için üyeye tanınan gün.', 1, 14)}</div>
      <div className="settings-actions"><span>{t('Değişiklikler yeni işlemlere uygulanır.')}</span><button className="primary-button" onClick={save} disabled={busy}><Save size={15} />{busy ? (language === 'en' ? 'Saving…' : 'Kaydediliyor…') : (language === 'en' ? 'Save settings' : 'Ayarları kaydet')}</button></div>
    </div>
    <div className="panel-card settings-note"><strong>{t('Güvenlik ve bildirimler')}</strong><p>{t('Kimlik doğrulama anahtarları Docker ortam değişkenlerinde yönetilir. Uygulama içi bildirimler etkindir. E-posta/SMS gönderimi için ayrıca bir sağlayıcı hesabı ve gizli anahtar yapılandırılması gerekir.')}</p><div className="settings-actions"><span>{language === 'en' ? 'Send a message to your own administrator email to verify SMTP.' : 'SMTP’yi doğrulamak için kendi yönetici e-posta adresine test iletisi gönder.'}</span><button className="secondary-button" onClick={sendTestEmail} disabled={testing}><Mail size={15} />{testing ? (language === 'en' ? 'Sending…' : 'Gönderiliyor…') : t('SMTP testi gönder')}</button></div></div>
  </section>
}
