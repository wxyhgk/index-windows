# Recognition plugins

Index exposes image recognition through typed plugins instead of feature-specific clients.

## Contract

- `RecognitionCapability` is an extensible string identifier. Built-in examples are
  `molecule.structure` and `chemical.formula`.
- `IRecognitionPlugin<TOutput>` keeps each capability's output strongly typed.
- `RecognitionPluginDescriptor.Id` is stable across releases and settings migrations.
- `RecognitionPluginRegistry` selects the compatible plugin with the highest priority;
  callers may request a specific plugin ID.
- Transport DTOs and model-specific details stay inside the platform plugin adapter.

MolGrapher is registered as `molgrapher.local` and returns
`MoleculeRecognitionResult`. A future formula recognizer should define its own result
record and implement `IRecognitionPlugin<FormulaRecognitionResult>` with capability
`RecognitionCapabilities.ChemicalFormula`.

## Loading policy

Plugins are currently in-process and explicitly allowlisted by the composition root in
`src/Index/Program.cs`. Do not scan and execute arbitrary DLLs from a writable folder.
If third-party binary plugins are introduced later, add manifest validation, API version
negotiation, trust controls, dependency isolation, and failure containment first.
