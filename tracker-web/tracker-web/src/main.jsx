import React, { useEffect, useState } from 'react'
import ReactDOM from 'react-dom/client'
import App from './App.jsx'
import StaffGate from './auth/StaffGate.jsx'
import EntrancePoster from './visitor/EntrancePoster.jsx'
import VisitorApp from './visitor/VisitorApp.jsx'
import './styles.css'

// Three pages share one website, chosen by the part of the address after "#":
//   http://<server>:5173/          -> staff dashboard (sign-in first)
//   http://<server>:5173/#/visit   -> visitor phone app (the entrance QR code opens this)
//   http://<server>:5173/#/poster  -> printable entrance QR poster
function Root() {
  const [hash, setHash] = useState(window.location.hash)

  useEffect(() => {
    const onChange = () => setHash(window.location.hash)
    window.addEventListener('hashchange', onChange)
    return () => window.removeEventListener('hashchange', onChange)
  }, [])

  if (hash.startsWith('#/visit')) return <VisitorApp />
  if (hash.startsWith('#/poster')) return <EntrancePoster />
  return <StaffGate>{(me, signOut) => <App me={me} onSignOut={signOut} />}</StaffGate>
}

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <Root />
  </React.StrictMode>,
)
