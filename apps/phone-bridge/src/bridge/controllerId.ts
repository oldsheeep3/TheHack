/**
 * `controller_id` assignment (§4.1): the firmware normally stamps its own
 * `controller_id`, but when it is unset the relay falls back to the
 * UI-selected value (main/sub).
 */

import type { ButtonEvent } from '../protocol/types'

export type ControllerIdSelection = 'main' | 'sub'

export function applyControllerId(
  event: ButtonEvent,
  fallbackControllerId: ControllerIdSelection,
): ButtonEvent {
  if (event.controller_id.trim() !== '') return event
  return { ...event, controller_id: fallbackControllerId }
}
