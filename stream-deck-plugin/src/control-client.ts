import { createConnection } from "node:net";

const DEFAULT_PIPE_PATH = String.raw`\\.\pipe\WinNetSwitch.Control.v1`;
const PROTOCOL_VERSION = 1;
const MAXIMUM_RESPONSE_CHARACTERS = 65_536;
const REQUEST_TIMEOUT_MILLISECONDS = 15_000;

export type NetworkAdapter = {
  id: string;
  name: string;
  description: string;
  status: string;
  isEnabled: boolean;
  isActive: boolean;
  isWireless: boolean;
};

type ControlCommand = "list" | "toggle" | "cycle";

type ControlResponse = {
  version: number;
  ok: boolean;
  adapters: NetworkAdapter[];
  error: string | null;
};

export class WinNetSwitchClient {
  readonly #pipePath: string;

  constructor(pipePath = DEFAULT_PIPE_PATH) {
    this.#pipePath = pipePath;
  }

  listAdapters(): Promise<NetworkAdapter[]> {
    return this.#request("list");
  }

  toggleAdapter(adapterId: string): Promise<NetworkAdapter[]> {
    if (!adapterId) {
      return Promise.reject(new Error("Select a network adapter first."));
    }

    return this.#request("toggle", adapterId);
  }

  cycleAdapters(): Promise<NetworkAdapter[]> {
    return this.#request("cycle");
  }

  #request(command: ControlCommand, adapterId?: string): Promise<NetworkAdapter[]> {
    return new Promise((resolve, reject) => {
      let completed = false;
      let responseText = "";
      const socket = createConnection(this.#pipePath);

      const fail = (error: unknown): void => {
        if (completed) {
          return;
        }

        completed = true;
        socket.destroy();
        reject(normalizeConnectionError(error));
      };

      socket.setEncoding("utf8");
      socket.setTimeout(REQUEST_TIMEOUT_MILLISECONDS);
      socket.once("connect", () => {
        socket.write(
          `${JSON.stringify({
            version: PROTOCOL_VERSION,
            command,
            ...(adapterId ? { adapterId } : {}),
          })}\n`,
        );
      });
      socket.on("data", (chunk: string) => {
        responseText += chunk;
        if (responseText.length > MAXIMUM_RESPONSE_CHARACTERS) {
          fail(new Error("WinNetSwitch returned an oversized response."));
          return;
        }

        const lineEnd = responseText.indexOf("\n");
        if (lineEnd < 0 || completed) {
          return;
        }

        try {
          const response = parseResponse(responseText.slice(0, lineEnd));
          completed = true;
          socket.end();
          if (!response.ok) {
            reject(new Error(response.error ?? "WinNetSwitch rejected the request."));
            return;
          }

          resolve(response.adapters);
        } catch (error) {
          fail(error);
        }
      });
      socket.once("timeout", () => fail(new Error("WinNetSwitch did not respond in time.")));
      socket.once("error", fail);
      socket.once("end", () => {
        if (!completed) {
          fail(new Error("WinNetSwitch closed the connection without a response."));
        }
      });
    });
  }
}

function parseResponse(json: string): ControlResponse {
  const value: unknown = JSON.parse(json);
  if (!isRecord(value) ||
      value.version !== PROTOCOL_VERSION ||
      typeof value.ok !== "boolean" ||
      !Array.isArray(value.adapters) ||
      !(value.error === null || typeof value.error === "string") ||
      !value.adapters.every(isNetworkAdapter)) {
    throw new Error("WinNetSwitch returned an invalid protocol response.");
  }

  return value as ControlResponse;
}

function isNetworkAdapter(value: unknown): value is NetworkAdapter {
  return isRecord(value) &&
    typeof value.id === "string" &&
    typeof value.name === "string" &&
    typeof value.description === "string" &&
    typeof value.status === "string" &&
    typeof value.isEnabled === "boolean" &&
    typeof value.isActive === "boolean" &&
    typeof value.isWireless === "boolean";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function normalizeConnectionError(error: unknown): Error {
  if (error instanceof Error && error.message.startsWith("WinNetSwitch")) {
    return error;
  }

  return new Error(
    "WinNetSwitch is not running. Install or start the required WinNetSwitch companion application.",
    { cause: error },
  );
}
