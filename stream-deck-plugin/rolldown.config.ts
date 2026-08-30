import { exec } from "node:child_process";
import { defineConfig } from "rolldown";

const isWatching = Boolean(process.env.ROLLUP_WATCH);
const pluginUuid = "dev.witqq.win-net-switch";
const pluginDirectory = `${pluginUuid}.sdPlugin`;

export default defineConfig({
  input: "src/plugin.ts",
  output: {
    file: `${pluginDirectory}/bin/plugin.js`,
    minify: !isWatching,
    sourcemap: isWatching,
  },
  platform: "node",
  resolve: {
    conditionNames: ["node"],
  },
  transform: {
    decorator: {
      legacy: true,
    },
  },
  plugins: [
    {
      name: "watch-manifest",
      buildStart() {
        this.addWatchFile(`${pluginDirectory}/manifest.json`);
      },
      buildEnd() {
        if (isWatching) {
          exec(`streamdeck restart ${pluginUuid}`);
        }
      },
    },
    {
      name: "emit-module-package-file",
      generateBundle() {
        this.emitFile({
          fileName: "package.json",
          source: "{ \"type\": \"module\" }",
          type: "asset",
        });
      },
    },
  ],
});
