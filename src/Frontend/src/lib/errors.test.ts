import { describe, expect, it } from "vitest";
import { ApiError, errorMessage } from "./api";

describe("errorMessage", () => {
  it("shows the API's own detail when it says something specific", () => {
    expect(errorMessage(new ApiError(401, "Invalid credentials."))).toBe("Invalid credentials.");
    expect(errorMessage(new ApiError(429, "Hourly AI extraction limit reached. Try again later."))).toBe(
      "Hourly AI extraction limit reached. Try again later.",
    );
    expect(errorMessage(new ApiError(502, "Notification delivery failed. Check the Telegram chat ID and try again."))).toBe(
      "Notification delivery failed. Check the Telegram chat ID and try again.",
    );
  });

  it("explains a generic detail by its status", () => {
    expect(errorMessage(new ApiError(500, "Internal server error."))).toBe(
      "Playdex ran into a problem on its side. Try again in a moment.",
    );
    expect(errorMessage(new ApiError(503, "Request failed."))).toBe("Playdex ran into a problem on its side. Try again in a moment.");
    expect(errorMessage(new ApiError(504, "Request failed."))).toBe("Playdex took too long to answer. Try again.");
    expect(errorMessage(new ApiError(401, "Request failed."))).toBe("Your session has expired. Sign in again.");
    expect(errorMessage(new ApiError(404, "Not found."))).toBe("That couldn't be found. It may have been deleted.");
    expect(errorMessage(new ApiError(400, "Invalid JSON body."))).toBe(
      "Playdex couldn't use that request. Check what you entered and try again.",
    );
    expect(errorMessage(new ApiError(418, "Request failed."))).toBe("Playdex couldn't complete the request (error 418). Try again.");
  });

  it("explains network failures and unreadable responses instead of showing browser text", () => {
    expect(errorMessage(new TypeError("Failed to fetch"))).toBe("Can't reach Playdex. Check your connection and try again.");
    expect(errorMessage(new TypeError("NetworkError when attempting to fetch resource."))).toBe(
      "Can't reach Playdex. Check your connection and try again.",
    );
    expect(errorMessage(new SyntaxError("Unexpected token '<', \"<!doctype \"... is not valid JSON"))).toBe(
      "Playdex sent a response this page couldn't read. Reload the page and try again.",
    );
    expect(errorMessage(new DOMException("signal timed out", "TimeoutError"))).toBe("Playdex took too long to answer. Try again.");
  });

  it("never shows a bug's message", () => {
    expect(errorMessage(new TypeError("Cannot read properties of undefined (reading 'map')"))).toBe("Something went wrong. Try again.");
    expect(errorMessage(new RangeError("Invalid time value"))).toBe("Something went wrong. Try again.");
    expect(errorMessage("boom")).toBe("Something went wrong. Try again.");
    expect(errorMessage(undefined)).toBe("Something went wrong. Try again.");
  });

  it("keeps the message of an error the app throws on purpose", () => {
    expect(errorMessage(new Error("Choose a PNG, JPEG or WebP screenshot."))).toBe("Choose a PNG, JPEG or WebP screenshot.");
  });
});
