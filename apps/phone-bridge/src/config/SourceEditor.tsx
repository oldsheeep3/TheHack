import { useEffect, useState } from 'react'
import type { JSX } from 'react'
import type { ApiClient } from '../protocol/apiClient'
import {
  SOURCE_AUDIO_MODES,
  SOURCE_AUDIO_MODE_LABEL,
  SOURCE_TYPES,
  SOURCE_TYPE_LABEL,
  SRT_MODE_LABEL,
  type DeviceInfo,
  type DeviceQueryType,
  type MixLayer,
  type SourceAudioMode,
  type SourceInfo,
  type SourceType,
  type SrtMode,
  type SrtSetupInfo,
} from '../protocol/types'
import { fullFrameMixLayer, type SourceFormValues } from './sourceForm'
import { srtUrlForMode } from './srtUrl'
import {
  Field,
  StackedField,
  dangerButtonClass,
  inputClass,
  primaryButtonClass,
  secondaryButtonClass,
} from './ui'

interface SourceEditorProps {
  apiClient: ApiClient
  /** Existing sources, offered as members when building a MIX. */
  sources: SourceInfo[]
  values: SourceFormValues
  onChange: (values: SourceFormValues) => void
  /** Set when editing: the id is the PC's key for the source and cannot be re-typed. */
  editingId: string | null
  errors: string[]
  busy: boolean
  onSubmit: () => void
  onCancel: () => void
}

/**
 * Add/edit form for every source type the PC supports (§4.2: NDI / WEBCAM / SRT / IMAGE / HTML / MIX).
 * Webcam and NDI pick from `GET /api/v1/devices/{type}` rather than asking the operator to type a device
 * id, and SRT shows the PC's own listener guidance from `GET /api/v1/srt/setup`.
 */
export function SourceEditor({
  apiClient,
  sources,
  values,
  onChange,
  editingId,
  errors,
  busy,
  onSubmit,
  onCancel,
}: SourceEditorProps): JSX.Element {
  // Tagged with the kind it was fetched for, so switching type never shows the previous kind's devices
  // while the new request is still in flight — and so "loading" is derived rather than a second state.
  const [deviceList, setDeviceList] = useState<{ type: DeviceQueryType; items: DeviceInfo[] } | null>(null)
  const [srtSetup, setSrtSetup] = useState<SrtSetupInfo | null>(null)

  const needsDevices = values.type === 'WEBCAM' || values.type === 'NDI'
  const deviceQueryType: DeviceQueryType = values.type === 'WEBCAM' ? 'webcam' : 'ndi'
  const devices = deviceList?.type === deviceQueryType ? deviceList.items : []
  const devicesLoading = needsDevices && deviceList?.type !== deviceQueryType

  useEffect(() => {
    if (!needsDevices) return
    const controller = new AbortController()
    apiClient
      .getDevices(deviceQueryType, controller.signal)
      .then((items) => setDeviceList({ type: deviceQueryType, items }))
      .catch(() => {
        // Enumeration is advisory — the manual field below still works — so a failure shows as
        // "no devices found" rather than blocking the form.
        if (!controller.signal.aborted) setDeviceList({ type: deviceQueryType, items: [] })
      })
    return () => controller.abort()
  }, [apiClient, needsDevices, deviceQueryType])

  useEffect(() => {
    if (values.type !== 'SRT') return
    const controller = new AbortController()
    apiClient
      .getSrtSetup(controller.signal)
      .then((result) => setSrtSetup(result))
      .catch(() => setSrtSetup(null))
    return () => controller.abort()
  }, [apiClient, values.type])

  const patch = (next: Partial<SourceFormValues>): void => onChange({ ...values, ...next })

  const selectedWebcam = devices.find((device) => device.id === values.webcamDeviceId)

  const addMixLayer = (sourceId: string): void => {
    patch({
      mixLayers: [
        ...values.mixLayers,
        fullFrameMixLayer(sourceId, values.mixCanvasWidth, values.mixCanvasHeight, values.mixLayers.length),
      ],
    })
  }

  const updateMixLayer = (index: number, layerPatch: Partial<MixLayer>): void => {
    patch({ mixLayers: values.mixLayers.map((layer, i) => (i === index ? { ...layer, ...layerPatch } : layer)) })
  }

  const removeMixLayer = (index: number): void => {
    patch({ mixLayers: values.mixLayers.filter((_, i) => i !== index) })
  }

  return (
    <div className="mt-4 flex flex-col gap-3 rounded-lg border border-border p-3">
      <p className="text-sm font-medium text-text-primary">{editingId ? `ソース編集: ${editingId}` : '新規ソース'}</p>

      {errors.length > 0 && (
        <ul className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          {errors.map((error) => (
            <li key={error}>{error}</li>
          ))}
        </ul>
      )}

      <StackedField label="ID">
        <input
          type="text"
          value={values.id}
          disabled={editingId !== null}
          onChange={(event) => patch({ id: event.target.value })}
          className={`${inputClass} disabled:opacity-60`}
        />
      </StackedField>

      <StackedField label="名前">
        <input
          type="text"
          value={values.name}
          onChange={(event) => patch({ name: event.target.value })}
          className={inputClass}
        />
      </StackedField>

      <StackedField label="種別">
        <select
          value={values.type}
          disabled={editingId !== null}
          onChange={(event) => patch({ type: event.target.value as SourceType })}
          className={`${inputClass} disabled:opacity-60`}
        >
          {SOURCE_TYPES.map((type) => (
            <option key={type} value={type}>
              {SOURCE_TYPE_LABEL[type]}
            </option>
          ))}
        </select>
      </StackedField>

      <StackedField label="音声">
        <select
          value={values.audioMode}
          onChange={(event) => patch({ audioMode: event.target.value as SourceAudioMode })}
          className={inputClass}
        >
          {SOURCE_AUDIO_MODES.map((mode) => (
            <option key={mode} value={mode}>
              {SOURCE_AUDIO_MODE_LABEL[mode]}
            </option>
          ))}
        </select>
      </StackedField>

      {values.type === 'NDI' && (
        <>
          <StackedField label="NDIソース（検出）">
            <select
              value={devices.some((device) => device.name === values.ndiSourceName) ? values.ndiSourceName : ''}
              onChange={(event) => patch({ ndiSourceName: event.target.value })}
              className={inputClass}
            >
              <option value="">{devicesLoading ? '検索中…' : '手入力'}</option>
              {devices.map((device) => (
                <option key={device.id} value={device.name}>
                  {device.name}
                </option>
              ))}
            </select>
          </StackedField>
          <StackedField label="NDIソース名">
            <input
              type="text"
              value={values.ndiSourceName}
              onChange={(event) => patch({ ndiSourceName: event.target.value })}
              placeholder="STUDIO (Cam1)"
              className={inputClass}
            />
          </StackedField>
          {!devicesLoading && devices.length === 0 && (
            <p className="text-xs text-text-muted">
              NDIソースが見つかりません（DistroAV 未導入か、送信側が起動していない可能性があります）。名前は直接入力できます。
            </p>
          )}
        </>
      )}

      {values.type === 'WEBCAM' && (
        <>
          <StackedField label="デバイス">
            <select
              value={values.webcamDeviceId}
              onChange={(event) => patch({ webcamDeviceId: event.target.value, webcamFormat: '' })}
              className={inputClass}
            >
              <option value="">{devicesLoading ? '検索中…' : '選択してください'}</option>
              {devices.map((device) => (
                <option key={device.id} value={device.id}>
                  {device.name}
                </option>
              ))}
            </select>
          </StackedField>
          <StackedField label="フォーマット（任意）">
            <select
              value={values.webcamFormat}
              onChange={(event) => patch({ webcamFormat: event.target.value })}
              className={inputClass}
            >
              <option value="">既定</option>
              {(selectedWebcam?.formats ?? []).map((format) => (
                <option key={format} value={format}>
                  {format}
                </option>
              ))}
            </select>
          </StackedField>
          {!devicesLoading && devices.length === 0 && (
            <p className="text-xs text-text-muted">
              カメラが見つかりません。PCに接続されているか、他アプリが専有していないか確認してください。
            </p>
          )}
        </>
      )}

      {values.type === 'SRT' && (
        <>
          <StackedField label="モード">
            <select
              value={values.srtMode}
              onChange={(event) => {
                const mode = event.target.value as SrtMode
                patch({
                  srtMode: mode,
                  srtUrl:
                    mode === 'listener' && srtSetup
                      ? srtUrlForMode(`srt://0.0.0.0:${srtSetup.listener_port}`, 'listener')
                      : values.srtUrl,
                })
              }}
              className={inputClass}
            >
              {(['listener', 'caller'] as SrtMode[]).map((mode) => (
                <option key={mode} value={mode}>
                  {SRT_MODE_LABEL[mode]}
                </option>
              ))}
            </select>
          </StackedField>
          <StackedField label="URL">
            <input
              type="text"
              value={values.srtUrl}
              onChange={(event) => patch({ srtUrl: event.target.value })}
              placeholder="srt://host:port"
              className={inputClass}
            />
          </StackedField>
          <StackedField label="レイテンシ (ms)">
            <input
              type="number"
              value={values.srtLatencyMs}
              onChange={(event) => patch({ srtLatencyMs: Number(event.target.value) })}
              className={inputClass}
            />
          </StackedField>
          {srtSetup && (
            <div className="rounded-md bg-surface-2 px-3 py-2 text-xs text-text-muted">
              <p className="whitespace-pre-wrap">{srtSetup.instructions_text}</p>
              <p className="mt-1">
                送信側に設定するURL: <span className="text-text-primary">{srtSetup.recommended_url}</span>
              </p>
              {srtSetup.host_candidates.length > 0 && (
                <p className="mt-1">このPCのアドレス: {srtSetup.host_candidates.join(' / ')}</p>
              )}
              <button
                type="button"
                onClick={() =>
                  patch({
                    srtMode: 'listener',
                    srtUrl: srtUrlForMode(`srt://0.0.0.0:${srtSetup.listener_port}`, 'listener'),
                    srtLatencyMs: srtSetup.recommended_latency_ms,
                  })
                }
                className={`${secondaryButtonClass} mt-2`}
              >
                推奨設定（Listener）を適用
              </button>
            </div>
          )}
        </>
      )}

      {values.type === 'IMAGE' && (
        <StackedField label="画像ファイル（PC上のパス）">
          <input
            type="text"
            value={values.imageFilePath}
            onChange={(event) => patch({ imageFilePath: event.target.value })}
            placeholder="C:\media\logo.png"
            className={inputClass}
          />
        </StackedField>
      )}

      {values.type === 'HTML' && (
        <>
          <StackedField label="URL（http(s):// またはローカル .html のパス）">
            <input
              type="text"
              value={values.htmlUrl}
              onChange={(event) => patch({ htmlUrl: event.target.value })}
              className={inputClass}
            />
          </StackedField>
          <Field label="ローカルファイル">
            <input
              type="checkbox"
              checked={values.htmlIsLocalFile}
              onChange={(event) => patch({ htmlIsLocalFile: event.target.checked })}
              className="h-5 w-5 accent-accent"
            />
          </Field>
          <div className="flex gap-2">
            <StackedField label="幅">
              <input
                type="number"
                value={values.htmlWidth}
                onChange={(event) => patch({ htmlWidth: Number(event.target.value) })}
                className={`${inputClass} w-28`}
              />
            </StackedField>
            <StackedField label="高さ">
              <input
                type="number"
                value={values.htmlHeight}
                onChange={(event) => patch({ htmlHeight: Number(event.target.value) })}
                className={`${inputClass} w-28`}
              />
            </StackedField>
            <StackedField label="FPS">
              <input
                type="number"
                value={values.htmlFps}
                onChange={(event) => patch({ htmlFps: Number(event.target.value) })}
                className={`${inputClass} w-20`}
              />
            </StackedField>
          </div>
          <StackedField label="追加CSS（任意）">
            <textarea
              value={values.htmlCss}
              rows={3}
              onChange={(event) => patch({ htmlCss: event.target.value })}
              className={inputClass}
            />
          </StackedField>
          <p className="text-xs text-text-muted">
            幅/高さはページ自身のレイアウト解像度です（この大きさで描画してからシーンに合わせて拡縮されます）。
          </p>
        </>
      )}

      {values.type === 'MIX' && (
        <>
          <div className="flex gap-2">
            <StackedField label="キャンバス幅">
              <input
                type="number"
                value={values.mixCanvasWidth}
                onChange={(event) => patch({ mixCanvasWidth: Number(event.target.value) })}
                className={`${inputClass} w-28`}
              />
            </StackedField>
            <StackedField label="キャンバス高さ">
              <input
                type="number"
                value={values.mixCanvasHeight}
                onChange={(event) => patch({ mixCanvasHeight: Number(event.target.value) })}
                className={`${inputClass} w-28`}
              />
            </StackedField>
          </div>

          <div className="flex flex-col gap-2">
            {values.mixLayers.map((layer, index) => (
              <div
                key={`${layer.source_id}-${index}`}
                className="flex flex-col gap-2 rounded-md bg-surface-2 px-3 py-2"
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="text-sm text-text-primary">
                    レイヤー{index + 1}:{' '}
                    {sources.find((source) => source.id === layer.source_id)?.name ?? layer.source_id}
                  </span>
                  <button type="button" onClick={() => removeMixLayer(index)} className={dangerButtonClass}>
                    削除
                  </button>
                </div>
                <div className="flex flex-wrap gap-2">
                  {(['x_position', 'y_position', 'width', 'height', 'z_order'] as const).map((key) => (
                    <StackedField key={key} label={key}>
                      <input
                        type="number"
                        value={layer[key]}
                        onChange={(event) => updateMixLayer(index, { [key]: Number(event.target.value) })}
                        className={`${inputClass} w-24`}
                      />
                    </StackedField>
                  ))}
                </div>
              </div>
            ))}
          </div>

          <StackedField label="レイヤーを追加">
            <select
              value=""
              onChange={(event) => {
                if (event.target.value) addMixLayer(event.target.value)
              }}
              className={inputClass}
            >
              <option value="">ソースを選択…</option>
              {sources
                .filter((source) => source.id !== null && source.id !== values.id)
                .map((source) => (
                  <option key={source.id} value={source.id as string}>
                    {source.name}
                  </option>
                ))}
            </select>
          </StackedField>
          <p className="text-xs text-text-muted">
            ミックスは1つのソースとして扱われ、バス・マルチビュー・モジュール割付のどこにでも置けます。メンバーは単体でも同時に使えます。
          </p>
        </>
      )}

      <div className="flex gap-2">
        <button type="button" disabled={busy} onClick={onSubmit} className={primaryButtonClass}>
          {editingId ? '更新' : '追加'}
        </button>
        <button type="button" onClick={onCancel} className={secondaryButtonClass}>
          キャンセル
        </button>
      </div>
    </div>
  )
}
