import jsQR from 'jsqr'
import { useEffect, useRef, useState } from 'react'

// Reads a QR code with the computer's camera and calls onScan(text) once.
// Works on any browser with a camera (jsQR decodes the frames), but browsers
// only allow the camera on https:// or on localhost.
export default function QrScanner({ onScan, onCancel }) {
  const videoRef = useRef(null)
  const [error, setError] = useState('')
  const onScanRef = useRef(onScan)
  onScanRef.current = onScan

  useEffect(() => {
    let stream
    let frame
    let done = false
    const canvas = document.createElement('canvas')
    const context = canvas.getContext('2d', { willReadFrequently: true })

    function tick() {
      const video = videoRef.current
      if (done || !video) return
      if (video.readyState >= video.HAVE_ENOUGH_DATA && video.videoWidth > 0) {
        canvas.width = video.videoWidth
        canvas.height = video.videoHeight
        context.drawImage(video, 0, 0, canvas.width, canvas.height)
        const image = context.getImageData(0, 0, canvas.width, canvas.height)
        const code = jsQR(image.data, image.width, image.height, { inversionAttempts: 'dontInvert' })
        if (code?.data) {
          done = true
          onScanRef.current(code.data.trim())
          return
        }
      }
      frame = requestAnimationFrame(tick)
    }

    async function start() {
      if (!navigator.mediaDevices?.getUserMedia) {
        setError('This browser cannot use the camera here. Open the dashboard on localhost or https, or type the 6-digit code.')
        return
      }
      try {
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: 'environment' },
          audio: false,
        })
        if (done) {
          stream.getTracks().forEach((t) => t.stop()) // closed before the camera started
          return
        }
        const video = videoRef.current
        video.srcObject = stream
        await video.play()
        frame = requestAnimationFrame(tick)
      } catch (e) {
        setError(
          e?.name === 'NotAllowedError'
            ? 'Camera permission was refused. Allow the camera for this site, or type the 6-digit code.'
            : e?.name === 'NotFoundError'
              ? 'No camera found. Type the 6-digit code instead.'
              : `Cannot start the camera (${e?.message ?? e}). Type the 6-digit code instead.`,
        )
      }
    }

    start()
    return () => {
      done = true
      cancelAnimationFrame(frame)
      stream?.getTracks().forEach((t) => t.stop())
    }
  }, [])

  return (
    <div className="overlay" onClick={onCancel}>
      <div className="modal scanner" onClick={(e) => e.stopPropagation()}>
        <h2>Scan the visitor's QR pass</h2>
        {error ? (
          <p className="form-error">{error}</p>
        ) : (
          <div className="scanner-view">
            <video ref={videoRef} muted playsInline />
            <div className="scanner-frame" aria-hidden="true" />
          </div>
        )}
        <p className="muted small">Hold the phone's QR code in front of the camera.</p>
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onCancel}>
            Cancel
          </button>
        </div>
      </div>
    </div>
  )
}
