namespace DragWin;

public static class DemoTournamentFactory
{
    public static Tournament Create(RaceRepository repository)
    {
        var racers = repository.GetRacers();
        var existingCars = repository.GetCars();
        var cars = new List<Car>();
        foreach (var name in new[] { "Alex", "Jordan", "Morgan", "Sam" })
        {
            var racerName = $"Sample {name}";
            var racer = racers.FirstOrDefault(item => item.Name == racerName)
                ?? repository.AddRacer(racerName);
            foreach (var carName in new[] { "Lightning", "Comet" })
            {
                cars.Add(existingCars.FirstOrDefault(item => item.RacerId == racer.Id && item.Name == carName)
                    ?? repository.AddCar(racer.Id, carName, 10000));
            }
        }

        var tournament = repository.CreateTournament(
            $"Demo Tournament {DateTime.Now:yyyy-MM-dd HH:mm:ss}", 4, cars.Select(car => car.Id).ToArray());
        repository.SaveRound(tournament.Id, new TournamentPlanner().CreateRound(cars, 4, 1));
        return tournament;
    }
}
