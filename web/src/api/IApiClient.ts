export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message) }
}

export const OFFLINE_MESSAGE = "Can't reach the server. Your work is saved on this device."

/** True when the server could not be reached at all, as opposed to answering with an error. */
export const isOffline = (ex: unknown) => ex instanceof ApiError && (ex.status === 0 || ex.status >= 502)

/** Transport only: sends a request, unwraps the response envelope, and turns failures into ApiError. */
export interface IApiClient {
  request<T = void>(path: string, method?: string, body?: unknown): Promise<T>
}
