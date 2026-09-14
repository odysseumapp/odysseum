import { ApiError, OFFLINE_MESSAGE, type IApiClient } from './IApiClient'

// Every response is an envelope: { apiVersion, data } on success, { apiVersion, error: { code, message } } on failure.
interface Envelope<T> { apiVersion: string; data?: T; error?: { code: number; message: string } }

export class FetchApiClient implements IApiClient {
  constructor(private readonly root = '/api') {}

  async request<T = void>(path: string, method = 'GET', body?: unknown): Promise<T> {
    let response: Response
    try {
      response = await fetch(`${this.root}${path}`, {
        method,
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
      })
    } catch {
      // fetch only throws when no response arrived at all: server down, network gone, or the request aborted.
      throw new ApiError(0, OFFLINE_MESSAGE)
    }
    const text = await response.text()
    let envelope: Envelope<T> | undefined
    try { envelope = text ? JSON.parse(text) as Envelope<T> : undefined } catch { envelope = undefined }
    if (!response.ok) throw new ApiError(response.status, envelope?.error?.message ?? `Request failed (${response.status}). Your work is saved on this device.`)
    return envelope?.data as T
  }
}
