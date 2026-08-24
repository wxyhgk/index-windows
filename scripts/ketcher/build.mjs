import { build } from "esbuild";
import { fileURLToPath } from "node:url";
import path from "node:path";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const outputDir = path.resolve(scriptDir, "../../src/Index/Assets/Ketcher");

await build({
  absWorkingDir: scriptDir,
  entryPoints: ["editor-entry.jsx"],
  bundle: true,
  minify: true,
  format: "iife",
  target: "chrome120",
  outfile: path.join(outputDir, "ketcher-bundle.js"),
  inject: [path.join(scriptDir, "process-shim.js")],
  define: {
    "process.env.NODE_ENV": JSON.stringify("production")
  },
  loader: {
    ".woff": "dataurl",
    ".woff2": "dataurl",
    ".ttf": "dataurl"
  },
  logLevel: "info"
});
