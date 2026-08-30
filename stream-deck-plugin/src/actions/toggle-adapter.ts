import streamDeck, {
  action,
  type DidReceiveSettingsEvent,
  type KeyAction,
  type KeyDownEvent,
  type SendToPluginEvent,
  SingletonAction,
  type WillAppearEvent,
} from "@elgato/streamdeck";
import type { JsonValue } from "@elgato/utils";

import { WinNetSwitchClient } from "../control-client";
import { formatAdapterTitle } from "../presentation";
import type { DataSourcePayload } from "../sdpi";

type ToggleAdapterSettings = {
  adapterId?: string;
};

@action({ UUID: "dev.witqq.win-net-switch.toggle-adapter" })
export class ToggleAdapterAction extends SingletonAction<ToggleAdapterSettings> {
  readonly #client = new WinNetSwitchClient();

  override async onWillAppear(ev: WillAppearEvent<ToggleAdapterSettings>): Promise<void> {
    if (ev.action.isKey()) {
      await this.#render(ev.action, ev.payload.settings);
    }
  }

  override async onDidReceiveSettings(
    ev: DidReceiveSettingsEvent<ToggleAdapterSettings>,
  ): Promise<void> {
    if (ev.action.isKey()) {
      await this.#render(ev.action, ev.payload.settings);
    }
  }

  override async onKeyDown(ev: KeyDownEvent<ToggleAdapterSettings>): Promise<void> {
    const adapterId = ev.payload.settings.adapterId;
    if (!adapterId) {
      await ev.action.setTitle("Select\nadapter");
      await ev.action.showAlert();
      return;
    }

    try {
      await ev.action.setTitle("Switching…");
      const adapters = await this.#client.toggleAdapter(adapterId);
      const adapter = adapters.find((item) => item.id === adapterId);
      if (!adapter) {
        throw new Error("The selected physical network adapter is no longer available.");
      }

      await this.#renderAdapter(ev.action, adapter);
    } catch (error) {
      await this.#renderError(ev.action, error);
    }
  }

  override async onSendToPlugin(
    ev: SendToPluginEvent<JsonValue, ToggleAdapterSettings>,
  ): Promise<void> {
    if (!isDataSourceRequest(ev.payload, "getAdapters")) {
      return;
    }

    try {
      const adapters = await this.#client.listAdapters();
      streamDeck.ui.sendToPropertyInspector({
        event: "getAdapters",
        items: adapters.map((adapter) => ({
          label: `${adapter.name} — ${adapter.isActive ? "on" : "off"}`,
          value: adapter.id,
        })),
      } satisfies DataSourcePayload);
    } catch (error) {
      streamDeck.logger.error("Could not load adapters for the property inspector.", error);
      streamDeck.ui.sendToPropertyInspector({
        event: "getAdapters",
        items: [
          {
            disabled: true,
            label: "Install or start WinNetSwitch first",
            value: "",
          },
        ],
      } satisfies DataSourcePayload);
    }
  }

  async #render(
    action: KeyAction<ToggleAdapterSettings>,
    settings: ToggleAdapterSettings,
  ): Promise<void> {
    if (!settings.adapterId) {
      await action.setState(0);
      await action.setTitle("Select\nadapter");
      return;
    }

    try {
      const adapters = await this.#client.listAdapters();
      const adapter = adapters.find((item) => item.id === settings.adapterId);
      if (!adapter) {
        await action.setState(0);
        await action.setTitle("Adapter\nmissing");
        return;
      }

      await this.#renderAdapter(action, adapter);
    } catch (error) {
      await this.#renderError(action, error, false);
    }
  }

  async #renderAdapter(
    action: KeyAction<ToggleAdapterSettings>,
    adapter: Awaited<ReturnType<WinNetSwitchClient["listAdapters"]>>[number],
  ): Promise<void> {
    await action.setState(adapter.isActive ? 1 : 0);
    await action.setTitle(formatAdapterTitle(adapter));
  }

  async #renderError(
    action: KeyAction<ToggleAdapterSettings>,
    error: unknown,
    showAlert = true,
  ): Promise<void> {
    streamDeck.logger.error("WinNetSwitch adapter toggle failed.", error);
    await action.setState(0);
    await action.setTitle("Start\nWinNetSwitch");
    if (showAlert) {
      await action.showAlert();
    }
  }
}

function isDataSourceRequest(payload: JsonValue, event: string): boolean {
  return typeof payload === "object" &&
    payload !== null &&
    !Array.isArray(payload) &&
    "event" in payload &&
    payload.event === event;
}
