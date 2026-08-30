import streamDeck from "@elgato/streamdeck";

import { CycleAdaptersAction } from "./actions/cycle-adapters";
import { ToggleAdapterAction } from "./actions/toggle-adapter";

streamDeck.logger.setLevel("info");
streamDeck.actions.registerAction(new ToggleAdapterAction());
streamDeck.actions.registerAction(new CycleAdaptersAction());
streamDeck.connect();
