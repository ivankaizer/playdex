import { useCallback, useEffect, useRef } from "react";
import { apiRequest, errorMessage, isAbortError } from "@/lib/api";
import type { TestNotificationResponse, UserSettingsResponse } from "../models";
import { LatestRequest } from "../shared/latestRequest";
import { isLifetimeCancellation } from "../shared/lifetime";
import { useStore } from "../shared/useStore";
import * as preferences from "./preferencesState";

// Blazor begins the initial load before the first render, so the first paint already shows loading.
const initialPreferencesState = () => preferences.beginSettingsLoad(preferences.createPreferencesState());

/** Personal settings (short-session filter + Telegram notifications). */
export function usePreferences() {
  const [state, update, get] = useStore(initialPreferencesState);
  const lifetime = useRef(new AbortController());
  const settingsRequest = useRef(new LatestRequest());

  const loadSettings = useCallback(async () => {
    update(preferences.beginSettingsLoad);
    const request = settingsRequest.current.begin();
    try {
      const response = await apiRequest<UserSettingsResponse>(preferences.settingsPath, { signal: request.signal });
      if (request.isCurrent() && !request.signal.aborted) {
        update((current) => preferences.completeSettingsLoad(current, response));
      }
    } catch (error) {
      if (isAbortError(error) && request.signal.aborted) return;
      if (request.isCurrent()) update((current) => preferences.failSettingsLoad(current, errorMessage(error)));
    } finally {
      request.finish();
    }
  }, [update]);

  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    const request = settingsRequest.current;
    return () => {
      controller.abort();
      request.invalidate();
    };
  }, []);

  const setIgnoreShortSessionsMinutes = useCallback(
    (value: string) => update((current) => preferences.setIgnoreShortSessionsMinutes(current, value)),
    [update],
  );

  const setTelegramChatId = useCallback(
    (value: string) => update((current) => preferences.setTelegramChatId(current, value)),
    [update],
  );

  const stepIgnoreShortSessionsMinutes = useCallback(
    (delta: number) => update((current) => preferences.stepIgnoreShortSessionsMinutes(current, delta)),
    [update],
  );

  const discardChanges = useCallback(() => update(preferences.discardSettingsChanges), [update]);

  const saveSettings = useCallback(async () => {
    const { state: validated, submission } = preferences.buildSettingsSubmission(get());
    update(() => validated);
    if (!submission) return;

    const signal = lifetime.current.signal;
    update(preferences.beginSettingsSave);
    try {
      const response = await apiRequest<UserSettingsResponse>(submission.path, {
        method: submission.method,
        body: submission.body,
        signal,
      });
      update((current) => preferences.completeSettingsSave(current, response));
    } catch (error) {
      if (!isLifetimeCancellation(error, signal)) {
        update((current) => preferences.failSettingsSave(current, errorMessage(error)));
      }
    } finally {
      update(preferences.endSettingsSave);
    }
  }, [get, update]);

  const sendTestNotification = useCallback(async () => {
    if (!preferences.canSendTestNotification(get())) return;

    const signal = lifetime.current.signal;
    update(preferences.beginTestNotification);
    try {
      const response = await apiRequest<TestNotificationResponse>(preferences.testNotificationPath, {
        method: "POST",
        signal,
      });
      update((current) => preferences.completeTestNotification(current, response));
    } catch (error) {
      if (!isLifetimeCancellation(error, signal)) {
        update((current) => preferences.failTestNotification(current, errorMessage(error)));
      }
    } finally {
      update(preferences.endTestNotification);
    }
  }, [get, update]);

  return {
    state,
    loadSettings,
    setIgnoreShortSessionsMinutes,
    stepIgnoreShortSessionsMinutes,
    setTelegramChatId,
    discardChanges,
    saveSettings,
    sendTestNotification,
  };
}
