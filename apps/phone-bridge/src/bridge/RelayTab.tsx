import { useEffect, useState } from 'react'
import type { JSX } from 'react'
import { getBrowserCapabilities } from '../lib/browserSupport'
import type { ButtonEvent } from '../protocol/types'
import { WsClient, type WsConnectionState } from '../protocol/wsClient'
import { applyControllerId, type ControllerIdSelection } from './controllerId'
import { SerialLink, type SerialLinkState } from './serialLink'

const MAX_LOG_ENTRIES = 20
const CONTROLLER_IDS: ControllerIdSelection[] = ['main', 'sub']

interface LogEntry {
  event: ButtonEvent
  receivedAt: number
}

const USB_STATE_LABEL: Record<SerialLinkState, string> = {
  idle: '未接続',
  unsupported: '非対応',
  connecting: '接続中…',
  open: '接続済み',
  reconnecting: '再接続中…',
  closed: '切断',
}

const USB_STATE_COLOR: Record<SerialLinkState, string> = {
  idle: 'bg-text-muted',
  unsupported: 'bg-danger',
  connecting: 'bg-warning',
  open: 'bg-success',
  reconnecting: 'bg-warning',
  closed: 'bg-text-muted',
}

const WS_STATE_LABEL: Record<WsConnectionState, string> = {
  idle: '未接続',
  connecting: '接続中…',
  open: '接続済み',
  reconnecting: '再接続中…',
  closed: '切断',
}

const WS_STATE_COLOR: Record<WsConnectionState, string> = {
  idle: 'bg-text-muted',
  connecting: 'bg-warning',
  open: 'bg-success',
  reconnecting: 'bg-warning',
  closed: 'bg-text-muted',
}

function StatusBadge({ label, colorClass }: { label: string; colorClass: string }): JSX.Element {
  return (
    <span className="flex items-center gap-2 text-sm text-text-muted">
      <span className={`h-2.5 w-2.5 rounded-full ${colorClass}`} aria-hidden="true" />
      {label}
    </span>
  )
}

function useStableInstance<T>(create: () => T): T {
  return useState(create)[0]
}

export function RelayTab(): JSX.Element {
  const capabilities = useStableInstance(() => getBrowserCapabilities())
  const serialLink = useStableInstance(() => new SerialLink())
  const wsClient = useStableInstance(() => new WsClient())

  const [controllerId, setControllerId] = useState<ControllerIdSelection>('main')
  const [usbState, setUsbState] = useState<SerialLinkState>(serialLink.getState())
  const [wsState, setWsState] = useState<WsConnectionState>(wsClient.getState())
  const [lastError, setLastError] = useState<string | null>(null)
  const [log, setLog] = useState<LogEntry[]>([])

  useEffect(() => {
    const unsubscribers = [
      serialLink.onStateChange(setUsbState),
      serialLink.onError(setLastError),
      wsClient.onStateChange(setWsState),
    ]

    wsClient.connect()

    return () => {
      for (const unsubscribe of unsubscribers) unsubscribe()
      serialLink.disconnect()
      wsClient.disconnect()
    }
  }, [serialLink, wsClient])

  // Re-subscribed whenever controllerId changes so the handler always
  // closes over the latest UI selection without needing a ref.
  useEffect(() => {
    return serialLink.onEvent((event) => {
      const resolved = applyControllerId(event, controllerId)
      setLog((prev) => [{ event: resolved, receivedAt: Date.now() }, ...prev].slice(0, MAX_LOG_ENTRIES))
      if (wsClient.getState() === 'open') {
        try {
          wsClient.send({ event: 'button_press', data: resolved })
        } catch (error) {
          setLastError(error instanceof Error ? error.message : String(error))
        }
      }
    })
  }, [serialLink, wsClient, controllerId])

  const isUsbBusy = usbState === 'open' || usbState === 'connecting' || usbState === 'reconnecting'

  return (
    <div className="flex flex-col gap-4">
      {!capabilities.canBridgeUsb && (
        <div className="rounded-lg border border-danger/40 bg-danger/10 p-4 text-sm text-danger">
          {!capabilities.isSecureContext
            ? 'HTTPS（または localhost）で開いてください。'
            : capabilities.hasWebUsb
              ? 'WebUSB のみ対応のブラウザです。Web Serial 対応の Chromium 系ブラウザが必要です。'
              : 'Web Serial に対応した Chromium 系ブラウザ（Chrome / Edge 等）でアクセスしてください。'}
        </div>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-surface-1 p-4">
        <div className="flex flex-col gap-2">
          <StatusBadge label={`USB: ${USB_STATE_LABEL[usbState]}`} colorClass={USB_STATE_COLOR[usbState]} />
          <StatusBadge label={`WS: ${WS_STATE_LABEL[wsState]}`} colorClass={WS_STATE_COLOR[wsState]} />
        </div>
        <button
          type="button"
          disabled={!capabilities.canBridgeUsb}
          onClick={() => {
            if (isUsbBusy) {
              serialLink.disconnect()
            } else {
              void serialLink.requestConnect()
            }
          }}
          className={`rounded-md px-5 py-3 text-base font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-40 ${
            isUsbBusy ? 'bg-danger/20 text-danger' : 'bg-accent text-white'
          }`}
        >
          {isUsbBusy ? 'USB切断' : 'USB接続'}
        </button>
      </div>

      <div className="rounded-lg border border-border bg-surface-1 p-4">
        <p className="mb-2 text-sm font-medium text-text-primary">コントローラーID</p>
        <div className="flex gap-2" role="radiogroup" aria-label="controller_id">
          {CONTROLLER_IDS.map((id) => (
            <button
              key={id}
              type="button"
              role="radio"
              aria-checked={controllerId === id}
              onClick={() => setControllerId(id)}
              className={`flex-1 rounded-md px-4 py-3 text-base font-medium transition-colors ${
                controllerId === id
                  ? 'bg-accent-muted text-text-primary ring-1 ring-accent'
                  : 'bg-surface-2 text-text-muted'
              }`}
            >
              {id}
            </button>
          ))}
        </div>
      </div>

      {lastError && (
        <p className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          {lastError}
        </p>
      )}

      <div className="rounded-lg border border-border bg-surface-1 p-4">
        <p className="mb-2 text-sm font-medium text-text-primary">受信イベントログ</p>
        {log.length === 0 ? (
          <p className="text-sm text-text-muted">まだイベントを受信していません。</p>
        ) : (
          <ul className="flex flex-col gap-1 text-sm">
            {log.map((entry, index) => (
              <li key={`${entry.receivedAt}-${index}`} className="flex justify-between text-text-muted">
                <span>
                  {entry.event.controller_id} / button {entry.event.button_id}
                </span>
                <span>{new Date(entry.receivedAt).toLocaleTimeString()}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
