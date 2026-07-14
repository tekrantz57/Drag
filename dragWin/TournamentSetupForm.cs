namespace DragWin;

public sealed class TournamentSetupForm : Form
{
    private readonly RaceRepository repository;
    private readonly TournamentPlanner planner = new();
    private bool setupActionRunning;
    private DateTimeOffset lastSetupButtonActionAt;
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
        MinimumSize = new Size(1100, 640);
        Size = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterParent;

        laneCountSelector.Items.AddRange(["2", "4"]);
        laneCountSelector.SelectedItem = "4";

        var addRacerButton = new Button { Text = "Add Racer", AutoSize = true, MinimumSize = new Size(90, 0) };
        var addCarButton = new Button { Text = "Add Car", AutoSize = true, MinimumSize = new Size(80, 0) };
        var updateCarButton = new Button { Text = "Update Car Default", AutoSize = true, MinimumSize = new Size(145, 0) };
        var retireCarButton = new Button { Text = "Retire Car", AutoSize = true, MinimumSize = new Size(90, 0) };
        var generateButton = new Button
        {
            Text = "Create Tournament and Round 1",
            AutoSize = false,
            Size = new Size(240, 28)
        };
        addRacerButton.Click += (_, _) => RunSetupButtonAction(addRacerButton, AddRacer);
        addCarButton.Click += (_, _) => RunSetupButtonAction(addCarButton, AddCar);
        updateCarButton.Click += (_, _) => RunSetupButtonAction(updateCarButton, UpdateSelectedCar);
        retireCarButton.Click += (_, _) => RunSetupButtonAction(retireCarButton, RetireSelectedCar);
        generateButton.Click += (_, _) => RunSetupButtonAction(generateButton, GenerateRound);
        carList.SelectedIndexChanged += (_, _) => LoadSelectedCarIntoEditor();

        var carsGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Available Cars",
            Padding = new Padding(8)
        };
        carsGroup.Controls.Add(carList);

        var previewGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Generated Round Preview",
            Padding = new Padding(8)
        };
        previewGroup.Controls.Add(roundPreview);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 380
        };
        split.Panel1.Controls.Add(carsGroup);
        split.Panel2.Controls.Add(previewGroup);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateRacerGroup(addRacerButton), 0, 0);
        layout.Controls.Add(CreateCarGroup(addCarButton, updateCarButton, retireCarButton), 1, 0);
        layout.Controls.Add(CreateTournamentGroup(generateButton), 2, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = $"Database: {repository.DatabasePath}",
            ForeColor = SystemColors.GrayText
        }, 0, 1);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 1)!, 3);
        layout.Controls.Add(split, 0, 2);
        layout.SetColumnSpan(split, 3);
        Controls.Add(layout);

        RefreshData();
    }

    private static Padding LabelMargin => new(3, 8, 6, 3);
    private static Padding ControlMargin => new(3, 3, 12, 3);

    private Control CreateRacerGroup(Button addRacerButton)
    {
        var group = new GroupBox
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Racers",
            Padding = new Padding(8)
        };
        var table = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { AutoSize = true, Text = "Racer:", Margin = LabelMargin }, 0, 0);
        racerNameInput.Dock = DockStyle.Fill;
        racerNameInput.Margin = ControlMargin;
        table.Controls.Add(racerNameInput, 1, 0);
        table.Controls.Add(addRacerButton, 2, 0);
        group.Controls.Add(table);
        return group;
    }

    private Control CreateCarGroup(
        Button addCarButton,
        Button updateCarButton,
        Button retireCarButton)
    {
        var group = new GroupBox
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Cars",
            Padding = new Padding(8)
        };
        var table = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 6,
            RowCount = 2
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(new Label { AutoSize = true, Text = "Racer:", Margin = LabelMargin }, 0, 0);
        racerSelector.Dock = DockStyle.Fill;
        racerSelector.Margin = ControlMargin;
        table.Controls.Add(racerSelector, 1, 0);
        table.Controls.Add(new Label { AutoSize = true, Text = "Car:", Margin = LabelMargin }, 2, 0);
        carNameInput.Dock = DockStyle.Fill;
        carNameInput.Margin = ControlMargin;
        table.Controls.Add(carNameInput, 3, 0);
        table.Controls.Add(new Label { AutoSize = true, Text = "Default dial:", Margin = LabelMargin }, 4, 0);
        dialInput.Dock = DockStyle.Fill;
        dialInput.Margin = new Padding(3, 3, 3, 3);
        table.Controls.Add(dialInput, 5, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0)
        };
        buttonPanel.Controls.AddRange([addCarButton, updateCarButton, retireCarButton]);
        table.Controls.Add(buttonPanel, 1, 1);
        table.SetColumnSpan(buttonPanel, 5);

        group.Controls.Add(table);
        return group;
    }

    private Control CreateTournamentGroup(Button generateButton)
    {
        var group = new GroupBox
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Tournament",
            Padding = new Padding(8),
            MinimumSize = new Size(330, 140)
        };
        var table = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(new Label { AutoSize = true, Text = "Name:", Margin = LabelMargin }, 0, 0);
        tournamentNameInput.Dock = DockStyle.None;
        tournamentNameInput.Margin = ControlMargin;
        tournamentNameInput.Width = 230;
        table.Controls.Add(tournamentNameInput, 1, 0);
        table.Controls.Add(new Label { AutoSize = true, Text = "Lanes:", Margin = LabelMargin }, 0, 1);
        laneCountSelector.Width = 70;
        laneCountSelector.Margin = ControlMargin;
        table.Controls.Add(laneCountSelector, 1, 1);
        generateButton.Margin = ControlMargin;
        table.Controls.Add(generateButton, 1, 2);

        group.Controls.Add(table);
        return group;
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

    private void UpdateSelectedCar()
    {
        if (carList.SelectedItem is not Car car)
        {
            MessageBox.Show(this, "Select a car to update.", Text);
            return;
        }
        if (racerSelector.SelectedItem is not Racer racer)
        {
            MessageBox.Show(this, "Select the car racer.", Text);
            return;
        }

        var checkedCarIds = carList.CheckedItems.Cast<Car>()
            .Select(item => item.Id)
            .ToHashSet();
        RunDatabaseAction(() =>
        {
            var updatedCar = repository.UpdateCar(
                car.Id,
                racer.Id,
                carNameInput.Text,
                decimal.ToInt32(dialInput.Value * 1000M));
            RefreshData(checkedCarIds);
            carList.SelectedItem = carList.Items
                .Cast<Car>()
                .Single(item => item.Id == updatedCar.Id);
        });
    }

    private void RetireSelectedCar()
    {
        if (carList.SelectedItem is not Car car)
        {
            MessageBox.Show(this, "Select a car to retire.", Text);
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Retire {car.DisplayName}?\n\nIt will be hidden from future tournament setup, but existing tournament history will be kept.",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
        {
            return;
        }

        var checkedCarIds = carList.CheckedItems.Cast<Car>()
            .Where(item => item.Id != car.Id)
            .Select(item => item.Id)
            .ToHashSet();
        RunDatabaseAction(() =>
        {
            repository.RetireCar(car.Id);
            carNameInput.Clear();
            RefreshData(checkedCarIds);
        });
    }

    private void LoadSelectedCarIntoEditor()
    {
        if (carList.SelectedItem is not Car car)
        {
            return;
        }

        racerSelector.SelectedItem = racerSelector.Items
            .Cast<Racer>()
            .FirstOrDefault(item => item.Id == car.RacerId);
        carNameInput.Text = car.Name;
        dialInput.Value = Math.Clamp(
            car.DefaultDialMilliseconds / 1000M,
            dialInput.Minimum,
            dialInput.Maximum);
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

            var duplicateRacers = heat.Entries
                .GroupBy(entry => entry.Car.RacerId)
                .Where(group => group.Count() > 1)
                .Select(group => group.First().Car.RacerName)
                .ToArray();
            if (duplicateRacers.Length > 0)
            {
                lines.Add($"  WARNING: same racer — {string.Join(", ", duplicateRacers)}");
            }
            lines.Add(string.Empty);
        }

        roundPreview.Lines = lines.ToArray();
    }

    private void RefreshData(IReadOnlySet<long>? checkedCarIds = null)
    {
        var selectedRacerId = (racerSelector.SelectedItem as Racer)?.Id;
        var selectedCarId = (carList.SelectedItem as Car)?.Id;
        racerSelector.DataSource = repository.GetRacers().ToList();
        if (selectedRacerId.HasValue)
        {
            racerSelector.SelectedItem = racerSelector.Items
                .Cast<Racer>().FirstOrDefault(item => item.Id == selectedRacerId);
        }

        carList.Items.Clear();
        foreach (var car in repository.GetCars())
        {
            var index = carList.Items.Add(car);
            if (checkedCarIds?.Contains(car.Id) == true)
            {
                carList.SetItemChecked(index, true);
            }
            if (selectedCarId == car.Id)
            {
                carList.SelectedIndex = index;
            }
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

    private void RunSetupButtonAction(Button button, Action action)
    {
        var now = DateTimeOffset.Now;
        if (now - lastSetupButtonActionAt < TimeSpan.FromMilliseconds(700))
        {
            return;
        }
        if (setupActionRunning)
        {
            return;
        }

        setupActionRunning = true;
        lastSetupButtonActionAt = now;
        var wasEnabled = button.Enabled;
        button.Enabled = false;
        try
        {
            action();
        }
        finally
        {
            setupActionRunning = false;
            if (!IsDisposed)
            {
                button.Enabled = wasEnabled;
            }
        }
    }
}
