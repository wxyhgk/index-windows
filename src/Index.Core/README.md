# Index.Core

`Index.Core` is the platform-independent compilation boundary shared by the Windows app and
unit tests. It owns actions, annotations, capture values, clipboard models, pin logic, rendering,
settings, storage, and toolbar contracts.

Rules:

- Do not reference WinUI, Windows Runtime UI types, P/Invoke, or `System.Drawing` here.
- Platform contracts may be included; concrete Windows implementations belong to
  `src/Index.Windows/` or the WinUI executable project.
- Tests reference this project instead of compiling production domain files again.
- Source files live under this project. Moving a type across the Core/Windows/UI boundary must
  update its physical location as well as its project reference.
