import streamDeck, {
  action,
  type KeyAction,
  type KeyDownEvent,
  SingletonAction,
  type WillAppearEvent,
} from "@elgato/streamdeck";

import { WinNetSwitchClient } from "../control-client";
import { formatCycleTitle } from "../presentation";

type CycleAdapterSettings = Record<string, never>;

@action({ UUID: "dev.witqq.win-net-switch.cycle-adapters" })
export class CycleAdaptersAction extends SingletonAction<CycleAdapterSettings> {
  readonly #client = new WinNetSwitchClient();

  override async onWillAppear(ev: WillAppearEvent<CycleAdapterSettings>): Promise<void> {
    if (ev.action.isKey()) {
      await this.#render(ev.action);
    }
  }

  override async onKeyDown(ev: KeyDownEvent<CycleAdapterSettings>): Promise<void> {
    try {
      await ev.action.setTitle("Switching…");
      const adapters = await this.#client.cycleAdapters();
      await ev.action.setTitle(formatCycleTitle(adapters));
    } catch (error) {
      await this.#renderError(ev.action, error);
    }
  }

  async #render(action: KeyAction<CycleAdapterSettings>): Promise<void> {
    try {
      const adapters = await this.#client.listAdapters();
      await action.setTitle(formatCycleTitle(adapters));
    } catch (error) {
      await this.#renderError(action, error, false);
    }
  }

  async #renderError(
    action: KeyAction<CycleAdapterSettings>,
    error: unknown,
    showAlert = true,
  ): Promise<void> {
    streamDeck.logger.error("WinNetSwitch adapter cycle failed.", error);
    await action.setTitle("Start\nWinNetSwitch");
    if (showAlert) {
      await action.showAlert();
    }
  }
}
