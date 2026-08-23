namespace YtecStickyNote.Services;

public sealed class WindowLayoutSession
{
    private readonly int _requiredStableSamples;
    private string? _candidateLayoutId;
    private int _candidateSampleCount;

    public WindowLayoutSession(int requiredStableSamples = 3)
    {
        if (requiredStableSamples < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredStableSamples));
        }

        _requiredStableSamples = requiredStableSamples;
    }

    public string LayoutId { get; private set; } = string.Empty;

    public bool IsDisplayTransition { get; private set; }

    public bool PlacementDirty { get; private set; }

    public void Initialize(string layoutId, bool placementDirty)
    {
        LayoutId = layoutId;
        PlacementDirty = placementDirty;
        IsDisplayTransition = false;
        ResetCandidate();
    }

    public void MarkUserPlacementChanged()
    {
        if (!IsDisplayTransition)
        {
            PlacementDirty = true;
        }
    }

    public void BeginDisplayTransition()
    {
        IsDisplayTransition = true;
        ResetCandidate();
    }

    public string? ObserveDisplayLayout(string layoutId)
    {
        if (!IsDisplayTransition)
        {
            return null;
        }

        if (string.Equals(layoutId, _candidateLayoutId, StringComparison.Ordinal))
        {
            _candidateSampleCount++;
        }
        else
        {
            _candidateLayoutId = layoutId;
            _candidateSampleCount = 1;
        }

        return _candidateSampleCount >= _requiredStableSamples ? _candidateLayoutId : null;
    }

    public void ApplyStableLayout(string layoutId, bool placementDirty)
    {
        LayoutId = layoutId;
        PlacementDirty = placementDirty;
        IsDisplayTransition = false;
        ResetCandidate();
    }

    public bool CanSavePlacement(string currentLayoutId)
    {
        return PlacementDirty &&
               !IsDisplayTransition &&
               string.Equals(currentLayoutId, LayoutId, StringComparison.Ordinal);
    }

    public void MarkPlacementSaved()
    {
        PlacementDirty = false;
    }

    private void ResetCandidate()
    {
        _candidateLayoutId = null;
        _candidateSampleCount = 0;
    }
}
