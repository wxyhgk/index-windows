namespace Index.Molecule;

public sealed record MoleculeRecognitionResult(
    string? Smiles,
    double Confidence,
    string? Sdf,
    int ProcessingTimeMs,
    string? Error);
