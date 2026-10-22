/**
 * Messages for people, from whatever a request or a page throws. The API's own detail is shown when it says something
 * specific ("Invalid credentials.", "Hourly AI extraction limit reached. Try again later."); generic details
 * ("Internal server error.", "Request failed."), network failures, unreadable responses and the app's own bugs get a
 * sentence that says what happened and what to do. Errors the app throws on purpose (form checks) keep their message.
 */

/** Details the API (or the client, when the body had none) uses for any error of a status: they say nothing more. */
const genericDetails = new Set([
  "",
  "request failed.",
  "internal server error.",
  "unauthorized.",
  "forbidden.",
  "not found.",
  "invalid json body.",
]);

const statusMessages: Readonly<Record<number, string>> = {
  400: "Playdex couldn't use that request. Check what you entered and try again.",
  401: "Your session has expired. Sign in again.",
  403: "You don't have access to this.",
  404: "That couldn't be found. It may have been deleted.",
  408: "Playdex took too long to answer. Try again.",
  409: "This was changed somewhere else. Reload the page and try again.",
  413: "That's too large to upload.",
  429: "Too many requests. Wait a moment and try again.",
};

const serverMessage = "Playdex ran into a problem on its side. Try again in a moment.";
const networkMessage = "Can't reach Playdex. Check your connection and try again.";
const unreadableMessage = "Playdex sent a response this page couldn't read. Reload the page and try again.";
const unknownMessage = "Something went wrong. Try again.";

/** fetch's network failure as Chrome, Firefox and Safari word it. */
const networkErrors = /failed to fetch|networkerror|load failed|network request failed/i;

/** The status of an ApiError from ./api, matched by shape so this module doesn't import the client. */
function apiStatus(error: unknown): number | null {
  if (!(error instanceof Error) || error.name !== "ApiError") return null;
  const status: unknown = (error as Error & { status?: unknown }).status;
  return typeof status === "number" ? status : null;
}

export function errorMessage(error: unknown): string {
  const status = apiStatus(error);
  if (status !== null) {
    const detail = (error as Error).message.trim();
    if (!genericDetails.has(detail.toLowerCase())) return detail;
    if (status === 504) return statusMessages[408];
    if (status >= 500) return serverMessage;
    return statusMessages[status] ?? `Playdex couldn't complete the request (error ${status}). Try again.`;
  }
  if (error instanceof DOMException && error.name === "TimeoutError") return statusMessages[408];
  if (error instanceof DOMException && error.name === "AbortError") return "The request was cancelled.";
  // fetch rejects with a TypeError when the network fails (or CORS blocks the response); any other one is a bug.
  if (error instanceof TypeError) return networkErrors.test(error.message) ? networkMessage : unknownMessage;
  // response.json() on an HTML error page, or a success response without data.
  if (error instanceof SyntaxError) return unreadableMessage;
  if (error instanceof RangeError || error instanceof ReferenceError || error instanceof URIError || error instanceof EvalError) {
    return unknownMessage;
  }
  if (error instanceof Error) return error.message.trim() || unknownMessage;
  return unknownMessage;
}
