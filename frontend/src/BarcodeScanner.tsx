import { useEffect, useRef, useState } from 'react'
import { Camera, CameraOff } from 'lucide-react'

type Detector={detect:(source:HTMLVideoElement)=>Promise<Array<{rawValue:string}>>}
type DetectorConstructor=new(options:{formats:string[]})=>Detector
export default function BarcodeScanner({onDetected,onError}:{onDetected:(value:string)=>void;onError:(message:string)=>void}){
 const video=useRef<HTMLVideoElement>(null),stream=useRef<MediaStream|null>(null),[running,setRunning]=useState(false)
 useEffect(()=>()=>{stream.current?.getTracks().forEach(track=>track.stop())},[])
 async function start(){try{const Detector=(window as unknown as {BarcodeDetector?:DetectorConstructor}).BarcodeDetector;if(!Detector){onError('Bu tarayıcı kamera barkod okumayı desteklemiyor. Barkodu elle yazabilirsin.');return}stream.current=await navigator.mediaDevices.getUserMedia({video:{facingMode:{ideal:'environment'}},audio:false});if(video.current){video.current.srcObject=stream.current;await video.current.play()}setRunning(true);const detector=new Detector({formats:['ean_13','ean_8','code_128','code_39','qr_code']});const scan=async()=>{if(!stream.current)return;try{if(video.current){const found=await detector.detect(video.current);if(found[0]?.rawValue){onDetected(found[0].rawValue);stop();return}}}catch{}requestAnimationFrame(scan)};requestAnimationFrame(scan)}catch{onError('Kamera açılamadı. Tarayıcı kamera iznini kontrol et veya barkodu elle yaz.')}}
 function stop(){stream.current?.getTracks().forEach(track=>track.stop());stream.current=null;setRunning(false)}
 return <div className="barcode-scanner"><button type="button" className="secondary-button" onClick={running?stop:start}>{running?<CameraOff size={15}/>:<Camera size={15}/>} {running?'Kamerayı kapat':'Kamerayla okut'}</button><video ref={video} className={running?'scanner-video':'scanner-video hidden'} playsInline muted aria-label="Barkod kamera görüntüsü"/></div>
}
