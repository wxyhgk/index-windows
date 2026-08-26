using Index.Recognition;

namespace Index.Molecule;

public sealed class MoleculeRecognitionOutputPolicy
    : IRecognitionOutputPolicy<MoleculeRecognitionResult>
{
    public RecognitionOutputEvaluation Evaluate(MoleculeRecognitionResult output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (!string.IsNullOrWhiteSpace(output.Error))
        {
            return RecognitionOutputEvaluation.Failure(output.Error);
        }

        return string.IsNullOrWhiteSpace(output.Sdf)
            && string.IsNullOrWhiteSpace(output.Smiles)
            ? RecognitionOutputEvaluation.Empty()
            : RecognitionOutputEvaluation.Success();
    }
}
