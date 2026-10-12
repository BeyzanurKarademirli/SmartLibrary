import { useCallback, useEffect, useState } from 'react'
import { ArrowRight, Check, Clock3 } from 'lucide-react'
import { usePreferences } from './preferences'

const API = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5180'
type Reservation = { id:string; book:string; member:string; email:string; status:string; createdAt:string; pickupExpiresAt:string|null }

export default function StaffReservations({token,onRefresh,onToast,onError}:{token:string|null;onRefresh:()=>Promise<void>;onToast:(message:string)=>void;onError:(message:string)=>void}) {
  const {t,language}=usePreferences()
  const [rows,setRows]=useState<Reservation[]>([])
  const [busy,setBusy]=useState<string|null>(null)
  const date=(value:string)=>new Intl.DateTimeFormat(language==='tr'?'tr-TR':'en-US',{day:'numeric',month:'short'}).format(new Date(value))
  const refresh=useCallback(async()=>{
    if(!token)return
    const response=await fetch(`${API}/api/staff/reservations`,{headers:{Authorization:`Bearer ${token}`}})
    if(!response.ok)throw new Error(t('Rezervasyon kuyruğu yüklenemedi.'))
    setRows(await response.json() as Reservation[])
  },[token,t])
  useEffect(()=>{refresh().catch(e=>onError((e as Error).message))},[refresh,onError])
  async function checkout(row:Reservation){
    if(!token)return
    setBusy(row.id)
    try{
      const response=await fetch(`${API}/api/staff/reservations/${row.id}/checkout`,{method:'POST',headers:{Authorization:`Bearer ${token}`}})
      if(!response.ok){const result=await response.json().catch(()=>null);throw new Error(result?.error||t('Ödünç işlemi tamamlanamadı.'))}
      await refresh();await onRefresh();onToast(language==='en'?`Loan checked out for ${row.member}.`:`${row.member} için ödünç işlemi tamamlandı.`)
    }catch(e){onError((e as Error).message)}finally{setBusy(null)}
  }
  return <section className="subpage"><span className="eyebrow muted">{t('KÜTÜPHANECİ')}</span><h1>{t('Rezervasyon kuyruğu')}</h1><p className="subpage-description">{t('Teslim almaya hazır kitapları üyenin hesabına ödünç olarak geçir.')}</p><div className="panel-card"><div className="panel-heading"><div><span className="eyebrow muted">{t('BEKLEYEN İŞLEMLER')}</span><h2>{t('Üye rezervasyonları')}</h2></div><span className="category-tag">{rows.length} {language==='en'?'records':'kayıt'}</span></div><div className="table-wrap"><table><thead><tr><th>{t('KİTAP')}</th><th>{t('ÜYE')}</th><th>{t('REZERVASYON')}</th><th>{t('DURUM')}</th><th>{t('İŞLEM')}</th></tr></thead><tbody>{rows.map(row=><tr key={row.id}><td><strong>{t(row.book)}</strong></td><td>{row.member}<br/><span className="table-muted">{row.email}</span></td><td>{date(row.createdAt)}</td><td><span className={`status-pill ${row.status==='ReadyForPickup'?'status-active':'status-waiting'}`}><i/>{row.status==='ReadyForPickup'?t('Teslim almaya hazır'):t('Kopya bekliyor')}</span>{row.pickupExpiresAt&&<span className="pickup-deadline"><Clock3 size={12}/>{date(row.pickupExpiresAt)} {t('son gün')}</span>}</td><td>{row.status==='ReadyForPickup'?<button className="table-action" disabled={busy===row.id} onClick={()=>checkout(row)}>{busy===row.id?t('İşleniyor...'):t('Ödünç ver')} <ArrowRight size={13}/></button>:<span className="table-muted">{t('Kitap iadesi bekleniyor')}</span>}</td></tr>)}</tbody></table>{!rows.length&&<div className="empty-state"><div className="empty-icon"><Check size={19}/></div><strong>{t('Bekleyen rezervasyon yok.')}</strong><span>{t('Yeni rezervasyonlar burada görünecek.')}</span></div>}</div></div></section>
}
