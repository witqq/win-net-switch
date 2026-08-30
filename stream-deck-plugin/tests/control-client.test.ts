import assert from "node:assert/strict";
import { createServer, type Server } from "node:net";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { after, before, test } from "node:test";

import { WinNetSwitchClient } from "../src/control-client.ts";
import { formatAdapterTitle, formatCycleTitle } from "../src/presentation.ts";

const pipePath = process.platform === "win32"
  ? String.raw`\\.\pipe\win-net-switch-plugin-${process.pid}`
  : join(tmpdir(), `win-net-switch-plugin-${process.pid}.sock`);
const requests: Record<string, unknown>[] = [];
const adapters = [
  {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Wi-Fi",
    description: "Wireless adapter",
    status: "Up",
    isEnabled: true,
    isActive: true,
    isWireless: true,
  },
  {
    id: "22222222-2222-2222-2222-222222222222",
    name: "Ethernet",
    description: "Wired adapter",
    status: "Disabled",
    isEnabled: false,
    isActive: false,
    isWireless: false,
  },
];

let server: Server;

before(async () => {
  server = createServer((socket) => {
    socket.setEncoding("utf8");
    let requestText = "";
    socket.on("data", (chunk: string) => {
      requestText += chunk;
      const lineEnd = requestText.indexOf("\n");
      if (lineEnd < 0) {
        return;
      }

      requests.push(JSON.parse(requestText.slice(0, lineEnd)) as Record<string, unknown>);
      socket.end(`${JSON.stringify({ version: 1, ok: true, adapters, error: null })}\n`);
    });
  });
  await new Promise<void>((resolve, reject) => {
    server.once("error", reject);
    server.listen(pipePath, resolve);
  });
});

after(async () => {
  await new Promise<void>((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
  });
});

test("client sends typed list, toggle, and cycle requests", async () => {
  const client = new WinNetSwitchClient(pipePath);

  assert.equal((await client.listAdapters()).length, 2);
  assert.equal((await client.toggleAdapter(adapters[0]!.id)).length, 2);
  assert.equal((await client.cycleAdapters()).length, 2);

  assert.deepEqual(requests, [
    { version: 1, command: "list" },
    { version: 1, command: "toggle", adapterId: adapters[0]!.id },
    { version: 1, command: "cycle" },
  ]);
});

test("adapter lists are coalesced briefly and invalidated by mutations", async () => {
  const requestOffset = requests.length;
  const client = new WinNetSwitchClient(pipePath);

  await Promise.all([
    client.listAdapters(),
    client.listAdapters(),
    client.listAdapters(),
  ]);
  await client.listAdapters();
  await client.toggleAdapter(adapters[0]!.id);
  await client.listAdapters();

  assert.deepEqual(requests.slice(requestOffset), [
    { version: 1, command: "list" },
    { version: 1, command: "toggle", adapterId: adapters[0]!.id },
    { version: 1, command: "list" },
  ]);
});

test("titles expose adapter and active cycle state", () => {
  assert.equal(formatAdapterTitle(adapters[0]!), "Wi-Fi\nON");
  assert.equal(formatCycleTitle(adapters), "Active\nWi-Fi");
});

test("missing companion produces an actionable error", async () => {
  const client = new WinNetSwitchClient(`${pipePath}.missing`);
  await assert.rejects(
    client.listAdapters(),
    /Install or start the required WinNetSwitch companion application/,
  );
});
