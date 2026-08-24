import React from "react";
import { createRoot } from "react-dom/client";
import { Editor } from "ketcher-react";
import { StandaloneStructServiceProvider } from "ketcher-standalone";
import "ketcher-react/dist/index.css";

const provider = new StandaloneStructServiceProvider();

function reportError(message) {
  const text = message instanceof Error ? message.message : String(message);
  window.ketcherLastError = text;
  window.chrome?.webview?.postMessage({ type: "ketcher-error", message: text });
}

function App() {
  return (
    <Editor
      staticResourcesUrl="./"
      structServiceProvider={provider}
      disableMacromoleculesEditor
      buttons={{
        miew: { hidden: true },
        recognize: { hidden: true }
      }}
      errorHandler={reportError}
      onInit={(ketcher) => {
        window.ketcher = ketcher;
        window.ketcherReady = true;
        window.chrome?.webview?.postMessage({ type: "ketcher-ready" });

        if (window.pendingMolecule) {
          const molecule = window.pendingMolecule;
          window.pendingMolecule = null;
          ketcher.setMolecule(molecule).catch(reportError);
        }
      }}
    />
  );
}

window.ketcherReady = false;
window.ketcherLastError = null;
window.setMolecule = async (molecule) => {
  if (!window.ketcher) {
    window.pendingMolecule = molecule;
    return false;
  }

  await window.ketcher.setMolecule(molecule);
  return true;
};
window.getSmiles = async () => window.ketcher?.getSmiles() ?? "";
window.getMolfile = async () => window.ketcher?.getMolfile() ?? "";
window.isReady = () => window.ketcherReady === true;

createRoot(document.getElementById("root")).render(<App />);
