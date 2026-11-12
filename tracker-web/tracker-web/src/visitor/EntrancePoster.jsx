import { QRCodeSVG } from 'qrcode.react'
import { Logo } from './ui.jsx'
import './visitor.css'

// A printable poster for the entrance: http://<server>:5173/#/poster
// The QR opens the visitor app at the same address this page was opened from,
// so open it using the server's network address (not "localhost") before printing.
export default function EntrancePoster() {
  const url = `${window.location.origin}${window.location.pathname}#/visit`
  const isLocalhost = ['localhost', '127.0.0.1', '[::1]'].includes(window.location.hostname)

  return (
    <main className="v-poster">
      <div className="v-poster__card">
        <Logo large />
        <h1>Log your visit here</h1>
        <p>Scan with your phone camera to log your visit and pick a computer.</p>
        <div className="v-poster__qr">
          <QRCodeSVG value={url} size={300} level="M" marginSize={2} title="Visitor registration" />
        </div>
        <p className="v-poster__url">{url}</p>
        <p style={{ marginTop: 12 }}>DICT Digital Transformation Center, Baguio City</p>

        <div className="v-poster__print">
          {isLocalhost && (
            <p className="v-alert v-alert--error" style={{ marginBottom: 12 }}>
              This page was opened as "localhost", so phones cannot use this QR code. Open it with the server's
              network address (for example http://192.168.1.10:5173/#/poster) before printing.
            </p>
          )}
          <button className="v-button v-button--primary" onClick={() => window.print()}>Print poster</button>
        </div>
      </div>
    </main>
  )
}
