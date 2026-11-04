import React, { useEffect, useState } from 'react'
import ReactDOM from 'react-dom/client'
import App from './App.jsx'
import Kiosk from './kiosk/Kiosk.jsx'
import './styles.css'

// Two screens share one app, chosen by the part of the URL after "#":
//   http://localhost:5173/            -> staff dashboard
//   http://localhost:5173/#/kiosk/1   -> login screen for computer 1 (PC-01)
function Root() {
  const [hash, setHash] = useState(window.location.hash)

  useEffect(() => {
    const onChange = () => setHash(window.location.hash)
    window.addEventListener('hashchange', onChange)
    return () => window.removeEventListener('hashchange', onChange)
  }, [])

  return hash.startsWith('#/kiosk') ? <Kiosk hash={hash} /> : <App />
}

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <Root />
  </React.StrictMode>,
)
