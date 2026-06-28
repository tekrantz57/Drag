namespace DragWin;

public sealed class TournamentSetupForm : Form
{
    private readonly RaceRepository repository;
    private readonly TournamentPlanner planner = new();
    private readonly TextBox racerNameInput = new() { Width = 150 };
    private readonly ComboBox racerSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 170,
        DisplayMember = nameof(Racer.Name)
    };
    private readonly TextBox carNameInput = new() { Width = 150 };
    private readonly NumericUpDown dialInput = new()
    {
        DecimalPlaces = 3,
        Increment = 0.001M,
        Minimum = 0.100M,
        Maximum = 60.000M,
        Value = 10.000M,
        Width = 80
    };
    private readonly CheckedListBox carList = new()
    {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        DisplayMember = nameof(Car.DisplayName)
    };
    private readonly TextBox tournamentNameInput = new()
    {
        Text = $"Tournament {DateTime.Now:yyyy-MM-dd}",
        Width = 210
    };
    private readonly ComboBox laneCountSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 50
    };
    private readonly TextBox roundPreview = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Dock = DockStyle.Fill,
        Font = new Font(FontFamily.GenericMonospace, 9)
    };

    public TournamentSetupForm(RaceRepository repository)
    {
        this.repository = repository;
        Text = "Racers, Cars, and Tournament Setup";
        MinimumSize = new Size(850, 600);
        StartPosition = FormStartPosition.CenterParent;

        laneCountSelector.Items.AddRange(["2", "4"]);
        laneCountSelector.SelectedItem = "4";

        var addRacerButton = new Button { Text = "Add Racer", AutoSize = true };
        var addCarButton = new Button { Text = "Add Car", AutoSize = true };
        var generateButton = new Button
        {
            Text = "Create Tournament and Round 1",
            AutoSize = true
        };
        addRacerButton.Click += (_, _) => AddRacer();
        addCarButton.Click += (_, _) => AddCar();
        generateButton.Click += (_, _) => GenerateRound();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 350
        };
        split.Panel1.Controls.Add(carList);
        split.Panel2.Controls.Add(roundPreview);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateFlowRow(
            new Label { AutoSize = true, Text = "Racer:", Margin = LabelMargin },
            racerNameInput,
            addRacerButton), 0, 0);
        layout.Controls.Add(CreateFlowRow(
            new Label { AutoSize = true, Text = "Owner:", Margin = LabelMargin },
            racerSelector,
            new Label { AutoSize = true, Text = "Car:", Margin = LabelMargin },
            carNameInput,
            new Label { AutoSize = true, Text = "Dial:", Margin = LabelMargin },
            dialInput,
            new Label { AutoSize = true, Text = "sec", Margin = LabelMargin },
            addCarButton), 0, 1);
        layout.Controls.Add(CreateFlowRow(
            new Label { AutoSize = true, Text = "Tournament:", Margin = LabelMargin },
            tournamentNameInput,
            new Label { AutoSize = true, Text = "Lanes:", Margin = LabelMargin },
            laneCountSelector,
            generateButton), 0, 2);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = $"Database: {repository.DatabasePath}",
            ForeColor = SystemColors.GrayText
        }, 0, 3);
        layout.Controls.Add(split, 0, 4);
        Controls.Add(layout);

        RefreshData();
    }

    private static Padding LabelMargin => new(8, 8, 3, 3);

    private static FlowLayoutPanel CreateFlowRow(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true
        };
        row.Controls.AddRange(controls);
        return row;
    }

    private void AddRacer()
    {
        RunDatabaseAction(() =>
        {
            var racer = repository.AddRacer(racerNameInput.Text);
            racerNameInput.Clear();
            RefreshData();
            racerSelector.SelectedItem = racerSelector.Items
                .Cast<Racer>().Single(item => item.Id == racer.Id);
        });
    }

    private void AddCar()
    {
        if (racerSelector.SelectedItem is not Racer racer)
        {
            MessageBox.Show(this, "Add or select a racer first.", Text);
            return;
        }

        RunDatabaseAction(() =>
        {
            repository.AddCar(
                racer.Id,
                carNameInput.Text,
                decimal.ToInt32(dialInput.Value * 1000M));
            carNameInput.Clear();
            RefreshData();
            racerSelector.SelectedItem = racerSelector.Items
                .Cast<Racer>().Single(item => item.Id == racer.Id);
        });
    }

    private void GenerateRound()
    {
        var selectedCars = carList.CheckedItems.Cast<Car>().ToArray();
        if (selectedCars.Length == 0)
        {
            MessageBox.Show(this, "Select at least one car.", Text);
            return;
        }

        var laneCount = int.Parse((string)laneCountSelector.SelectedItem!);
        RunDatabaseAction(() =>
        {
            var tournament = repository.CreateTournament(
                tournamentNameInput.Text,
                laneCount,
                selectedCars.Select(car => car.Id).ToArray());
            var round = planner.CreateRound(selectedCars, laneCount, 1);
            repository.SaveRound(tournament.Id, round);
            ShowRound(round);
        });
    }

    private void ShowRound(RoundPlan round)
    {
        var lines = new List<string>
        {
            $"ROUND {round.RoundNumber}    RANDOM SEED {round.RandomSeed}",
            string.Empty
        };

        foreach (var heat in round.Heats)
        {
            lines.Add($"HEAT {heat.HeatNumber} — {heat.AdvanceCount} advance");
            foreach (var entry in heat.Entries.OrderBy(entry => entry.LaneNumber))
            {
                lines.Add(
                    $"  Lane {entry.LaneNumber}: {entry.Car.DisplayName}" +
                    (entry.IsBye ? "  [BYE PASS — ADVANCES]" : string.Empty));
            }

            var duplicateOwners = heat.Entries
                .GroupBy(entry => entry.Car.RacerId)
                .Where(group => group.Count() > 1)
                .Select(group => group.First().Car.RacerName)
                .ToArray();
            if (duplicateOwners.Length > 0)
            {
                lines.Add($"  WARNING: same owner — {string.Join(", ", duplicateOwners)}");
            }
            lines.Add(string.Empty);
        }

        roundPreview.Lines = lines.ToArray();
    }

    private void RefreshData()
    {
        var selectedRacerId = (racerSelector.SelectedItem as Racer)?.Id;
        racerSelector.DataSource = repository.GetRacers().ToList();
        if (selectedRacerId.HasValue)
        {
            racerSelector.SelectedItem = racerSelector.Items
                .Cast<Racer>().FirstOrDefault(item => item.Id == selectedRacerId);
        }

        carList.Items.Clear();
        foreach (var car in repository.GetCars())
        {
            carList.Items.Add(car);
        }
    }

    private void RunDatabaseAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Operation failed");
        }
    }
}
