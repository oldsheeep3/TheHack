import { describe, expect, it } from 'vitest'
import type { ButtonEvent } from '../../protocol/types'
import { applyControllerId } from '../controllerId'

describe('applyControllerId', () => {
  it('keeps the device-provided controller_id when set', () => {
    const event: ButtonEvent = { controller_id: 'main', button_id: 1, timestamp: 1 }
    expect(applyControllerId(event, 'sub')).toEqual(event)
  })

  it('falls back to the UI selection when controller_id is empty', () => {
    const event: ButtonEvent = { controller_id: '', button_id: 1, timestamp: 1 }
    expect(applyControllerId(event, 'sub')).toEqual({ controller_id: 'sub', button_id: 1, timestamp: 1 })
  })

  it('falls back to the UI selection when controller_id is whitespace-only', () => {
    const event: ButtonEvent = { controller_id: '   ', button_id: 1, timestamp: 1 }
    expect(applyControllerId(event, 'main')).toEqual({ controller_id: 'main', button_id: 1, timestamp: 1 })
  })
})
