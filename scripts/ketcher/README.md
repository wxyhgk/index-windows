# Ketcher web assets

This directory builds the offline Ketcher editor loaded by the Windows WebView2 host.

Requirements: Node.js 24.14.1 or newer and npm.

```powershell
npm install
npm run build
```

The build writes `ketcher-bundle.js` and `ketcher-bundle.css` to
`src/Index/Assets/Ketcher/`. Keep `ketcher-react`, `ketcher-standalone`, and
`ketcher-core` on the same version.
