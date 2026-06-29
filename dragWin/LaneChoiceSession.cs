namespace DragWin;

public sealed class LaneChoiceSession
{
    private readonly long[] choiceOrder;
    private readonly HashSet<int> physicalLanes;
    private readonly Dictionary<long, int> laneByCar;
    private readonly HashSet<int> lockedLanes = [];
    private readonly HashSet<long> chosenCars = [];
    private int choiceIndex;

    public LaneChoiceSession(
        IReadOnlyList<RoundEntry> entries,
        IReadOnlyCollection<int> availablePhysicalLanes)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(availablePhysicalLanes);
        if (entries.Count == 0)
        {
            throw new ArgumentException("At least one entry is required.", nameof(entries));
        }

        physicalLanes = availablePhysicalLanes.ToHashSet();
        laneByCar = entries.ToDictionary(entry => entry.Car.Id, entry => entry.LaneNumber);
        choiceOrder = entries
            .OrderBy(entry => entry.LaneChoiceOrder)
            .Select(entry => entry.Car.Id)
            .ToArray();

        if (physicalLanes.Count != availablePhysicalLanes.Count ||
            laneByCar.Values.Distinct().Count() != laneByCar.Count ||
            laneByCar.Values.Any(lane => !physicalLanes.Contains(lane)))
        {
            throw new ArgumentException("Initial lane assignments must be unique and available.");
        }
    }

    public bool IsComplete => choiceIndex >= choiceOrder.Length;

    public long? CurrentCarId => IsComplete ? null : choiceOrder[choiceIndex];

    public IReadOnlySet<int> LockedLanes => lockedLanes;

    public IReadOnlyCollection<int> AvailableLanes =>
        physicalLanes.Where(lane => !lockedLanes.Contains(lane)).Order().ToArray();

    public int GetLane(long carId) => laneByCar[carId];

    public bool HasChosen(long carId) => chosenCars.Contains(carId);

    public IReadOnlyDictionary<long, int> Assignments => laneByCar;

    public void Choose(long carId, int selectedLane)
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("All lane choices are complete.");
        }
        if (CurrentCarId != carId)
        {
            throw new InvalidOperationException("This car is not the current lane chooser.");
        }
        if (!physicalLanes.Contains(selectedLane) || lockedLanes.Contains(selectedLane))
        {
            throw new InvalidOperationException("That lane is not available.");
        }

        var originalLane = laneByCar[carId];
        var displacedCarId = laneByCar
            .Where(assignment =>
                assignment.Key != carId && assignment.Value == selectedLane)
            .Select(assignment => (long?)assignment.Key)
            .SingleOrDefault();

        laneByCar[carId] = selectedLane;
        if (displacedCarId.HasValue)
        {
            laneByCar[displacedCarId.Value] = originalLane;
        }

        lockedLanes.Add(selectedLane);
        chosenCars.Add(carId);
        choiceIndex++;
    }
}
