import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App.jsx'
import StaffGate from './auth/StaffGate.jsx'
import './styles.css'

// http://localhost:5173/  -> staff dashboard (sign-in first).
// (The visitor phone app will be added next to it in phase 3.)
function Root() {
  return <StaffGate>{(me, signOut) => <App me={me} onSignOut={signOut} />}</StaffGate>
}

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <Root />
  </React.StrictMode>,
)
