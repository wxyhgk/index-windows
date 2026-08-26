# Recognition plugins

Index exposes image recognition through typed plugins instead of feature-specific clients.

## Contract

- `RecognitionCapability` is an extensible string identifier. Built-in examples are
  `molecule.structure` and `chemical.formula`.
- `IRecognitionPlugin<TOutput>` keeps each capability's output strongly typed.
- `RecognitionPluginDescriptor.Id` is stable across releases and settings migrations.
- `RecognitionPluginRegistry` selects the compatible plugin with the highest priority;
  callers may request a specific plugin ID.
- `ShotRecognitionWorkflow<TOutput>` owns asset fallback, media type selection, plugin
  resolution, cancellation, and the `Pending/Success/Empty/Error/Cancelled` state machine.
- `IRecognitionOutputPolicy<TOutput>` maps a strongly typed domain result to a terminal
  workflow status without teaching the generic workflow about molecules or formulas.
- Transport DTOs and model-specific details stay inside the platform plugin adapter.
- `IRecognitionServiceHost` exposes readiness and ownership without leaking process APIs
  into Core. The Windows `LocalRecognitionHost` attaches to an existing compatible
  service or starts a local one, and only terminates processes it owns.

MolGrapher is registered as `molgrapher.local` and returns
`MoleculeRecognitionResult`. A future formula recognizer should define its own result
record and implement `IRecognitionPlugin<FormulaRecognitionResult>` with capability
`RecognitionCapabilities.ChemicalFormula`. It should also provide a formula output policy
and a UI result presenter; the screenshot workflow itself is reused unchanged.

## Loading policy

Plugins are currently in-process and explicitly allowlisted by the composition root in
`src/Index/Program.cs`. Do not scan and execute arbitrary DLLs from a writable folder.
If third-party binary plugins are introduced later, add manifest validation, API version
negotiation, trust controls, dependency isolation, and failure containment first.
